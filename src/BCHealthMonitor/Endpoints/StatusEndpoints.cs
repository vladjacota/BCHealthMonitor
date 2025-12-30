using System.Text;
using BCHealthMonitor.Models;
using BCHealthMonitor.Services;

namespace BCHealthMonitor.Endpoints;

public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/status", async (IHealthCheckService healthService) =>
        {
            var details = await healthService.GetDetailedHealthAsync();
            var html = GenerateStatusPage(details);
            return Results.Content(html, "text/html");
        })
        .WithTags("Status")
        .WithName("GetStatusPage")
        .WithSummary("Get human-readable status dashboard")
        .ExcludeFromDescription();
    }

    private static string GenerateStatusPage(DetailedHealthResult health)
    {
        var statusColor = health.Status switch
        {
            HealthStatus.Healthy => "#22c55e",
            HealthStatus.Degraded => "#eab308",
            HealthStatus.Unhealthy => "#ef4444",
            HealthStatus.Unreachable => "#6b7280",
            _ => "#6b7280"
        };

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine("  <meta http-equiv=\"refresh\" content=\"10\">");
        sb.AppendLine("  <title>BC Health Monitor - Status</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    * { margin: 0; padding: 0; box-sizing: border-box; }");
        sb.AppendLine("    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #0f172a; color: #e2e8f0; padding: 2rem; }");
        sb.AppendLine("    .container { max-width: 1200px; margin: 0 auto; }");
        sb.AppendLine("    h1 { font-size: 1.5rem; margin-bottom: 1rem; }");
        sb.AppendLine("    .status-banner { padding: 1rem; border-radius: 0.5rem; margin-bottom: 2rem; display: flex; align-items: center; gap: 1rem; }");
        sb.AppendLine("    .status-dot { width: 12px; height: 12px; border-radius: 50%; }");
        sb.AppendLine("    .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 1rem; }");
        sb.AppendLine("    .card { background: #1e293b; border-radius: 0.5rem; padding: 1rem; }");
        sb.AppendLine("    .card h2 { font-size: 0.875rem; color: #94a3b8; margin-bottom: 0.75rem; text-transform: uppercase; letter-spacing: 0.05em; }");
        sb.AppendLine("    .metric { display: flex; justify-content: space-between; padding: 0.5rem 0; border-bottom: 1px solid #334155; }");
        sb.AppendLine("    .metric:last-child { border-bottom: none; }");
        sb.AppendLine("    .metric-label { color: #94a3b8; }");
        sb.AppendLine("    .metric-value { font-weight: 600; }");
        sb.AppendLine("    .healthy { color: #22c55e; }");
        sb.AppendLine("    .degraded { color: #eab308; }");
        sb.AppendLine("    .unhealthy { color: #ef4444; }");
        sb.AppendLine("    .unreachable { color: #6b7280; }");
        sb.AppendLine("    .check-item { display: flex; align-items: center; gap: 0.5rem; padding: 0.5rem 0; }");
        sb.AppendLine("    .check-icon { width: 8px; height: 8px; border-radius: 50%; }");
        sb.AppendLine("    .meta { color: #64748b; font-size: 0.75rem; margin-top: 2rem; text-align: center; }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <div class=\"container\">");
        
        // Header
        sb.AppendLine($"    <h1>BC Health Monitor - {health.InstanceName}</h1>");
        
        // Status banner
        sb.AppendLine($"    <div class=\"status-banner\" style=\"background: {statusColor}20; border: 1px solid {statusColor};\">");
        sb.AppendLine($"      <div class=\"status-dot\" style=\"background: {statusColor};\"></div>");
        sb.AppendLine($"      <span style=\"font-weight: 600; color: {statusColor};\">{health.Status}</span>");
        sb.AppendLine($"      <span style=\"color: #94a3b8; margin-left: auto;\">Last check: {health.Timestamp:HH:mm:ss} ({health.DurationMs}ms)</span>");
        sb.AppendLine("    </div>");

        sb.AppendLine("    <div class=\"grid\">");
        
        // System metrics
        sb.AppendLine("      <div class=\"card\">");
        sb.AppendLine("        <h2>System Resources</h2>");
        if (health.System != null)
        {
            var cpuClass = health.System.CpuPercent < 70 ? "healthy" : health.System.CpuPercent < 85 ? "degraded" : "unhealthy";
            var memClass = health.System.MemoryPercent < 70 ? "healthy" : health.System.MemoryPercent < 90 ? "degraded" : "unhealthy";
            
            sb.AppendLine($"        <div class=\"metric\"><span class=\"metric-label\">CPU Usage</span><span class=\"metric-value {cpuClass}\">{health.System.CpuPercent:F1}%</span></div>");
            sb.AppendLine($"        <div class=\"metric\"><span class=\"metric-label\">Memory Usage</span><span class=\"metric-value {memClass}\">{health.System.MemoryPercent:F1}%</span></div>");
            sb.AppendLine($"        <div class=\"metric\"><span class=\"metric-label\">Memory Available</span><span class=\"metric-value\">{health.System.MemoryAvailableMB:N0} MB</span></div>");
            sb.AppendLine($"        <div class=\"metric\"><span class=\"metric-label\">Memory Total</span><span class=\"metric-value\">{health.System.MemoryTotalMB:N0} MB</span></div>");
        }
        sb.AppendLine("      </div>");

        // Sessions
        sb.AppendLine("      <div class=\"card\">");
        sb.AppendLine("        <h2>Sessions</h2>");
        if (health.Sessions != null)
        {
            sb.AppendLine($"        <div class=\"metric\"><span class=\"metric-label\">Web Client</span><span class=\"metric-value\">{health.Sessions.WebClient}</span></div>");
            sb.AppendLine($"        <div class=\"metric\"><span class=\"metric-label\">Web Service</span><span class=\"metric-value\">{health.Sessions.WebService}</span></div>");
            sb.AppendLine($"        <div class=\"metric\"><span class=\"metric-label\">Background</span><span class=\"metric-value\">{health.Sessions.Background}</span></div>");
            sb.AppendLine($"        <div class=\"metric\"><span class=\"metric-label\">Total</span><span class=\"metric-value\" style=\"font-size: 1.25rem;\">{health.Sessions.Total}</span></div>");
            sb.AppendLine($"        <div class=\"metric\"><span class=\"metric-label\">Source</span><span class=\"metric-value\" style=\"color: #64748b;\">{health.Sessions.Source}</span></div>");
        }
        sb.AppendLine("      </div>");

        // Scheduler
        sb.AppendLine("      <div class=\"card\">");
        sb.AppendLine("        <h2>Task Scheduler</h2>");
        if (health.Scheduler != null)
        {
            var enabledClass = health.Scheduler.Enabled ? "healthy" : "unhealthy";
            sb.AppendLine($"        <div class=\"metric\"><span class=\"metric-label\">Status</span><span class=\"metric-value {enabledClass}\">{(health.Scheduler.Enabled ? "Enabled" : "Disabled")}</span></div>");
            sb.AppendLine($"        <div class=\"metric\"><span class=\"metric-label\">Business Hours</span><span class=\"metric-value\">{(health.Scheduler.IsBusinessHours ? "Yes" : "No")}</span></div>");
            sb.AppendLine($"        <div class=\"metric\"><span class=\"metric-label\">Override Active</span><span class=\"metric-value\">{(health.Scheduler.OverrideActive ? "Yes" : "No")}</span></div>");
            if (!string.IsNullOrEmpty(health.Scheduler.Reason))
            {
                sb.AppendLine($"        <div class=\"metric\"><span class=\"metric-label\">Reason</span><span class=\"metric-value\" style=\"color: #94a3b8;\">{health.Scheduler.Reason}</span></div>");
            }
        }
        sb.AppendLine("      </div>");

        // Health checks
        sb.AppendLine("      <div class=\"card\">");
        sb.AppendLine("        <h2>Health Checks</h2>");
        foreach (var check in health.Checks)
        {
            var checkColor = check.Value.Status switch
            {
                HealthStatus.Healthy => "#22c55e",
                HealthStatus.Degraded => "#eab308",
                HealthStatus.Unhealthy => "#ef4444",
                _ => "#6b7280"
            };
            
            var valueDisplay = "";
            if (check.Value.Value.HasValue)
            {
                valueDisplay = $" ({check.Value.Value:F1}";
                if (check.Value.Warning.HasValue && check.Value.Max.HasValue)
                    valueDisplay += $" / {check.Value.Warning}-{check.Value.Max}";
                else if (check.Value.Max.HasValue)
                    valueDisplay += $" / {check.Value.Max}";
                valueDisplay += ")";
            }
            else if (check.Value.LatencyMs.HasValue)
            {
                valueDisplay = $" ({check.Value.LatencyMs}ms)";
            }

            sb.AppendLine($"        <div class=\"check-item\">");
            sb.AppendLine($"          <div class=\"check-icon\" style=\"background: {checkColor};\"></div>");
            sb.AppendLine($"          <span>{check.Key}{valueDisplay}</span>");
            sb.AppendLine($"        </div>");
        }
        sb.AppendLine("      </div>");

        sb.AppendLine("    </div>");

        // Meta info
        sb.AppendLine($"    <div class=\"meta\">");
        sb.AppendLine($"      Server: {health.ServerName} | Instance: {health.InstanceName} | Uptime: {health.Uptime} | Version: {health.Version}");
        sb.AppendLine($"      <br>Page auto-refreshes every 10 seconds");
        sb.AppendLine($"    </div>");

        sb.AppendLine("  </div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }
}
