using System.Diagnostics;
using BCHealthMonitor.Configuration;
using BCHealthMonitor.Models;
using Microsoft.Extensions.Options;
using Prometheus;

namespace BCHealthMonitor.Services;

public interface IHealthCheckService
{
    Task<HealthResult> CheckClientHealthAsync();
    Task<HealthResult> CheckWebServicesHealthAsync();
    Task<HealthResult> CheckSchedulerHealthAsync();
    Task<HealthResult> CheckAggregateHealthAsync();
    Task<DetailedHealthResult> GetDetailedHealthAsync();
    bool IsStartupComplete { get; }
}

public class HealthCheckService : IHealthCheckService
{
    private readonly ILogger<HealthCheckService> _logger;
    private readonly HealthMonitorOptions _options;
    private readonly ISessionDataService _sessionService;
    private readonly ISystemMetricsService _systemMetrics;
    private readonly ISchedulerControlService _schedulerService;
    private readonly IBCAvailabilityService _availabilityService;
    private readonly IStartupStateService _startupState;
    private readonly ICacheService _cache;
    
    public bool IsStartupComplete => _startupState.IsStartupComplete;

    // Prometheus metrics
    private static readonly Gauge CpuUsageGauge = Metrics.CreateGauge("bc_health_cpu_percent", "CPU usage percentage");
    private static readonly Gauge MemoryUsageGauge = Metrics.CreateGauge("bc_health_memory_percent", "Memory usage percentage");
    private static readonly Gauge SessionsTotalGauge = Metrics.CreateGauge("bc_health_sessions_total", "Total active sessions");
    private static readonly Gauge SessionsWebClientGauge = Metrics.CreateGauge("bc_health_sessions_webclient", "Web client sessions");
    private static readonly Gauge SessionsWebServiceGauge = Metrics.CreateGauge("bc_health_sessions_webservice", "Web service sessions");
    private static readonly Gauge SessionsBackgroundGauge = Metrics.CreateGauge("bc_health_sessions_background", "Background sessions");
    private static readonly Gauge SchedulerEnabledGauge = Metrics.CreateGauge("bc_health_scheduler_enabled", "Task scheduler enabled (1=yes, 0=no)");
    private static readonly Gauge HealthStatusGauge = Metrics.CreateGauge("bc_health_status", "Overall health status (1=healthy, 0=unhealthy)", "endpoint");

    public HealthCheckService(
        ILogger<HealthCheckService> logger,
        IOptions<HealthMonitorOptions> options,
        ISessionDataService sessionService,
        ISystemMetricsService systemMetrics,
        ISchedulerControlService schedulerService,
        IBCAvailabilityService availabilityService,
        IStartupStateService startupState,
        ICacheService cache)
    {
        _logger = logger;
        _options = options.Value;
        _sessionService = sessionService;
        _systemMetrics = systemMetrics;
        _schedulerService = schedulerService;
        _availabilityService = availabilityService;
        _startupState = startupState;
        _cache = cache;
    }

    public async Task<HealthResult> CheckClientHealthAsync()
    {
        var cached = _cache.Get<HealthResult>(CacheKeys.HealthClient);
        if (cached != null)
        {
            cached.Cached = true;
            return cached;
        }

        var sw = Stopwatch.StartNew();
        var result = new HealthResult();

        try
        {
            // Check BC health first - if unreachable, skip other checks
            var bcCheck = await CheckBcHealthAsync();
            result.Checks["bc_health"] = bcCheck;

            if (bcCheck.Status == Models.HealthStatus.Unreachable)
            {
                result.Status = Models.HealthStatus.Unreachable;
                return FinalizeResult(result, sw, CacheKeys.HealthClient, "client");
            }

            // Run remaining checks in parallel
            var cpuTask = CheckCpuAsync();
            var memoryTask = CheckMemoryAsync();
            var sessionsTask = _sessionService.GetSessionCountsAsync();

            await Task.WhenAll(cpuTask, memoryTask, sessionsTask);

            var cpuCheck = await cpuTask;
            var memoryCheck = await memoryTask;
            var sessions = await sessionsTask;

            result.Checks["cpu"] = cpuCheck;
            result.Checks["memory"] = memoryCheck;

            var clientSessionCheck = CheckClientSessions(sessions);
            result.Checks["client_sessions"] = clientSessionCheck;

            // Determine overall status
            result.Status = DetermineOverallStatus(bcCheck, cpuCheck, memoryCheck, clientSessionCheck);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during client health check");
            result.Status = Models.HealthStatus.Unhealthy;
            result.Checks["error"] = new CheckResult
            {
                Status = Models.HealthStatus.Unhealthy,
                Message = ex.Message
            };
        }

        return FinalizeResult(result, sw, CacheKeys.HealthClient, "client");
    }

    public async Task<HealthResult> CheckWebServicesHealthAsync()
    {
        var cached = _cache.Get<HealthResult>(CacheKeys.HealthWebServices);
        if (cached != null)
        {
            cached.Cached = true;
            return cached;
        }

        var sw = Stopwatch.StartNew();
        var result = new HealthResult();

        try
        {
            // Check BC health first - if unreachable, skip other checks
            var bcCheck = await CheckBcHealthAsync();
            result.Checks["bc_health"] = bcCheck;

            if (bcCheck.Status == Models.HealthStatus.Unreachable)
            {
                result.Status = Models.HealthStatus.Unreachable;
                return FinalizeResult(result, sw, CacheKeys.HealthWebServices, "webservices");
            }

            // Run remaining checks in parallel
            var cpuTask = CheckCpuAsync();
            var memoryTask = CheckMemoryAsync();
            var sessionsTask = _sessionService.GetSessionCountsAsync();

            await Task.WhenAll(cpuTask, memoryTask, sessionsTask);

            var cpuCheck = await cpuTask;
            var memoryCheck = await memoryTask;
            var sessions = await sessionsTask;

            result.Checks["cpu"] = cpuCheck;
            result.Checks["memory"] = memoryCheck;

            // Check sessions - web services are blocked earlier to protect client capacity
            var clientProtectionCheck = CheckClientProtection(sessions);
            var wsSessionCheck = CheckWebServiceSessions(sessions);
            result.Checks["client_protection"] = clientProtectionCheck;
            result.Checks["webservice_sessions"] = wsSessionCheck;

            // Determine overall status
            result.Status = DetermineOverallStatus(bcCheck, cpuCheck, memoryCheck, clientProtectionCheck, wsSessionCheck);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during web services health check");
            result.Status = Models.HealthStatus.Unhealthy;
            result.Checks["error"] = new CheckResult
            {
                Status = Models.HealthStatus.Unhealthy,
                Message = ex.Message
            };
        }

        return FinalizeResult(result, sw, CacheKeys.HealthWebServices, "webservices");
    }

    public async Task<HealthResult> CheckSchedulerHealthAsync()
    {
        var cached = _cache.Get<HealthResult>(CacheKeys.HealthScheduler);
        if (cached != null)
        {
            cached.Cached = true;
            return cached;
        }

        var sw = Stopwatch.StartNew();
        var result = new HealthResult();

        try
        {
            // Check BC health first - if unreachable, skip other checks
            var bcCheck = await CheckBcHealthAsync();
            result.Checks["bc_health"] = bcCheck;

            if (bcCheck.Status == Models.HealthStatus.Unreachable)
            {
                result.Status = Models.HealthStatus.Unreachable;
                return FinalizeResult(result, sw, CacheKeys.HealthScheduler, "scheduler");
            }

            // Run remaining checks in parallel
            var cpuTask = CheckCpuAsync();
            var memoryTask = CheckMemoryAsync();
            var schedulerTask = _schedulerService.GetStateAsync();
            var sessionsTask = _sessionService.GetSessionCountsAsync();

            await Task.WhenAll(cpuTask, memoryTask, schedulerTask, sessionsTask);

            var cpuCheck = await cpuTask;
            var memoryCheck = await memoryTask;
            var schedulerState = await schedulerTask;
            var sessions = await sessionsTask;

            result.Checks["cpu"] = cpuCheck;
            result.Checks["memory"] = memoryCheck;

            var schedulerCheck = new CheckResult
            {
                Status = schedulerState.Enabled && !schedulerState.IsBusinessHours 
                    ? Models.HealthStatus.Healthy 
                    : Models.HealthStatus.Unhealthy,
                Message = schedulerState.Reason
            };
            result.Checks["scheduler"] = schedulerCheck;

            var totalSessionCheck = CheckTotalSessions(sessions);
            result.Checks["total_sessions"] = totalSessionCheck;

            // Determine overall status
            result.Status = DetermineOverallStatus(bcCheck, cpuCheck, memoryCheck, schedulerCheck, totalSessionCheck);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during scheduler health check");
            result.Status = Models.HealthStatus.Unhealthy;
            result.Checks["error"] = new CheckResult
            {
                Status = Models.HealthStatus.Unhealthy,
                Message = ex.Message
            };
        }

        return FinalizeResult(result, sw, CacheKeys.HealthScheduler, "scheduler");
    }

    public async Task<HealthResult> CheckAggregateHealthAsync()
    {
        var cached = _cache.Get<HealthResult>(CacheKeys.HealthAggregate);
        if (cached != null)
        {
            cached.Cached = true;
            return cached;
        }

        var sw = Stopwatch.StartNew();
        var result = new HealthResult();

        try
        {
            // Check BC health first - if unreachable, skip other checks
            var bcCheck = await CheckBcHealthAsync();
            result.Checks["bc_health"] = bcCheck;

            if (bcCheck.Status == Models.HealthStatus.Unreachable)
            {
                result.Status = Models.HealthStatus.Unreachable;
                return FinalizeResult(result, sw, CacheKeys.HealthAggregate, "aggregate");
            }

            // Run remaining checks in parallel
            var cpuTask = CheckCpuAsync();
            var memoryTask = CheckMemoryAsync();

            await Task.WhenAll(cpuTask, memoryTask);

            var cpuCheck = await cpuTask;
            var memoryCheck = await memoryTask;

            result.Checks["cpu"] = cpuCheck;
            result.Checks["memory"] = memoryCheck;

            // Determine overall status (aggregate only cares about BC and system resources)
            result.Status = DetermineOverallStatus(bcCheck, cpuCheck, memoryCheck);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during aggregate health check");
            result.Status = Models.HealthStatus.Unhealthy;
            result.Checks["error"] = new CheckResult
            {
                Status = Models.HealthStatus.Unhealthy,
                Message = ex.Message
            };
        }

        return FinalizeResult(result, sw, CacheKeys.HealthAggregate, "aggregate");
    }

    public async Task<DetailedHealthResult> GetDetailedHealthAsync()
    {
        var cached = _cache.Get<DetailedHealthResult>(CacheKeys.HealthDetails);
        if (cached != null)
        {
            cached.Cached = true;
            return cached;
        }

        var sw = Stopwatch.StartNew();
        var result = new DetailedHealthResult
        {
            InstanceName = _options.BCInstance.Name,
            ServerName = Environment.MachineName,
            Uptime = (DateTime.UtcNow - _startupState.StartTime).ToString(@"d\.hh\:mm\:ss"),
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0"
        };

        try
        {
            // BC health - this is the primary check
            var bcCheck = await CheckBcHealthAsync();
            result.Checks["bc_health"] = bcCheck;
            
            _logger.LogDebug("BC health check result: Status={Status}, Message={Message}, Source={Source}", 
                bcCheck.Status, bcCheck.Message, bcCheck.Source);

            // If BC is not healthy, that determines the overall status
            if (bcCheck.Status != Models.HealthStatus.Healthy)
            {
                result.Status = bcCheck.Status;
                _logger.LogDebug("BC not healthy, setting overall status to {Status}", bcCheck.Status);
            }

            // System metrics
            var cpuCheck = await CheckCpuAsync();
            var memoryCheck = await CheckMemoryAsync();
            var (availMem, totalMem) = await _systemMetrics.GetMemoryInfoAsync();
            
            result.Checks["cpu"] = cpuCheck;
            result.Checks["memory"] = memoryCheck;
            result.System = new SystemDetails
            {
                CpuPercent = cpuCheck.Value ?? 0,
                MemoryPercent = memoryCheck.Value ?? 0,
                MemoryAvailableMB = availMem,
                MemoryTotalMB = totalMem
            };

            // Sessions
            var sessions = await _sessionService.GetSessionCountsAsync();
            result.Sessions = new SessionDetails
            {
                WebClient = sessions.WebClient,
                WebService = sessions.WebService,
                Background = sessions.Background,
                Total = sessions.Total,
                Source = sessions.Source
            };

            result.Checks["client_sessions"] = CheckClientSessions(sessions);
            result.Checks["webservice_sessions"] = CheckWebServiceSessions(sessions);
            result.Checks["total_sessions"] = CheckTotalSessions(sessions);

            // Scheduler
            var schedulerState = await _schedulerService.GetStateAsync();
            result.Scheduler = new SchedulerDetails
            {
                Enabled = schedulerState.Enabled,
                IsBusinessHours = schedulerState.IsBusinessHours,
                OverrideActive = schedulerState.OverrideActive,
                OverrideExpires = schedulerState.OverrideExpires,
                Reason = schedulerState.Reason
            };

            result.Checks["scheduler"] = new CheckResult
            {
                // Scheduler state is informational, not a health indicator
                // Being disabled during business hours is expected behavior
                Status = Models.HealthStatus.Healthy,
                Message = schedulerState.Reason
            };

            // Determine overall status - BC health takes precedence
            if (result.Status == Models.HealthStatus.Healthy)
            {
                // Only use DetermineOverallStatus if BC is healthy
                result.Status = DetermineOverallStatus(result.Checks.Values.ToArray());
            }
            // If BC status is Unreachable/Unhealthy/Degraded, keep that status
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during detailed health check");
            result.Status = Models.HealthStatus.Unhealthy;
            result.Checks["error"] = new CheckResult
            {
                Status = Models.HealthStatus.Unhealthy,
                Message = ex.Message
            };
        }

        result.DurationMs = sw.ElapsedMilliseconds;
        _cache.Set(CacheKeys.HealthDetails, result);
        return result;
    }

    private async Task<CheckResult> CheckBcHealthAsync()
    {
        // Delegate to BCAvailabilityService which handles multiple strategies
        return await _availabilityService.CheckAvailabilityAsync();
    }

    private async Task<CheckResult> CheckCpuAsync()
    {
        var cpu = await _systemMetrics.GetCpuUsagePercentAsync();
        var warning = _options.BCInstance.Thresholds.Cpu.Warning;
        var max = _options.BCInstance.Thresholds.Cpu.Max;
        
        CpuUsageGauge.Set(cpu);

        var status = cpu < warning ? Models.HealthStatus.Healthy
                   : cpu < max ? Models.HealthStatus.Degraded
                   : Models.HealthStatus.Unhealthy;

        return new CheckResult
        {
            Status = status,
            Value = cpu,
            Warning = warning,
            Max = max
        };
    }

    private async Task<CheckResult> CheckMemoryAsync()
    {
        var memory = await _systemMetrics.GetMemoryUsagePercentAsync();
        var warning = _options.BCInstance.Thresholds.Memory.Warning;
        var max = _options.BCInstance.Thresholds.Memory.Max;
        
        MemoryUsageGauge.Set(memory);

        var status = memory < warning ? Models.HealthStatus.Healthy
                   : memory < max ? Models.HealthStatus.Degraded
                   : Models.HealthStatus.Unhealthy;

        return new CheckResult
        {
            Status = status,
            Value = memory,
            Warning = warning,
            Max = max
        };
    }

    private CheckResult CheckClientSessions(SessionCounts sessions)
    {
        var warning = _options.BCInstance.Thresholds.ClientSessions.Warning;
        var max = _options.BCInstance.Thresholds.ClientSessions.Max;
        
        SessionsWebClientGauge.Set(sessions.WebClient);
        SessionsTotalGauge.Set(sessions.Total);
        SessionsWebServiceGauge.Set(sessions.WebService);
        SessionsBackgroundGauge.Set(sessions.Background);

        var status = !warning.HasValue || sessions.WebClient < warning.Value ? Models.HealthStatus.Healthy
                   : sessions.WebClient < max ? Models.HealthStatus.Degraded
                   : Models.HealthStatus.Unhealthy;

        return new CheckResult
        {
            Status = status,
            Value = sessions.WebClient,
            Warning = warning,
            Max = max,
            Source = sessions.Source
        };
    }

    private CheckResult CheckClientProtection(SessionCounts sessions)
    {
        // Warning threshold used for WS blocking to protect client capacity
        var warning = _options.BCInstance.Thresholds.ClientSessions.Warning ?? _options.BCInstance.Thresholds.ClientSessions.Max;

        return new CheckResult
        {
            Status = sessions.WebClient < warning ? Models.HealthStatus.Healthy : Models.HealthStatus.Unhealthy,
            Value = sessions.WebClient,
            Warning = warning,
            Message = sessions.WebClient >= warning ? "Protecting client session capacity" : null,
            Source = sessions.Source
        };
    }

    private CheckResult CheckWebServiceSessions(SessionCounts sessions)
    {
        var warning = _options.BCInstance.Thresholds.WebServiceSessions.Warning;
        var max = _options.BCInstance.Thresholds.WebServiceSessions.Max;

        var status = !warning.HasValue || sessions.WebService < warning.Value ? Models.HealthStatus.Healthy
                   : sessions.WebService < max ? Models.HealthStatus.Degraded
                   : Models.HealthStatus.Unhealthy;

        return new CheckResult
        {
            Status = status,
            Value = sessions.WebService,
            Warning = warning,
            Max = max,
            Source = sessions.Source
        };
    }

    private CheckResult CheckTotalSessions(SessionCounts sessions)
    {
        var warning = _options.BCInstance.Thresholds.TotalSessions.Warning;
        var max = _options.BCInstance.Thresholds.TotalSessions.Max;

        var status = !warning.HasValue || sessions.Total < warning.Value ? Models.HealthStatus.Healthy
                   : sessions.Total < max ? Models.HealthStatus.Degraded
                   : Models.HealthStatus.Unhealthy;

        return new CheckResult
        {
            Status = status,
            Value = sessions.Total,
            Warning = warning,
            Max = max,
            Source = sessions.Source
        };
    }

    private static Models.HealthStatus DetermineOverallStatus(params CheckResult[] checks)
    {
        if (checks.Any(c => c.Status == Models.HealthStatus.Unreachable))
            return Models.HealthStatus.Unreachable;
        
        if (checks.Any(c => c.Status == Models.HealthStatus.Unhealthy))
            return Models.HealthStatus.Unhealthy;
        
        if (checks.Any(c => c.Status == Models.HealthStatus.Degraded))
            return Models.HealthStatus.Degraded;
        
        return Models.HealthStatus.Healthy;
    }

    private HealthResult FinalizeResult(HealthResult result, Stopwatch sw, string cacheKey, string metricLabel)
    {
        result.DurationMs = sw.ElapsedMilliseconds;
        result.Timestamp = DateTime.UtcNow;
        
        _cache.Set(cacheKey, result);
        
        var isHealthy = result.Status == Models.HealthStatus.Healthy || result.Status == Models.HealthStatus.Degraded;
        HealthStatusGauge.WithLabels(metricLabel).Set(isHealthy ? 1 : 0);
        SchedulerEnabledGauge.Set(_schedulerService.IsSchedulerEnabled ? 1 : 0);

        return result;
    }
}
