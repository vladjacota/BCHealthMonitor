namespace BCHealthMonitor.Models;

public class SessionCounts
{
    public int WebClient { get; set; }
    public int WebService { get; set; }
    public int Background { get; set; }
    public int Total => WebClient + WebService + Background;
    public string Source { get; set; } = "unknown";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsValid { get; set; } = true;
    public string? ErrorMessage { get; set; }

    public static SessionCounts Empty(string source = "none", string? error = null) => new()
    {
        Source = source,
        IsValid = error == null,
        ErrorMessage = error
    };

    public static SessionCounts FromSql(int webClient, int webService, int background) => new()
    {
        WebClient = webClient,
        WebService = webService,
        Background = background,
        Source = "sql"
    };

    public static SessionCounts FromApi(int webClient, int webService, int background) => new()
    {
        WebClient = webClient,
        WebService = webService,
        Background = background,
        Source = "api"
    };

    public static SessionCounts FromPerfCounter(int total) => new()
    {
        // When using perf counters, we can't distinguish session types
        // Assume worst case: all sessions are client sessions for safety
        WebClient = total,
        WebService = 0,
        Background = 0,
        Source = "perfcounter"
    };
}
