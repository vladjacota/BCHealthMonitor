# Find service configuration
$service = Get-WmiObject -Class Win32_Service -Filter "Name='BCHealthMonitor'"
$exePath = $service.PathName.Trim('"')
$serviceDir = Split-Path $exePath

Write-Host "Service executable: $exePath" -ForegroundColor Cyan
Write-Host "Service directory: $serviceDir" -ForegroundColor Cyan
Write-Host ""

$configPath = Join-Path $serviceDir "appsettings.json"
if (Test-Path $configPath) {
    Write-Host "Configuration file:" -ForegroundColor Yellow
    Get-Content $configPath -Raw
} else {
    Write-Host "ERROR: Configuration file not found at $configPath" -ForegroundColor Red
}
