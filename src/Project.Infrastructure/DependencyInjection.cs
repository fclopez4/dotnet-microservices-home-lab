using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Project.Application.Interfaces;
using Project.Application.Ports;
using Project.Domain.Ports;
using Project.Infrastructure.Messaging;
using Project.Infrastructure.Messaging.Consumers;
using Project.Infrastructure.Persistence;
using Project.Infrastructure.Persistence.Configuration;
using Project.Infrastructure.Persistence.Repositories;
using Project.Infrastructure.Resilience;
using Project.Infrastructure.Security;

namespace Project.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // MongoDB mappings
        MongoDbMappings.Register();

        // MongoDB context
        services.AddSingleton(_ => new MongoDbContext(
            configuration.GetConnectionString("MongoDB")!,
            configuration["MongoDB:DatabaseName"] ?? "project"));

        // Resilience pipelines (Polly)
        services.AddResiliencePipelines();

        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        // Read repositories (CQRS read side)
        services.AddScoped<IUserReadRepository, UserReadRepository>();

        // MassTransit + RabbitMQ + MongoDB Outbox
        services.AddMassTransit(bus =>
        {
            bus.AddConsumer<SendEmailConsumer>();

            bus.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(new Uri(configuration.GetConnectionString("RabbitMQ")!));

                cfg.ReceiveEndpoint("email_queue", e =>
                {
                    // Quorum queue for durability
                    e.SetQuorumQueue();

                    // Retry policy on consumer failures
                    e.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));

                    e.ConfigureConsumer<SendEmailConsumer>(context);
                });

                cfg.ConfigureEndpoints(context);
            });

            // MongoDB Outbox - guaranteed message delivery
            bus.AddMongoDbOutbox(o =>
            {
                o.ClientFactory(provider =>
                {
                    var ctx = provider.GetRequiredService<MongoDbContext>();
                    return ctx.Client;
                });
                o.DatabaseFactory(provider =>
                {
                    var ctx = provider.GetRequiredService<MongoDbContext>();
                    return ctx.Database;
                });

                o.DuplicateDetectionWindow = TimeSpan.FromSeconds(30);
                o.UseBusOutbox();
            });
        });

        // IEmailQueue implementation via MassTransit
        services.AddScoped<IEmailQueue, MassTransitEmailQueue>();

        // Redis distributed cache
        var redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrEmpty(redisConnection) && !redisConnection.Contains("CONFIGURE_IN"))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnection;
                options.InstanceName = "project:";
            });
        }
        else
        {
            // Fallback to in-memory cache for development without Redis
            services.AddDistributedMemoryCache();
        }

        // Security
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();

        // Migrations
        services.AddScoped<Migrations.MigrationRunner>();

        return services;
    }
}
