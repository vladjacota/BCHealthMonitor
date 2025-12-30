using BCHealthMonitor.Configuration;
using BCHealthMonitor.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BCHealthMonitor.Services;

public interface ICacheService
{
    T? Get<T>(string key) where T : class;
    void Set<T>(string key, T value) where T : class;
    void Set<T>(string key, T value, TimeSpan expiration) where T : class;
    void Remove(string key);
    void Clear();
}

public class CacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly HealthMonitorOptions _options;
    private readonly ILogger<CacheService> _logger;

    public CacheService(
        IMemoryCache cache,
        IOptions<HealthMonitorOptions> options,
        ILogger<CacheService> logger)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public T? Get<T>(string key) where T : class
    {
        if (_cache.TryGetValue(key, out T? value))
        {
            _logger.LogDebug("Cache hit for key: {Key}", key);
            return value;
        }

        _logger.LogDebug("Cache miss for key: {Key}", key);
        return null;
    }

    public void Set<T>(string key, T value) where T : class
    {
        var expiration = TimeSpan.FromSeconds(_options.Server.CacheDurationSeconds);
        Set(key, value, expiration);
    }

    public void Set<T>(string key, T value, TimeSpan expiration) where T : class
    {
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration
        };

        _cache.Set(key, value, cacheOptions);
        _logger.LogDebug("Cached key: {Key} for {Expiration}s", key, expiration.TotalSeconds);
    }

    public void Remove(string key)
    {
        _cache.Remove(key);
        _logger.LogDebug("Removed cache key: {Key}", key);
    }

    public void Clear()
    {
        // MemoryCache doesn't have a Clear method, so we use a workaround
        // In production, you might want to use a different approach
        if (_cache is MemoryCache memoryCache)
        {
            memoryCache.Compact(1.0);
        }
        _logger.LogDebug("Cache cleared");
    }
}

public static class CacheKeys
{
    public const string HealthClient = "health:client";
    public const string HealthWebServices = "health:webservices";
    public const string HealthScheduler = "health:scheduler";
    public const string HealthAggregate = "health:aggregate";
    public const string HealthDetails = "health:details";
    public const string SessionCounts = "sessions:counts";
    public const string SystemMetrics = "system:metrics";
}
