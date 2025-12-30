using System.Text.Json.Serialization;

namespace BCHealthMonitor.Models;

public class HealthResult
{
    [JsonPropertyName("status")]
    public HealthStatus Status { get; set; } = HealthStatus.Healthy;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }

    [JsonPropertyName("checks")]
    public Dictionary<string, CheckResult> Checks { get; set; } = new();

    [JsonPropertyName("cached")]
    public bool Cached { get; set; } = false;

    public int GetHttpStatusCode() => Status switch
    {
        HealthStatus.Healthy => 200,
        HealthStatus.Degraded => 200,
        HealthStatus.Unhealthy => 503,
        HealthStatus.Unreachable => 504,
        _ => 503
    };
}

public class CheckResult
{
    [JsonPropertyName("status")]
    public HealthStatus Status { get; set; } = HealthStatus.Healthy;

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Value { get; set; }

    [JsonPropertyName("warning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Warning { get; set; }

    [JsonPropertyName("max")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Max { get; set; }

    /// <summary>
    /// Legacy property - use Warning instead
    /// </summary>
    [JsonPropertyName("threshold")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Obsolete("Use Warning and Max instead")]
    public double? Threshold { get; set; }

    [JsonPropertyName("latency_ms")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? LatencyMs { get; set; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }
}

public class DetailedHealthResult : HealthResult
{
    [JsonPropertyName("instance_name")]
    public string InstanceName { get; set; } = "";

    [JsonPropertyName("server_name")]
    public string ServerName { get; set; } = Environment.MachineName;

    [JsonPropertyName("uptime")]
    public string Uptime { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("sessions")]
    public SessionDetails? Sessions { get; set; }

    [JsonPropertyName("scheduler")]
    public SchedulerDetails? Scheduler { get; set; }

    [JsonPropertyName("system")]
    public SystemDetails? System { get; set; }
}

public class SessionDetails
{
    [JsonPropertyName("web_client")]
    public int WebClient { get; set; }

    [JsonPropertyName("web_service")]
    public int WebService { get; set; }

    [JsonPropertyName("background")]
    public int Background { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "unknown";
}

public class SchedulerDetails
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("is_business_hours")]
    public bool IsBusinessHours { get; set; }

    [JsonPropertyName("override_active")]
    public bool OverrideActive { get; set; }

    [JsonPropertyName("override_expires")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? OverrideExpires { get; set; }

    [JsonPropertyName("next_change")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? NextChange { get; set; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
}

public class SystemDetails
{
    [JsonPropertyName("cpu_percent")]
    public double CpuPercent { get; set; }

    [JsonPropertyName("memory_percent")]
    public double MemoryPercent { get; set; }

    [JsonPropertyName("memory_available_mb")]
    public long MemoryAvailableMB { get; set; }

    [JsonPropertyName("memory_total_mb")]
    public long MemoryTotalMB { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HealthStatus
{
    Healthy,
    Degraded,
    Unhealthy,
    Unreachable
}
