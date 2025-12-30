using System.Diagnostics;
using System.Net.Sockets;
using System.ServiceProcess;
using BCHealthMonitor.Configuration;
using BCHealthMonitor.Models;
using Microsoft.Extensions.Options;

namespace BCHealthMonitor.Services;

public interface IBCAvailabilityService
{
    /// <summary>
    /// Check BC availability using the configured strategy
    /// </summary>
    Task<CheckResult> CheckAvailabilityAsync();
    
    /// <summary>
    /// Discover the Windows Service name for the BC instance
    /// </summary>
    Task<string> GetServiceNameAsync();
}

public class BCAvailabilityService : IBCAvailabilityService
{
    private readonly ILogger<BCAvailabilityService> _logger;
    private readonly HealthMonitorOptions _options;
    private readonly HttpClient _httpClient;
    private readonly string _instanceName;
    private readonly string _installationType;
    
    private string? _cachedServiceName;
    private readonly SemaphoreSlim _serviceNameLock = new(1, 1);
    
    // BC Performance Counter category name
    private const string BCPerfCounterCategory = "Microsoft Dynamics 365 Business Central";
    
    public BCAvailabilityService(
        ILogger<BCAvailabilityService> logger,
        IOptions<HealthMonitorOptions> options,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _options = options.Value;
        _httpClient = httpClientFactory.CreateClient("BCApi");
        _instanceName = _options.BCInstance.Name;
        _installationType = _options.BCInstance.Installation.Type;
        
        _logger.LogInformation(
            "BC Availability Service initialized. Strategy: {Strategy}, Instance: {Instance}, InstallationType: {InstallationType}",
            _options.BCInstance.Strategy, _instanceName, _installationType);
    }

    public async Task<CheckResult> CheckAvailabilityAsync()
    {
        var strategy = _options.BCInstance.Strategy;
        
        return strategy switch
        {
            HealthCheckStrategy.Http => await CheckHttpAsync(),
            HealthCheckStrategy.Tcp => await CheckTcpAsync(),
            HealthCheckStrategy.Service => await CheckServiceAsync(),
            HealthCheckStrategy.PerfCounter => await CheckPerfCounterAsync(),
            HealthCheckStrategy.Combined => await CheckCombinedAsync(),
            HealthCheckStrategy.Auto => await CheckAutoAsync(),
            _ => await CheckAutoAsync()
        };
    }

    /// <summary>
    /// Auto strategy: fallback chain HTTP → TCP → Service → PerfCounter
    /// </summary>
    private async Task<CheckResult> CheckAutoAsync()
    {
        // Try HTTP first (most comprehensive - tests full web stack)
        var result = await CheckHttpAsync();
        if (result.Status == HealthStatus.Healthy)
        {
            result.Source = "http";
            return result;
        }
        _logger.LogDebug("HTTP check failed: {Message}, trying next strategy", result.Message);

        // Try TCP if configured
        if (_options.BCInstance.TcpPort.HasValue)
        {
            result = await CheckTcpAsync();
            if (result.Status == HealthStatus.Healthy)
            {
                result.Source = "tcp";
                return result;
            }
            _logger.LogDebug("TCP check failed: {Message}, trying next strategy", result.Message);
        }

        // Try Windows Service check
        result = await CheckServiceAsync();
        if (result.Status == HealthStatus.Healthy)
        {
            result.Source = "service";
            return result;
        }
        _logger.LogDebug("Service check failed: {Message}, trying next strategy", result.Message);

        // Last resort: Performance Counter
        result = await CheckPerfCounterAsync();
        if (result.Status == HealthStatus.Healthy)
        {
            result.Source = "perfcounter";
            return result;
        }

        // All strategies failed
        _logger.LogWarning("All availability check strategies failed for instance {Instance}", _instanceName);
        result.Source = "auto-failed";
        return result;
    }

    /// <summary>
    /// Combined strategy: HTTP + Service + PerfCounter must ALL pass
    /// </summary>
    private async Task<CheckResult> CheckCombinedAsync()
    {
        var sw = Stopwatch.StartNew();
        var failures = new List<string>();

        // Run all checks in parallel
        var httpTask = CheckHttpAsync();
        var serviceTask = CheckServiceAsync();
        var perfTask = CheckPerfCounterAsync();

        await Task.WhenAll(httpTask, serviceTask, perfTask);

        var httpResult = await httpTask;
        var serviceResult = await serviceTask;
        var perfResult = await perfTask;

        if (httpResult.Status != HealthStatus.Healthy)
            failures.Add($"HTTP: {httpResult.Message}");

        if (serviceResult.Status != HealthStatus.Healthy)
            failures.Add($"Service: {serviceResult.Message}");

        if (perfResult.Status != HealthStatus.Healthy)
            failures.Add($"PerfCounter: {perfResult.Message}");

        sw.Stop();

        if (failures.Count == 0)
        {
            return new CheckResult
            {
                Status = HealthStatus.Healthy,
                LatencyMs = sw.ElapsedMilliseconds,
                Source = "combined"
            };
        }

        return new CheckResult
        {
            Status = failures.Count == 3 ? HealthStatus.Unreachable : HealthStatus.Unhealthy,
            LatencyMs = sw.ElapsedMilliseconds,
            Message = string.Join("; ", failures),
            Source = "combined"
        };
    }

    /// <summary>
    /// HTTP endpoint check (OData/API metadata)
    /// </summary>
    private async Task<CheckResult> CheckHttpAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var url = $"{_options.BCInstance.BaseUrl}{_options.BCInstance.HealthEndpoint}";
            var timeout = TimeSpan.FromSeconds(_options.BCInstance.HealthCheckTimeoutSeconds);
            
            using var cts = new CancellationTokenSource(timeout);
            using var response = await _httpClient.GetAsync(url, cts.Token);
            
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new CheckResult
                {
                    Status = HealthStatus.Healthy,
                    LatencyMs = sw.ElapsedMilliseconds,
                    Source = "http"
                };
            }

            return new CheckResult
            {
                Status = HealthStatus.Unhealthy,
                LatencyMs = sw.ElapsedMilliseconds,
                Message = $"BC returned {response.StatusCode}",
                Source = "http"
            };
        }
        catch (TaskCanceledException)
        {
            return new CheckResult
            {
                Status = HealthStatus.Unreachable,
                LatencyMs = sw.ElapsedMilliseconds,
                Message = "HTTP health check timed out",
                Source = "http"
            };
        }
        catch (HttpRequestException ex)
        {
            return new CheckResult
            {
                Status = HealthStatus.Unreachable,
                LatencyMs = sw.ElapsedMilliseconds,
                Message = $"HTTP unreachable: {ex.Message}",
                Source = "http"
            };
        }
    }

    /// <summary>
    /// TCP port connectivity check
    /// </summary>
    private async Task<CheckResult> CheckTcpAsync()
    {
        var port = _options.BCInstance.TcpPort;
        if (!port.HasValue)
        {
            return new CheckResult
            {
                Status = HealthStatus.Unhealthy,
                Message = "TCP port not configured",
                Source = "tcp"
            };
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            var timeout = TimeSpan.FromSeconds(_options.BCInstance.HealthCheckTimeoutSeconds);
            
            using var cts = new CancellationTokenSource(timeout);
            await client.ConnectAsync("localhost", port.Value, cts.Token);
            
            sw.Stop();

            return new CheckResult
            {
                Status = HealthStatus.Healthy,
                LatencyMs = sw.ElapsedMilliseconds,
                Message = $"Port {port.Value} is open",
                Source = "tcp"
            };
        }
        catch (OperationCanceledException)
        {
            return new CheckResult
            {
                Status = HealthStatus.Unreachable,
                LatencyMs = sw.ElapsedMilliseconds,
                Message = $"TCP connection to port {port.Value} timed out",
                Source = "tcp"
            };
        }
        catch (SocketException ex)
        {
            return new CheckResult
            {
                Status = HealthStatus.Unreachable,
                LatencyMs = sw.ElapsedMilliseconds,
                Message = $"TCP port {port.Value} unreachable: {ex.Message}",
                Source = "tcp"
            };
        }
    }

    /// <summary>
    /// Windows Service status check
    /// </summary>
    private async Task<CheckResult> CheckServiceAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var serviceName = await GetServiceNameAsync();
            
            using var sc = new ServiceController(serviceName);
            var status = sc.Status;
            
            sw.Stop();

            if (status == ServiceControllerStatus.Running)
            {
                return new CheckResult
                {
                    Status = HealthStatus.Healthy,
                    LatencyMs = sw.ElapsedMilliseconds,
                    Message = $"Service '{serviceName}' is running",
                    Source = "service"
                };
            }

            return new CheckResult
            {
                Status = HealthStatus.Unhealthy,
                LatencyMs = sw.ElapsedMilliseconds,
                Message = $"Service '{serviceName}' status: {status}",
                Source = "service"
            };
        }
        catch (InvalidOperationException ex)
        {
            // Service not found - clear cache and let next call re-discover
            _cachedServiceName = null;
            
            return new CheckResult
            {
                Status = HealthStatus.Unreachable,
                LatencyMs = sw.ElapsedMilliseconds,
                Message = $"Service not found: {ex.Message}",
                Source = "service"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult
            {
                Status = HealthStatus.Unreachable,
                LatencyMs = sw.ElapsedMilliseconds,
                Message = $"Service check failed: {ex.Message}",
                Source = "service"
            };
        }
    }

    /// <summary>
    /// BC Performance Counter check - proves BC is actually processing
    /// </summary>
    private Task<CheckResult> CheckPerfCounterAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Try instance-specific category first (most common pattern)
            var categoryName = $"{BCPerfCounterCategory}: {_instanceName}";
            string? instanceParameter = null;

            if (!PerformanceCounterCategory.Exists(categoryName))
            {
                // Fallback to generic category (older BC versions or different installation)
                categoryName = BCPerfCounterCategory;

                if (!PerformanceCounterCategory.Exists(categoryName))
                {
                    return Task.FromResult(new CheckResult
                    {
                        Status = HealthStatus.Unreachable,
                        LatencyMs = sw.ElapsedMilliseconds,
                        Message = $"Performance counter category not found. Tried: " +
                                  $"'{BCPerfCounterCategory}: {_instanceName}' and '{BCPerfCounterCategory}'",
                        Source = "perfcounter"
                    });
                }

                // For generic category, need to specify instance name as parameter
                instanceParameter = _instanceName;
            }

            // "# Active Sessions" counter proves BC service is functioning
            using var counter = new PerformanceCounter(
                categoryName,
                "# Active Sessions",
                instanceParameter ?? "",
                readOnly: true);

            var value = counter.NextValue();
            sw.Stop();

            // If we can read the counter, BC is running
            return Task.FromResult(new CheckResult
            {
                Status = HealthStatus.Healthy,
                LatencyMs = sw.ElapsedMilliseconds,
                Value = value,
                Message = $"Active sessions: {value}",
                Source = "perfcounter"
            });
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(new CheckResult
            {
                Status = HealthStatus.Unreachable,
                LatencyMs = sw.ElapsedMilliseconds,
                Message = $"Performance counter not available: {ex.Message}",
                Source = "perfcounter"
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new CheckResult
            {
                Status = HealthStatus.Unreachable,
                LatencyMs = sw.ElapsedMilliseconds,
                Message = $"Performance counter check failed: {ex.Message}",
                Source = "perfcounter"
            });
        }
    }

    /// <summary>
    /// Get the Windows Service name, discovering it if necessary
    /// </summary>
    public async Task<string> GetServiceNameAsync()
    {
        // Return cached value if available
        if (!string.IsNullOrEmpty(_cachedServiceName))
            return _cachedServiceName;

        // Return configured value if specified
        if (!string.IsNullOrEmpty(_options.BCInstance.ServiceName))
        {
            _cachedServiceName = _options.BCInstance.ServiceName;
            return _cachedServiceName;
        }

        await _serviceNameLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (!string.IsNullOrEmpty(_cachedServiceName))
                return _cachedServiceName;

            // Try to discover via PowerShell
            var discovered = await DiscoverServiceNameViaPowerShellAsync();
            if (!string.IsNullOrEmpty(discovered))
            {
                _cachedServiceName = discovered;
                _logger.LogInformation("Discovered BC service name: {ServiceName}", _cachedServiceName);
                return _cachedServiceName;
            }

            // Fallback to standard pattern
            _cachedServiceName = $"MicrosoftDynamicsNavServer${_instanceName}";
            _logger.LogInformation("Using fallback BC service name pattern: {ServiceName}", _cachedServiceName);
            return _cachedServiceName;
        }
        finally
        {
            _serviceNameLock.Release();
        }
    }

    private async Task<string?> DiscoverServiceNameViaPowerShellAsync()
    {
        try
        {
            var script = BuildServiceDiscoveryScript();
            var result = await RunPowerShellAsync(script);
            
            var serviceName = result.Trim();
            if (!string.IsNullOrEmpty(serviceName) && serviceName.StartsWith("MicrosoftDynamicsNavServer$"))
            {
                return serviceName;
            }

            _logger.LogDebug("PowerShell service discovery returned unexpected result: {Result}", result);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PowerShell service discovery failed, will use fallback pattern");
            return null;
        }
    }

    private string BuildServiceDiscoveryScript()
    {
        return _installationType.Equals("LSUpdateService", StringComparison.OrdinalIgnoreCase)
            ? BuildLSUpdateServiceDiscoveryScript()
            : BuildStandardDiscoveryScript();
    }

    private string BuildStandardDiscoveryScript()
    {
        var adminToolPath = GetStandardAdminToolPath();
        
        return $@"
$ErrorActionPreference = 'Stop'
try {{
    Import-Module '{adminToolPath}' -ErrorAction Stop
    $instance = Get-NAVServerInstance '{_instanceName}' -ErrorAction Stop
    $instance.Name
}} catch {{
    # Return empty on failure - caller will use fallback
    ''
}}
";
    }

    private string BuildLSUpdateServiceDiscoveryScript()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("try {");
        
        if (!string.IsNullOrEmpty(_options.BCInstance.Installation.GocModulePath))
        {
            sb.AppendLine($"    Import-Module '{_options.BCInstance.Installation.GocModulePath}' -ErrorAction Stop");
        }
        
        sb.AppendLine($@"
    $BcServer = Get-GocInstalledPackage -Id 'bc-server' -InstanceName '{_instanceName}'
    if (!$BcServer) {{
        ''
        return
    }}

    # Import PowerShell Modules based on version
    if ([version]$BcServer.Version -lt [version]'24.0.0.0') {{
        Import-Module (Join-Path $BcServer.Info.ServerDir 'Microsoft.Dynamics.Nav.Management.dll') -Global
    }} else {{
        $managementFolder = Join-Path -Path $BcServer.Info.ServerDir -ChildPath 'Management'
        Import-Module (Join-Path -Path $managementFolder -ChildPath 'Microsoft.Dynamics.Nav.Management.dll') -NoClobber -Global
    }}

    $instance = Get-NAVServerInstance '{_instanceName}' -ErrorAction Stop
    $instance.Name
}} catch {{
    ''
}}
");
        return sb.ToString();
    }

    private string GetStandardAdminToolPath()
    {
        // If explicit path provided, use it
        if (!string.IsNullOrEmpty(_options.BCInstance.Installation.AdminToolPath))
        {
            return _options.BCInstance.Installation.AdminToolPath;
        }

        // If version specified, use it
        if (!string.IsNullOrEmpty(_options.BCInstance.Installation.Version))
        {
            return $@"C:\Program Files\Microsoft Dynamics 365 Business Central\{_options.BCInstance.Installation.Version}\Service\NavAdminTool.ps1";
        }

        // Try to auto-detect - find newest version
        var bcPath = @"C:\Program Files\Microsoft Dynamics 365 Business Central";
        if (Directory.Exists(bcPath))
        {
            var versions = Directory.GetDirectories(bcPath)
                .Select(d => new DirectoryInfo(d).Name)
                .Where(n => int.TryParse(n, out _))
                .OrderByDescending(n => int.Parse(n))
                .ToList();

            if (versions.Count > 0)
            {
                var latestVersion = versions.First();
                _logger.LogDebug("Auto-detected BC version: {Version}", latestVersion);
                return $@"C:\Program Files\Microsoft Dynamics 365 Business Central\{latestVersion}\Service\NavAdminTool.ps1";
            }
        }

        // Fallback
        _logger.LogWarning("Could not auto-detect BC version for admin tool path");
        return @"C:\Program Files\Microsoft Dynamics 365 Business Central\240\Service\NavAdminTool.ps1";
    }

    private async Task<string> RunPowerShellAsync(string script)
    {
        var scriptFile = Path.GetTempFileName() + ".ps1";
        await File.WriteAllTextAsync(scriptFile, script);
        
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptFile}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            
            // Wait with timeout
            var completed = await Task.Run(() => process.WaitForExit(10000));
            if (!completed)
            {
                try { process.Kill(); } catch { }
                throw new System.TimeoutException("PowerShell script timed out");
            }

            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogDebug("PowerShell stderr: {Error}", error);
            }

            return output;
        }
        finally
        {
            try { File.Delete(scriptFile); } catch { }
        }
    }
}
