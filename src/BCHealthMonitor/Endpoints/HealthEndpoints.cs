using BCHealthMonitor.Models;
using BCHealthMonitor.Services;

namespace BCHealthMonitor.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/health")
            .WithTags("Health");

        // GET /health - Aggregate health
        group.MapGet("/", async (IHealthCheckService healthService) =>
        {
            if (!healthService.IsStartupComplete)
            {
                return Results.Json(
                    new { status = "Starting", message = "Health monitor is starting up" },
                    statusCode: 503);
            }

            var result = await healthService.CheckAggregateHealthAsync();
            return Results.Json(result, statusCode: result.GetHttpStatusCode());
        })
        .WithName("GetAggregateHealth")
        .WithSummary("Get aggregate health status")
        .Produces<HealthResult>(200)
        .Produces<HealthResult>(503)
        .Produces<HealthResult>(504);

        // GET /health/client - Client health for load balancer
        group.MapGet("/client", async (IHealthCheckService healthService) =>
        {
            if (!healthService.IsStartupComplete)
            {
                return Results.Json(
                    new { status = "Starting", message = "Health monitor is starting up" },
                    statusCode: 503);
            }

            var result = await healthService.CheckClientHealthAsync();
            return Results.Json(result, statusCode: result.GetHttpStatusCode());
        })
        .WithName("GetClientHealth")
        .WithSummary("Get client health status for load balancer (Web Client sessions)")
        .Produces<HealthResult>(200)
        .Produces<HealthResult>(503)
        .Produces<HealthResult>(504);

        // GET /health/webservices - Web services health for load balancer
        group.MapGet("/webservices", async (IHealthCheckService healthService) =>
        {
            if (!healthService.IsStartupComplete)
            {
                return Results.Json(
                    new { status = "Starting", message = "Health monitor is starting up" },
                    statusCode: 503);
            }

            var result = await healthService.CheckWebServicesHealthAsync();
            return Results.Json(result, statusCode: result.GetHttpStatusCode());
        })
        .WithName("GetWebServicesHealth")
        .WithSummary("Get web services health status for load balancer (OData/SOAP/API)")
        .Produces<HealthResult>(200)
        .Produces<HealthResult>(503)
        .Produces<HealthResult>(504);

        // GET /health/scheduler - Scheduler health
        group.MapGet("/scheduler", async (IHealthCheckService healthService) =>
        {
            if (!healthService.IsStartupComplete)
            {
                return Results.Json(
                    new { status = "Starting", message = "Health monitor is starting up" },
                    statusCode: 503);
            }

            var result = await healthService.CheckSchedulerHealthAsync();
            return Results.Json(result, statusCode: result.GetHttpStatusCode());
        })
        .WithName("GetSchedulerHealth")
        .WithSummary("Get scheduler health status")
        .Produces<HealthResult>(200)
        .Produces<HealthResult>(503)
        .Produces<HealthResult>(504);

        // GET /health/details - Detailed health for troubleshooting
        group.MapGet("/details", async (IHealthCheckService healthService) =>
        {
            if (!healthService.IsStartupComplete)
            {
                return Results.Json(
                    new { status = "Starting", message = "Health monitor is starting up" },
                    statusCode: 503);
            }

            var result = await healthService.GetDetailedHealthAsync();
            return Results.Json(result, statusCode: result.GetHttpStatusCode());
        })
        .WithName("GetDetailedHealth")
        .WithSummary("Get detailed health status for troubleshooting")
        .Produces<DetailedHealthResult>(200)
        .Produces<DetailedHealthResult>(503)
        .Produces<DetailedHealthResult>(504);
    }
}
