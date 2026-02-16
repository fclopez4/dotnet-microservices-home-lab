using System.Net.Http.Json;
using FluentAssertions;
using Project.Application.DTOs;
using Project.Integration.Tests.Fixtures;

namespace Project.Integration.Tests.Infrastructure;

[Collection("Integration")]
public class OpenTelemetryTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task HttpRequest_GeneratesTraceSpan()
    {
        var client = fixture.CreateClient();
        fixture.ExportedActivities.Clear();

        await client.GetAsync("/health");

        // Allow the InMemory exporter to flush
        await Task.Delay(200);

        fixture.ExportedActivities.Should().Contain(a =>
            a.DisplayName.Contains("GET") ||
            a.OperationName.Contains("Microsoft.AspNetCore"));
    }

    [Fact]
    public async Task AuthenticatedRequest_GeneratesTraceSpan()
    {
        var client = fixture.CreateClient();
        fixture.ExportedActivities.Clear();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"otel_{suffix}", $"otel_{suffix}@test.com", "Password123!"));

        await Task.Delay(200);

        fixture.ExportedActivities.Should().Contain(a =>
            a.DisplayName.Contains("POST") ||
            a.OperationName.Contains("Microsoft.AspNetCore"));
    }

    [Fact]
    public async Task TracingInstrumentation_IncludesHttpAttributes()
    {
        var client = fixture.CreateClient();
        fixture.ExportedActivities.Clear();

        await client.GetAsync("/health");

        await Task.Delay(200);

        var span = fixture.ExportedActivities.FirstOrDefault(a =>
            a.OperationName.Contains("Microsoft.AspNetCore"));

        span.Should().NotBeNull("ASP.NET Core instrumentation should produce a span");

        var tags = span!.Tags.ToDictionary(t => t.Key, t => t.Value);

        // OpenTelemetry semantic conventions for HTTP spans
        tags.Should().ContainKey("url.path");
        tags.Should().ContainKey("http.request.method");
        tags.Should().ContainKey("http.route");
    }
}
