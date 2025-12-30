namespace BCHealthMonitor.Models;

public class SchedulerState
{
    public bool Enabled { get; set; }
    public bool IsBusinessHours { get; set; }
    public bool OverrideActive { get; set; }
    public DateTime? OverrideExpires { get; set; }
    public SchedulerOverrideType? OverrideType { get; set; }
    public DateTime? NextScheduledChange { get; set; }
    public string Reason { get; set; } = "";
    public DateTime LastChecked { get; set; } = DateTime.UtcNow;
    public bool LastOperationSuccess { get; set; } = true;
    public string? LastError { get; set; }
}

public enum SchedulerOverrideType
{
    ManualEnable,
    ManualDisable,
    TemporaryEnable,
    TemporaryDisable
}

public class SchedulerOverride
{
    public bool Enable { get; set; }
    public TimeSpan? Duration { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt => Duration.HasValue ? CreatedAt + Duration.Value : null;
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;
}
