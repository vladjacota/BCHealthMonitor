namespace BCHealthMonitor.Configuration;

/// <summary>
/// Strategy for checking BC service availability
/// </summary>
public enum HealthCheckStrategy
{
    /// <summary>
    /// Auto-fallback chain: HTTP → TCP (if configured) → Service → PerfCounter
    /// Returns first successful check or last failure
    /// </summary>
    Auto,
    
    /// <summary>
    /// HTTP endpoint check only (OData/API metadata)
    /// </summary>
    Http,
    
    /// <summary>
    /// TCP port connectivity check only (requires TcpPort configured)
    /// </summary>
    Tcp,
    
    /// <summary>
    /// Windows Service status check only
    /// </summary>
    Service,
    
    /// <summary>
    /// BC Performance Counter check only (proves BC is processing)
    /// </summary>
    PerfCounter,
    
    /// <summary>
    /// Combined deep health: HTTP + Service + PerfCounter must ALL pass
    /// </summary>
    Combined
}

public class HealthMonitorOptions
{
    public ServerOptions Server { get; set; } = new();
    public BCInstanceOptions BCInstance { get; set; } = new();
    public LoggingOptions Logging { get; set; } = new();
}

public class ServerOptions
{
    public int Port { get; set; } = 5080;
    public int CacheDurationSeconds { get; set; } = 5;
    public int StartupDelaySeconds { get; set; } = 20;
}

public class BCInstanceOptions
{
    public string Name { get; set; } = "BC";
    public string BaseUrl { get; set; } = "http://localhost:7048/BC";
    
    /// <summary>
    /// Endpoint to check BC availability. BC on-premise has no native /health endpoint.
    /// Use OData metadata endpoint: /ODataV4/$metadata (always available, no auth needed)
    /// </summary>
    public string HealthEndpoint { get; set; } = "/ODataV4/$metadata";
    
    /// <summary>
    /// Strategy for checking BC availability. Default: Auto (fallback chain)
    /// </summary>
    public HealthCheckStrategy Strategy { get; set; } = HealthCheckStrategy.Auto;
    
    /// <summary>
    /// TCP port for connectivity check. Required for Tcp strategy, optional for Auto.
    /// Common BC ports: 7046 (Client Services), 7047 (SOAP), 7048 (OData), 7049 (Developer)
    /// </summary>
    public int? TcpPort { get; set; }
    
    /// <summary>
    /// Windows Service name. If empty, auto-discovers via Get-NAVServerInstance
    /// or falls back to pattern: MicrosoftDynamicsNavServer$[Name]
    /// </summary>
    public string ServiceName { get; set; } = "";
    
    /// <summary>
    /// Timeout in seconds for availability checks. Default: 5
    /// </summary>
    public int HealthCheckTimeoutSeconds { get; set; } = 5;
    
    public string SqlConnectionString { get; set; } = "";
    public List<string> TenantDatabases { get; set; } = new();
    public ThresholdOptions Thresholds { get; set; } = new();
    public SchedulerControlOptions SchedulerControl { get; set; } = new();
    public BCInstallationOptions Installation { get; set; } = new();
}

public class BCInstallationOptions
{
    /// <summary>
    /// Installation type: "Standard" or "LSUpdateService"
    /// </summary>
    public string Type { get; set; } = "Standard";
    
    /// <summary>
    /// For Standard installation: BC version folder name (e.g., "240", "270")
    /// If empty, will try to auto-detect
    /// </summary>
    public string Version { get; set; } = "";
    
    /// <summary>
    /// For Standard installation: Custom path to NavAdminTool.ps1
    /// If empty, uses default: C:\Program Files\Microsoft Dynamics 365 Business Central\{Version}\Service\NavAdminTool.ps1
    /// </summary>
    public string AdminToolPath { get; set; } = "";
    
    /// <summary>
    /// For LSUpdateService: Path to GoCurrentServer module (usually auto-loaded)
    /// </summary>
    public string GocModulePath { get; set; } = "";
}

public class ThresholdOptions
{
    /// <summary>
    /// CPU usage thresholds. Warning triggers Degraded, Max triggers Unhealthy.
    /// </summary>
    public ResourceThreshold Cpu { get; set; } = new() { Warning = 70, Max = 85 };
    
    /// <summary>
    /// Memory usage thresholds. Warning triggers Degraded, Max triggers Unhealthy.
    /// </summary>
    public ResourceThreshold Memory { get; set; } = new() { Warning = 75, Max = 90 };
    
    public SessionThresholdOptions ClientSessions { get; set; } = new() { Warning = 100, Max = 200 };
    public SessionThresholdOptions WebServiceSessions { get; set; } = new() { Warning = 56, Max = 80 };
    public SessionThresholdOptions TotalSessions { get; set; } = new() { Warning = 200, Max = 250 };
}

/// <summary>
/// Threshold configuration with warning (Degraded) and max (Unhealthy) levels
/// </summary>
public class ResourceThreshold
{
    /// <summary>
    /// Warning threshold - triggers Degraded status (still returns 200 OK)
    /// </summary>
    public int Warning { get; set; }
    
    /// <summary>
    /// Maximum threshold - triggers Unhealthy status (returns 503)
    /// </summary>
    public int Max { get; set; }
}

public class SessionThresholdOptions
{
    /// <summary>
    /// Warning threshold - triggers Degraded status and blocks new web service connections
    /// </summary>
    public int? Warning { get; set; }
    
    /// <summary>
    /// Maximum threshold - triggers Unhealthy status
    /// </summary>
    public int Max { get; set; }
}

public class SchedulerControlOptions
{
    public bool Enabled { get; set; } = true;
    public BusinessHoursOptions BusinessHours { get; set; } = new();
}

public class BusinessHoursOptions
{
    public string Start { get; set; } = "08:00";
    public string End { get; set; } = "20:00";
    public List<string> Days { get; set; } = new() { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
    public string Timezone { get; set; } = "Europe/Bucharest";

    public TimeOnly StartTime => TimeOnly.Parse(Start);
    public TimeOnly EndTime => TimeOnly.Parse(End);

    public bool IsBusinessHours(DateTime utcNow)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(Timezone);
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
            var currentTime = TimeOnly.FromDateTime(localTime);
            var currentDay = localTime.DayOfWeek.ToString()[..3];

            if (!Days.Contains(currentDay, StringComparer.OrdinalIgnoreCase))
                return false;

            return currentTime >= StartTime && currentTime < EndTime;
        }
        catch
        {
            // Fallback to local time if timezone lookup fails
            var localTime = utcNow.ToLocalTime();
            var currentTime = TimeOnly.FromDateTime(localTime);
            return currentTime >= StartTime && currentTime < EndTime;
        }
    }
}

public class LoggingOptions
{
    public bool EventLog { get; set; } = false;
    public string FilePath { get; set; } = @"C:\Logs\BCHealthMonitor\";
    public ApplicationInsightsOptions ApplicationInsights { get; set; } = new();
}

public class ApplicationInsightsOptions
{
    public bool Enabled { get; set; } = false;
    public string ConnectionString { get; set; } = "";
}
