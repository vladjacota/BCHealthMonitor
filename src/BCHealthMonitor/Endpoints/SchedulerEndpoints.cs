using BCHealthMonitor.Models;
using BCHealthMonitor.Services;

namespace BCHealthMonitor.Endpoints;

public static class SchedulerEndpoints
{
    public static void MapSchedulerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/scheduler")
            .WithTags("Scheduler");

        // GET /scheduler - Get scheduler state
        group.MapGet("/", async (ISchedulerControlService schedulerService) =>
        {
            var state = await schedulerService.GetStateAsync();
            return Results.Json(state);
        })
        .WithName("GetSchedulerState")
        .WithSummary("Get current scheduler state")
        .Produces<SchedulerState>(200);

        // POST /scheduler/enable - Enable scheduler
        group.MapPost("/enable", async (
            ISchedulerControlService schedulerService,
            string? duration) =>
        {
            TimeSpan? durationTimeSpan = null;
            
            if (!string.IsNullOrEmpty(duration))
            {
                durationTimeSpan = ParseDuration(duration);
                if (durationTimeSpan == null)
                {
                    return Results.BadRequest(new 
                    { 
                        error = "Invalid duration format",
                        hint = "Use formats like: 30m, 1h, 2h30m, 1d"
                    });
                }
            }

            var success = await schedulerService.EnableAsync(durationTimeSpan);
            
            if (success)
            {
                var state = await schedulerService.GetStateAsync();
                return Results.Json(new
                {
                    success = true,
                    message = durationTimeSpan.HasValue 
                        ? $"Scheduler enabled for {duration}" 
                        : "Scheduler enabled (permanent override)",
                    state
                });
            }

            return Results.Json(new
            {
                success = false,
                message = "Failed to enable scheduler"
            }, statusCode: 500);
        })
        .WithName("EnableScheduler")
        .WithSummary("Enable task scheduler (with optional duration)")
        .Produces(200)
        .Produces(400)
        .Produces(500);

        // POST /scheduler/disable - Disable scheduler
        group.MapPost("/disable", async (
            ISchedulerControlService schedulerService,
            string? duration) =>
        {
            TimeSpan? durationTimeSpan = null;
            
            if (!string.IsNullOrEmpty(duration))
            {
                durationTimeSpan = ParseDuration(duration);
                if (durationTimeSpan == null)
                {
                    return Results.BadRequest(new 
                    { 
                        error = "Invalid duration format",
                        hint = "Use formats like: 30m, 1h, 2h30m, 1d"
                    });
                }
            }

            var success = await schedulerService.DisableAsync(durationTimeSpan);
            
            if (success)
            {
                var state = await schedulerService.GetStateAsync();
                return Results.Json(new
                {
                    success = true,
                    message = durationTimeSpan.HasValue 
                        ? $"Scheduler disabled for {duration}" 
                        : "Scheduler disabled (permanent override)",
                    state
                });
            }

            return Results.Json(new
            {
                success = false,
                message = "Failed to disable scheduler"
            }, statusCode: 500);
        })
        .WithName("DisableScheduler")
        .WithSummary("Disable task scheduler (with optional duration)")
        .Produces(200)
        .Produces(400)
        .Produces(500);

        // DELETE /scheduler/override - Clear override, return to business hours logic
        group.MapDelete("/override", async (ISchedulerControlService schedulerService) =>
        {
            await schedulerService.ClearOverrideAsync();
            var state = await schedulerService.GetStateAsync();
            
            return Results.Json(new
            {
                success = true,
                message = "Override cleared, returning to business hours logic",
                state
            });
        })
        .WithName("ClearSchedulerOverride")
        .WithSummary("Clear manual override and return to automatic business hours control")
        .Produces(200);
    }

    private static TimeSpan? ParseDuration(string duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
            return null;

        duration = duration.Trim().ToLower();
        
        // Try simple formats first: 30m, 1h, 2d
        if (duration.EndsWith("m") && int.TryParse(duration[..^1], out var minutes))
            return TimeSpan.FromMinutes(minutes);
        
        if (duration.EndsWith("h") && int.TryParse(duration[..^1], out var hours))
            return TimeSpan.FromHours(hours);
        
        if (duration.EndsWith("d") && int.TryParse(duration[..^1], out var days))
            return TimeSpan.FromDays(days);

        // Try compound format: 2h30m
        var totalMinutes = 0;
        var current = "";
        
        foreach (var c in duration)
        {
            if (char.IsDigit(c))
            {
                current += c;
            }
            else if (c == 'h' && int.TryParse(current, out var h))
            {
                totalMinutes += h * 60;
                current = "";
            }
            else if (c == 'm' && int.TryParse(current, out var m))
            {
                totalMinutes += m;
                current = "";
            }
            else if (c == 'd' && int.TryParse(current, out var d))
            {
                totalMinutes += d * 24 * 60;
                current = "";
            }
        }

        return totalMinutes > 0 ? TimeSpan.FromMinutes(totalMinutes) : null;
    }
}
