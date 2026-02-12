using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Project.Application;
using Project.Infrastructure;
using Project.Infrastructure.Migrations;

// Serilog bootstrap
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Project.Worker")
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}  {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddSerilog();

    // Sentry error monitoring
    builder.Services.AddLogging(logging =>
        logging.AddSentry(o =>
        {
            o.Dsn = builder.Configuration["Sentry:Dsn"] ?? "";
            o.Environment = builder.Configuration["DOTNET_ENVIRONMENT"] ?? "Development";
        }));

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // MassTransit consumers are registered via AddInfrastructure → AddMassTransit
    // No need for a manual BackgroundService - MassTransit handles message consumption

    // OpenTelemetry → OTLP Collector
    var otelEndpoint = builder.Configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317";
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Project.Worker"))
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(opt => opt.Endpoint = new Uri(otelEndpoint)))
        .WithMetrics(metrics => metrics
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Project.Worker"))
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(opt => opt.Endpoint = new Uri(otelEndpoint)));

    var host = builder.Build();

    // Run MongoDB migrations
    using (var scope = host.Services.CreateScope())
    {
        var runner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();
        await runner.RunAsync();
    }

    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Worker terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
