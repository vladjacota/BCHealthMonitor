namespace BCHealthMonitor.Services;

public interface IStartupStateService
{
    bool IsStartupComplete { get; }
    DateTime StartTime { get; }
    void MarkStartupComplete();
}

/// <summary>
/// Singleton service to track startup state across all scoped services
/// </summary>
public class StartupStateService : IStartupStateService
{
    private volatile bool _startupComplete = false;
    private readonly DateTime _startTime = DateTime.UtcNow;
    
    public bool IsStartupComplete => _startupComplete;
    public DateTime StartTime => _startTime;
    
    public void MarkStartupComplete()
    {
        _startupComplete = true;
    }
}
