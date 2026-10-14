using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TaskApi.Messaging;
using TaskApi.Persistence;

namespace TaskApi.Observability;

public static class ObservabilityExtensions
{
    private const string ReadyTag = "ready";

    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        var openTelemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddNpgsql()
                .AddSource(OutboxDispatcher.ActivitySourceName, "RabbitMQ.Client.Publisher"))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddMeter("Npgsql"))
            .WithLogging();

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            openTelemetry.UseOtlpExporter();
        }

        builder.Services.AddHealthChecks()
            .AddDbContextCheck<TaskDbContext>("postgres", tags: [ReadyTag])
            .AddCheck<OutboxHealthCheck>("outbox", tags: [ReadyTag]);

        return builder;
    }

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) });

        return app;
    }
}
