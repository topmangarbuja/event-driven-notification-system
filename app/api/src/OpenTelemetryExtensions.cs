using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace src;

public static class OpenTelemetryExtensions
{
    // Check if the environment variable "OTEL_EXPORTER_OTLP_ENDPOINT" is set. If it is not set, OpenTelemetry will not be configured.
    // This allows the application to run without OpenTelemetry if the environment variable is not set.
    public static void AddOpenTelemetryConfiguration(this WebApplicationBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
        {
            return;
        }

        //1. Configure OpenTelemetry logging
        builder.Logging.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
        });

        //2. Configure OpenTelemetry tracing and metrics
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "api"))
            .WithTracing(t => t.AddAspNetCoreInstrumentation()
                .AddSource(MessageTracing.ActivitySourceName))
            .WithMetrics(m => m.AddAspNetCoreInstrumentation());

        //3. Configure OpenTelemetry exporter
        builder.Services.AddOpenTelemetry().UseOtlpExporter();
    }
}