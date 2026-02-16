using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Project.Application.DTOs;
using Project.Domain.Ports;
using Testcontainers.MongoDb;
using Testcontainers.Redis;

namespace Project.Integration.Tests.Fixtures;

[CollectionDefinition("Integration")]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>;

public class IntegrationTestFixture : IAsyncLifetime
{
    private MongoDbContainer _mongoContainer = null!;
    private RedisContainer _redisContainer = null!;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public InMemoryEmailQueue EmailQueue { get; } = new();
    public List<Activity> ExportedActivities { get; } = [];
    public List<Metric> ExportedMetrics { get; } = [];

    public string MongoConnectionString => _mongoContainer.GetConnectionString();
    public string RedisConnectionString => _redisContainer.GetConnectionString();

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("DOCKER_API_VERSION", "1.41");

        _mongoContainer = new MongoDbBuilder("mongo:7").Build();
        _redisContainer = new RedisBuilder("redis:7-alpine").Build();

        await Task.WhenAll(
            _mongoContainer.StartAsync(),
            _redisContainer.StartAsync());

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");

                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:MongoDB"] = MongoConnectionString,
                        ["ConnectionStrings:Redis"] = RedisConnectionString,
                        ["ConnectionStrings:RabbitMQ"] = "amqp://guest:guest@localhost:5672",
                        ["MongoDB:DatabaseName"] = $"test_{Guid.NewGuid():N}",
                        ["Jwt:Secret"] = "integration-test-secret-key-minimum-32-characters-long!!",
                        ["Jwt:Issuer"] = "Project.Tests",
                        ["Jwt:Audience"] = "Project.Tests",
                        ["Jwt:ExpirationMinutes"] = "60",
                        ["Sentry:Dsn"] = "",
                    });
                });

                builder.ConfigureServices(services =>
                {
                    // Remove MassTransit services (bus, hosted services, outbox)
                    // to avoid RabbitMQ connection attempts during tests
                    var massTransitDescriptors = services.Where(d =>
                        d.ServiceType.FullName?.Contains("MassTransit") == true ||
                        d.ImplementationType?.FullName?.Contains("MassTransit") == true).ToList();
                    foreach (var d in massTransitDescriptors)
                        services.Remove(d);

                    // Remove MassTransit hosted services
                    var hostedServices = services.Where(d =>
                        d.ServiceType == typeof(IHostedService) &&
                        (d.ImplementationType?.FullName?.Contains("MassTransit") == true ||
                         d.ImplementationType?.FullName?.Contains("BusOutbox") == true)).ToList();
                    foreach (var d in hostedServices)
                        services.Remove(d);

                    // Replace IEmailQueue with in-memory stub
                    services.RemoveAll<IEmailQueue>();
                    services.AddSingleton<InMemoryEmailQueue>(EmailQueue);
                    services.AddSingleton<IEmailQueue>(sp => sp.GetRequiredService<InMemoryEmailQueue>());

                    // Fix JWT validation key: Program.cs reads jwtSettings before
                    // ConfigureAppConfiguration overrides are applied, so the
                    // validation key differs from the generation key. PostConfigure
                    // ensures the middleware uses the test secret.
                    services.PostConfigure<JwtBearerOptions>(
                        JwtBearerDefaults.AuthenticationScheme, options =>
                        {
                            options.TokenValidationParameters = new TokenValidationParameters
                            {
                                ValidateIssuer = true,
                                ValidateAudience = true,
                                ValidateLifetime = true,
                                ValidateIssuerSigningKey = true,
                                ValidIssuer = "Project.Tests",
                                ValidAudience = "Project.Tests",
                                IssuerSigningKey = new SymmetricSecurityKey(
                                    Encoding.UTF8.GetBytes(
                                        "integration-test-secret-key-minimum-32-characters-long!!"))
                            };
                        });

                    // Replace OTLP exporter with InMemory exporter to avoid
                    // failed connection attempts to localhost:4317 and capture
                    // traces/metrics for test assertions.
                    var otelDescriptors = services.Where(d =>
                        d.ServiceType.FullName?.Contains("OpenTelemetry") == true ||
                        d.ImplementationType?.FullName?.Contains("OpenTelemetry") == true ||
                        d.ServiceType.FullName?.Contains("OtlpExporter") == true ||
                        d.ImplementationType?.FullName?.Contains("OtlpExporter") == true).ToList();
                    foreach (var d in otelDescriptors)
                        services.Remove(d);

                    var otelHostedServices = services.Where(d =>
                        d.ServiceType == typeof(IHostedService) &&
                        d.ImplementationType?.FullName?.Contains("OpenTelemetry") == true).ToList();
                    foreach (var d in otelHostedServices)
                        services.Remove(d);

                    services.AddOpenTelemetry()
                        .WithTracing(tracing => tracing
                            .SetResourceBuilder(ResourceBuilder.CreateDefault()
                                .AddService("Project.Api.Tests"))
                            .AddAspNetCoreInstrumentation()
                            .AddHttpClientInstrumentation()
                            .AddInMemoryExporter(ExportedActivities))
                        .WithMetrics(metrics => metrics
                            .SetResourceBuilder(ResourceBuilder.CreateDefault()
                                .AddService("Project.Api.Tests"))
                            .AddAspNetCoreInstrumentation()
                            .AddHttpClientInstrumentation()
                            .AddInMemoryExporter(ExportedMetrics));
                });
            });
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _mongoContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    // --- Test helpers ---

    public HttpClient CreateClient() => Factory.CreateClient();

    public async Task<(string Token, string RefreshToken, string UserId)> RegisterAndLoginAsync(
        HttpClient client, string? suffix = null)
    {
        suffix ??= Guid.NewGuid().ToString("N")[..8];
        var username = $"testuser_{suffix}";
        var email = $"testuser_{suffix}@test.com";
        const string password = "Password123!";

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(username, email, password));
        registerResponse.EnsureSuccessStatusCode();
        var registerResult = await registerResponse.Content.ReadFromJsonAsync<RegisterUserResponse>();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(username, password));
        loginResponse.EnsureSuccessStatusCode();
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginResult!.Token);

        return (loginResult.Token, loginResult.RefreshToken!, registerResult!.Id);
    }
}
