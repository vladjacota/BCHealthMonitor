# Deploy and Restart BCHealthMonitor Service
Write-Host "BCHealthMonitor - Deploy and Restart" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Configuration
$serviceName = "BCHealthMonitor"
$buildPath = "C:\work\LS Retail\BCHealthMonitor\src\BCHealthMonitor\bin\Debug\net8.0\win-x64"
$servicePath = "C:\Services\BCHealthMonitor"

# Step 1: Check if service exists
Write-Host "[1] Checking service status..." -ForegroundColor Yellow
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($null -eq $service) {
    Write-Host "  ERROR: Service '$serviceName' not found!" -ForegroundColor Red
    Write-Host "  Please verify the service name or installation." -ForegroundColor Red
    exit 1
}

Write-Host "  Service found: $($service.Status)" -ForegroundColor Green
Write-Host ""

# Step 2: Stop service
Write-Host "[2] Stopping service..." -ForegroundColor Yellow
try {
    Stop-Service -Name $serviceName -Force -ErrorAction Stop
    Start-Sleep -Seconds 2
    Write-Host "  Service stopped" -ForegroundColor Green
} catch {
    Write-Host "  ERROR: Failed to stop service: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Step 3: Verify build exists
Write-Host "[3] Checking build..." -ForegroundColor Yellow
if (-not (Test-Path $buildPath)) {
    Write-Host "  ERROR: Build path not found: $buildPath" -ForegroundColor Red
    exit 1
}

$dllPath = Join-Path $buildPath "BCHealthMonitor.dll"
if (-not (Test-Path $dllPath)) {
    Write-Host "  ERROR: BCHealthMonitor.dll not found in build path" -ForegroundColor Red
    exit 1
}

Write-Host "  Build found: $buildPath" -ForegroundColor Green
Write-Host ""

# Step 4: Backup current version
Write-Host "[4] Creating backup..." -ForegroundColor Yellow
$backupPath = "$servicePath.backup.$(Get-Date -Format 'yyyyMMdd-HHmmss')"
try {
    Copy-Item -Path $servicePath -Destination $backupPath -Recurse -Force
    Write-Host "  Backup created: $backupPath" -ForegroundColor Green
} catch {
    Write-Host "  WARNING: Failed to create backup: $($_.Exception.Message)" -ForegroundColor Yellow
}
Write-Host ""

# Step 5: Deploy new version
Write-Host "[5] Deploying new version..." -ForegroundColor Yellow
try {
    Copy-Item -Path "$buildPath\*" -Destination $servicePath -Recurse -Force
    Write-Host "  Files copied successfully" -ForegroundColor Green
} catch {
    Write-Host "  ERROR: Failed to copy files: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Attempting to restore backup..." -ForegroundColor Yellow
    Copy-Item -Path "$backupPath\*" -Destination $servicePath -Recurse -Force
    exit 1
}
Write-Host ""

# Step 6: Verify configuration
Write-Host "[6] Verifying configuration..." -ForegroundColor Yellow
$configPath = Join-Path $servicePath "appsettings.json"
if (Test-Path $configPath) {
    $config = Get-Content $configPath -Raw | ConvertFrom-Json
    Write-Host "  Database: $($config.BCInstance.SqlConnectionString)" -ForegroundColor White
    Write-Host "  Tenant DBs: $($config.BCInstance.TenantDatabases.Count) configured" -ForegroundColor White
    Write-Host "  Config verified" -ForegroundColor Green
} else {
    Write-Host "  WARNING: appsettings.json not found!" -ForegroundColor Red
}
Write-Host ""

# Step 7: Start service
Write-Host "[7] Starting service..." -ForegroundColor Yellow
try {
    Start-Service -Name $serviceName -ErrorAction Stop
    Start-Sleep -Seconds 3

    $service = Get-Service -Name $serviceName
    if ($service.Status -eq "Running") {
        Write-Host "  Service started successfully" -ForegroundColor Green
    } else {
        Write-Host "  WARNING: Service status is $($service.Status)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  ERROR: Failed to start service: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Step 8: Test endpoints
Write-Host "[8] Testing health endpoints..." -ForegroundColor Yellow
Start-Sleep -Seconds 5  # Give service time to initialize

try {
    $health = Invoke-RestMethod -Uri "http://localhost:5080/health/details" -TimeoutSec 10
    Write-Host "  Status: $($health.status)" -ForegroundColor $(if ($health.status -eq "Healthy") { "Green" } else { "Yellow" })
    Write-Host "  Sessions Source: $($health.sessions.source)" -ForegroundColor $(if ($health.sessions.source -ne "none") { "Green" } else { "Red" })
    Write-Host "  Web Client: $($health.sessions.web_client)" -ForegroundColor White
    Write-Host "  Total: $($health.sessions.total)" -ForegroundColor White
} catch {
    Write-Host "  ERROR: Health endpoint not responding: $($_.Exception.Message)" -ForegroundColor Red
}
Write-Host ""

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Deployment Complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  - Open http://localhost:5080/status in browser" -ForegroundColor White
Write-Host "  - Check logs: $($config.Logging.FilePath)" -ForegroundColor White
Write-Host ""
