using System.Diagnostics;
using System.Text;
using BCHealthMonitor.Configuration;
using BCHealthMonitor.Models;
using Microsoft.Extensions.Options;

namespace BCHealthMonitor.Services;

public interface ISchedulerControlService
{
    Task<SchedulerState> GetStateAsync();
    Task<bool> EnableAsync(TimeSpan? duration = null);
    Task<bool> DisableAsync(TimeSpan? duration = null);
    Task ClearOverrideAsync();
    bool IsSchedulerEnabled { get; }
}

public class SchedulerControlService : ISchedulerControlService
{
    private readonly ILogger<SchedulerControlService> _logger;
    private readonly HealthMonitorOptions _options;
    private readonly string _instanceName;
    private readonly string _installationType;
    
    private SchedulerOverride? _currentOverride;
    private bool _lastKnownState = true;
    private DateTime _lastStateCheck = DateTime.MinValue;
    private readonly TimeSpan _stateCacheDuration = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsSchedulerEnabled => _lastKnownState;

    public SchedulerControlService(
        ILogger<SchedulerControlService> logger,
        IOptions<HealthMonitorOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _instanceName = _options.BCInstance.Name;
        _installationType = _options.BCInstance.Installation.Type;
        
        _logger.LogInformation("Scheduler control initialized for instance '{Instance}' using {InstallationType} installation",
            _instanceName, _installationType);
    }

    public async Task<SchedulerState> GetStateAsync()
    {
        await _lock.WaitAsync();
        try
        {
            // Check if override has expired
            if (_currentOverride?.IsExpired == true)
            {
                _logger.LogInformation("Scheduler override expired, reverting to business hours logic");
                _currentOverride = null;
            }

            var isBusinessHours = _options.BCInstance.SchedulerControl.BusinessHours.IsBusinessHours(DateTime.UtcNow);
            var enabled = await GetCurrentSchedulerStateAsync();

            var state = new SchedulerState
            {
                Enabled = enabled,
                IsBusinessHours = isBusinessHours,
                OverrideActive = _currentOverride != null,
                OverrideExpires = _currentOverride?.ExpiresAt,
                OverrideType = _currentOverride != null 
                    ? (_currentOverride.Enable 
                        ? (_currentOverride.Duration.HasValue ? SchedulerOverrideType.TemporaryEnable : SchedulerOverrideType.ManualEnable)
                        : (_currentOverride.Duration.HasValue ? SchedulerOverrideType.TemporaryDisable : SchedulerOverrideType.ManualDisable))
                    : null,
                LastChecked = DateTime.UtcNow
            };

            // Determine reason
            if (_currentOverride != null)
            {
                state.Reason = _currentOverride.Enable 
                    ? "Manual override: enabled" 
                    : "Manual override: disabled";
            }
            else if (isBusinessHours)
            {
                state.Reason = "Business hours: scheduler disabled";
            }
            else
            {
                state.Reason = "Off hours: scheduler enabled";
            }

            return state;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> EnableAsync(TimeSpan? duration = null)
    {
        await _lock.WaitAsync();
        try
        {
            _logger.LogInformation("Enabling scheduler{Duration}", 
                duration.HasValue ? $" for {duration.Value}" : " (permanent override)");

            var success = await SetSchedulerStateAsync(true);
            
            if (success)
            {
                _currentOverride = new SchedulerOverride
                {
                    Enable = true,
                    Duration = duration
                };
            }

            return success;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DisableAsync(TimeSpan? duration = null)
    {
        await _lock.WaitAsync();
        try
        {
            _logger.LogInformation("Disabling scheduler{Duration}", 
                duration.HasValue ? $" for {duration.Value}" : " (permanent override)");

            var success = await SetSchedulerStateAsync(false);
            
            if (success)
            {
                _currentOverride = new SchedulerOverride
                {
                    Enable = false,
                    Duration = duration
                };
            }

            return success;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task ClearOverrideAsync()
    {
        _logger.LogInformation("Clearing scheduler override");
        _currentOverride = null;
        return Task.CompletedTask;
    }

    private async Task<bool> GetCurrentSchedulerStateAsync()
    {
        // Return cached state if still valid (avoids expensive PowerShell calls)
        if (DateTime.UtcNow - _lastStateCheck < _stateCacheDuration)
        {
            _logger.LogDebug("Using cached scheduler state: {State}", _lastKnownState);
            return _lastKnownState;
        }

        try
        {
            var script = BuildGetSchedulerStateScript();
            var result = await RunPowerShellAsync(script);
            _lastKnownState = result.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            _lastStateCheck = DateTime.UtcNow;
            return _lastKnownState;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get scheduler state from BC, using last known state: {State}", _lastKnownState);
            return _lastKnownState;
        }
    }

    private async Task<bool> SetSchedulerStateAsync(bool enabled)
    {
        try
        {
            var script = BuildSetSchedulerStateScript(enabled);
            var result = await RunPowerShellAsync(script);
            var success = result.Contains("SUCCESS");

            if (success)
            {
                _lastKnownState = enabled;
                _lastStateCheck = DateTime.UtcNow; // Update cache timestamp
                _logger.LogInformation("Scheduler state changed to: {Enabled}", enabled);
            }
            else
            {
                _logger.LogError("Failed to set scheduler state. PowerShell output: {Output}", result);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set scheduler state to {Enabled}", enabled);
            return false;
        }
    }

    private string BuildGetSchedulerStateScript()
    {
        return _installationType.Equals("LSUpdateService", StringComparison.OrdinalIgnoreCase)
            ? BuildLSUpdateServiceGetScript()
            : BuildStandardGetScript();
    }

    private string BuildSetSchedulerStateScript(bool enabled)
    {
        return _installationType.Equals("LSUpdateService", StringComparison.OrdinalIgnoreCase)
            ? BuildLSUpdateServiceSetScript(enabled)
            : BuildStandardSetScript(enabled);
    }

    private string BuildStandardGetScript()
    {
        var adminToolPath = GetStandardAdminToolPath();
        
        return $@"
$ErrorActionPreference = 'Stop'
Import-Module '{adminToolPath}' -ErrorAction Stop
$config = Get-NAVServerConfiguration -ServerInstance '{_instanceName}' -KeyName 'EnableTaskScheduler'
$config.Value
";
    }

    private string BuildStandardSetScript(bool enabled)
    {
        var adminToolPath = GetStandardAdminToolPath();
        var value = enabled.ToString().ToLower();
        
        return $@"
$ErrorActionPreference = 'Stop'
Import-Module '{adminToolPath}' -ErrorAction Stop
Set-NAVServerConfiguration -ServerInstance '{_instanceName}' -KeyName 'EnableTaskScheduler' -KeyValue '{value}' -ApplyTo All -Force
Write-Output 'SUCCESS'
";
    }

    private string BuildLSUpdateServiceGetScript()
    {
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        
        // Load GoCurrent module if path specified
        if (!string.IsNullOrEmpty(_options.BCInstance.Installation.GocModulePath))
        {
            sb.AppendLine($"Import-Module '{_options.BCInstance.Installation.GocModulePath}' -ErrorAction Stop");
        }
        
        sb.AppendLine($@"
$BcServer = Get-GocInstalledPackage -Id 'bc-server' -InstanceName '{_instanceName}'
if (!$BcServer) {{
    throw 'Specified instance ({_instanceName}) does not exist or is not a Business Central instance.'
}}

# Import PowerShell Modules based on version
if ([version]$BcServer.Version -lt [version]'24.0.0.0') {{
    Import-Module (Join-Path $BcServer.Info.ServerDir 'Microsoft.Dynamics.Nav.Management.dll') -Global
}} else {{
    $managementFolder = Join-Path -Path $BcServer.Info.ServerDir -ChildPath 'Management'
    Import-Module (Join-Path -Path $managementFolder -ChildPath 'Microsoft.Dynamics.Nav.Management.dll') -NoClobber -Global
}}

$config = Get-NAVServerConfiguration -ServerInstance '{_instanceName}' -KeyName 'EnableTaskScheduler'
$config.Value
");
        return sb.ToString();
    }

    private string BuildLSUpdateServiceSetScript(bool enabled)
    {
        var value = enabled.ToString().ToLower();
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        
        // Load GoCurrent module if path specified
        if (!string.IsNullOrEmpty(_options.BCInstance.Installation.GocModulePath))
        {
            sb.AppendLine($"Import-Module '{_options.BCInstance.Installation.GocModulePath}' -ErrorAction Stop");
        }
        
        sb.AppendLine($@"
$BcServer = Get-GocInstalledPackage -Id 'bc-server' -InstanceName '{_instanceName}'
if (!$BcServer) {{
    throw 'Specified instance ({_instanceName}) does not exist or is not a Business Central instance.'
}}

# Import PowerShell Modules based on version
if ([version]$BcServer.Version -lt [version]'24.0.0.0') {{
    Import-Module (Join-Path $BcServer.Info.ServerDir 'Microsoft.Dynamics.Nav.Management.dll') -Global
}} else {{
    $managementFolder = Join-Path -Path $BcServer.Info.ServerDir -ChildPath 'Management'
    Import-Module (Join-Path -Path $managementFolder -ChildPath 'Microsoft.Dynamics.Nav.Management.dll') -NoClobber -Global
}}

Set-NAVServerConfiguration -ServerInstance '{_instanceName}' -KeyName 'EnableTaskScheduler' -KeyValue '{value}' -ApplyTo All -Force
Write-Output 'SUCCESS'
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

            if (versions.Any())
            {
                var latestVersion = versions.First();
                _logger.LogInformation("Auto-detected BC version: {Version}", latestVersion);
                return $@"C:\Program Files\Microsoft Dynamics 365 Business Central\{latestVersion}\Service\NavAdminTool.ps1";
            }
        }

        // Fallback to latest known version (no wildcard - causes Import-Module to fail)
        _logger.LogWarning("Could not auto-detect BC version, using fallback version 240");
        return @"C:\Program Files\Microsoft Dynamics 365 Business Central\240\Service\NavAdminTool.ps1";
    }

    private async Task<string> RunPowerShellAsync(string script)
    {
        // Write script to temp file to avoid escaping issues
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
            
            await process.WaitForExitAsync();

            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogWarning("PowerShell stderr: {Error}", error);
            }

            return output;
        }
        finally
        {
            // Clean up temp file
            try { File.Delete(scriptFile); } catch { }
        }
    }
}
