using Prometheus;

namespace BCHealthMonitor.Endpoints;

public static class MetricsEndpoints
{
    public static void MapMetricsEndpoints(this WebApplication app)
    {
        // Prometheus metrics endpoint
        app.MapGet("/metrics", async context =>
        {
            await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(context.Response.Body);
        })
        .WithTags("Metrics")
        .WithName("GetPrometheusMetrics")
        .WithSummary("Get Prometheus-format metrics")
        .ExcludeFromDescription();
    }
}
