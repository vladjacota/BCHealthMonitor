# Enable Debug Logging
Write-Host "Enabling Debug Logging for BCHealthMonitor..." -ForegroundColor Cyan
Write-Host ""

$servicePath = "C:\Services\BCHealthMonitor"
$configPath = Join-Path $servicePath "appsettings.json"

if (-not (Test-Path $configPath)) {
    Write-Host "ERROR: Config not found at $configPath" -ForegroundColor Red
    exit 1
}

# Read current config
$config = Get-Content $configPath -Raw | ConvertFrom-Json

# Add Serilog minimum level configuration if it doesn't exist
if (-not $config.PSObject.Properties['Serilog']) {
    $config | Add-Member -MemberType NoteProperty -Name "Serilog" -Value ([PSCustomObject]@{
        MinimumLevel = [PSCustomObject]@{
            Default = "Debug"
        }
    })
} else {
    $config.Serilog.MinimumLevel.Default = "Debug"
}

# Save config
$config | ConvertTo-Json -Depth 10 | Set-Content $configPath

Write-Host "Debug logging enabled" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Restart the service (requires admin):" -ForegroundColor White
Write-Host "   Restart-Service BCHealthMonitor" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Trigger a health check:" -ForegroundColor White
Write-Host "   Invoke-RestMethod http://localhost:5080/health/details" -ForegroundColor Gray
Write-Host ""
Write-Host "3. Check logs for SQL errors:" -ForegroundColor White
Write-Host "   Get-Content 'C:\Logs\BCHealthMonitor\*.log' -Tail 50 | Select-String 'SQL|Session'" -ForegroundColor Gray
Write-Host ""
