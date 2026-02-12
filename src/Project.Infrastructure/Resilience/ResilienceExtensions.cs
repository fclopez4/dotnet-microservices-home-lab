using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Polly;
using Polly.Retry;

namespace Project.Infrastructure.Resilience;

public static class ResilienceExtensions
{
    public const string MongoDbPipeline = "mongodb";

    public static IServiceCollection AddResiliencePipelines(this IServiceCollection services)
    {
        services.AddResiliencePipeline(MongoDbPipeline, builder =>
        {
            builder
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromSeconds(1),
                    BackoffType = DelayBackoffType.Exponential,
                    ShouldHandle = new PredicateBuilder().Handle<MongoException>()
                })
                .AddCircuitBreaker(new Polly.CircuitBreaker.CircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    MinimumThroughput = 5,
                    BreakDuration = TimeSpan.FromSeconds(15),
                    ShouldHandle = new PredicateBuilder().Handle<MongoException>()
                })
                .AddTimeout(TimeSpan.FromSeconds(10));
        });

        // RabbitMQ resilience is handled by MassTransit's built-in retry and circuit breaker

        return services;
    }
}
