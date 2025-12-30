using System.Diagnostics;
using System.Runtime.InteropServices;
using BCHealthMonitor.Configuration;
using Microsoft.Extensions.Options;

namespace BCHealthMonitor.Services;

public interface ISystemMetricsService
{
    Task<double> GetCpuUsagePercentAsync();
    Task<double> GetMemoryUsagePercentAsync();
    Task<(long AvailableMB, long TotalMB)> GetMemoryInfoAsync();
}

/// <summary>
/// System metrics service with background CPU sampling to avoid blocking delays.
/// CPU is sampled every second in the background; GetCpuUsagePercentAsync returns instantly.
/// </summary>
public class SystemMetricsService : ISystemMetricsService, IHostedService, IDisposable
{
    private readonly ILogger<SystemMetricsService> _logger;
    private readonly PerformanceCounter? _cpuCounter;
    private readonly PerformanceCounter? _memoryCounter;
    private readonly CancellationTokenSource _cts = new();
    private Task? _backgroundTask;
    private double _currentCpuPercent;
    private readonly object _cpuLock = new();
    private bool _disposed;

    public SystemMetricsService(ILogger<SystemMetricsService> logger)
    {
        _logger = logger;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
                _memoryCounter = new PerformanceCounter("Memory", "Available MBytes", true);
                
                // Initial read to initialize counters
                _cpuCounter.NextValue();
                _memoryCounter.NextValue();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize performance counters. Falling back to alternative methods.");
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting background CPU sampling");
        _backgroundTask = Task.Run(() => SampleCpuLoop(_cts.Token), cancellationToken);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Stopping background CPU sampling");
        _cts.Cancel();
        
        if (_backgroundTask != null)
        {
            try
            {
                await _backgroundTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }
    }

    private async Task SampleCpuLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                double cpu;
                if (_cpuCounter != null)
                {
                    cpu = Math.Round(_cpuCounter.NextValue(), 1);
                }
                else
                {
                    cpu = await GetCpuUsageFallbackAsync();
                }

                lock (_cpuLock)
                {
                    _currentCpuPercent = cpu;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error sampling CPU, will retry");
            }

            try
            {
                await Task.Delay(1000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Returns the latest CPU usage percentage instantly (no blocking delay).
    /// </summary>
    public Task<double> GetCpuUsagePercentAsync()
    {
        lock (_cpuLock)
        {
            return Task.FromResult(_currentCpuPercent);
        }
    }

    private async Task<double> GetCpuUsageFallbackAsync()
    {
        var startTime = DateTime.UtcNow;
        var startCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;

        await Task.Delay(500);

        var endTime = DateTime.UtcNow;
        var endCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;

        var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
        var totalMsPassed = (endTime - startTime).TotalMilliseconds;
        var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

        return Math.Round(cpuUsageTotal * 100, 1);
    }

    public Task<double> GetMemoryUsagePercentAsync()
    {
        try
        {
            var (availableMB, totalMB) = GetMemoryInfoSync();
            var usedMB = totalMB - availableMB;
            var percentage = (double)usedMB / totalMB * 100;
            return Task.FromResult(Math.Round(percentage, 1));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate memory usage percentage");
            return Task.FromResult(0.0);
        }
    }

    public Task<(long AvailableMB, long TotalMB)> GetMemoryInfoAsync()
    {
        return Task.FromResult(GetMemoryInfoSync());
    }

    private (long AvailableMB, long TotalMB) GetMemoryInfoSync()
    {
        long availableMB = 0;
        long totalMB = 0;

        if (_memoryCounter != null)
        {
            try
            {
                availableMB = (long)_memoryCounter.NextValue();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read memory performance counter");
            }
        }

        // Get total memory using GC info or WMI fallback
        try
        {
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            totalMB = gcMemoryInfo.TotalAvailableMemoryBytes / (1024 * 1024);
            
            if (availableMB == 0)
            {
                // Estimate available from GC info
                var usedBytes = Process.GetCurrentProcess().WorkingSet64;
                availableMB = (gcMemoryInfo.TotalAvailableMemoryBytes - usedBytes) / (1024 * 1024);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get total memory info");
            // Fallback to a reasonable default
            totalMB = 16384; // Assume 16GB if we can't determine
        }

        return (availableMB, totalMB);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cpuCounter?.Dispose();
            _memoryCounter?.Dispose();
            _disposed = true;
        }
    }
}
