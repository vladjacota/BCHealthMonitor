# Update BCHealthMonitor Service
# This script stops the service, deploys the new version, and restarts it

param(
    [switch]$SkipBuild
)

Write-Host "=== BCHealthMonitor Service Update ===" -ForegroundColor Cyan
Write-Host ""

# Check if running as administrator
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: This script requires administrator privileges" -ForegroundColor Red
    Write-Host "Please run PowerShell as Administrator and try again" -ForegroundColor Yellow
    exit 1
}

$projectPath = "C:\work\LS Retail\BCHealthMonitor"
$serviceDir = "C:\Services\BCHealthMonitor"

# Step 1: Build (unless skipped)
if (-not $SkipBuild) {
    Write-Host "[1/4] Building project..." -ForegroundColor Yellow
    Set-Location $projectPath
    dotnet build src/BCHealthMonitor/BCHealthMonitor.csproj -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Build failed" -ForegroundColor Red
        exit 1
    }
    Write-Host "  Build successful" -ForegroundColor Green
    Write-Host ""
}

# Step 2: Stop service
Write-Host "[2/4] Stopping BCHealthMonitor service..." -ForegroundColor Yellow
$service = Get-Service -Name BCHealthMonitor -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -eq 'Running') {
        Stop-Service -Name BCHealthMonitor -Force
        Write-Host "  Service stopped" -ForegroundColor Green
    } else {
        Write-Host "  Service already stopped" -ForegroundColor Gray
    }
} else {
    Write-Host "  WARNING: Service not found" -ForegroundColor Yellow
}
Write-Host ""

# Wait a moment for files to be released
Start-Sleep -Seconds 2

# Step 3: Deploy
Write-Host "[3/4] Deploying to $serviceDir..." -ForegroundColor Yellow
Set-Location $projectPath
dotnet publish src/BCHealthMonitor/BCHealthMonitor.csproj -c Release -r win-x64 --self-contained false -o $serviceDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Publish failed" -ForegroundColor Red
    Write-Host "Attempting to restart service anyway..." -ForegroundColor Yellow
    Start-Service -Name BCHealthMonitor
    exit 1
}
Write-Host "  Deploy successful" -ForegroundColor Green
Write-Host ""

# Step 4: Start service
Write-Host "[4/4] Starting BCHealthMonitor service..." -ForegroundColor Yellow
Start-Service -Name BCHealthMonitor
Start-Sleep -Seconds 3

$service = Get-Service -Name BCHealthMonitor
Write-Host "  Service status: $($service.Status)" -ForegroundColor $(if ($service.Status -eq 'Running') { 'Green' } else { 'Red' })
Write-Host ""

# Step 5: Verify
Write-Host "=== Verification ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Waiting 5 seconds for service to initialize..." -ForegroundColor Gray
Start-Sleep -Seconds 5

try {
    $response = Invoke-RestMethod -Uri "http://localhost:5080/health/details" -ErrorAction Stop
    Write-Host "Health check successful:" -ForegroundColor Green
    Write-Host "  Instance: $($response.instance_name)" -ForegroundColor White
    Write-Host "  Status: $($response.status)" -ForegroundColor White
    Write-Host "  Sessions: $($response.sessions.total) (source: $($response.sessions.source))" -ForegroundColor White
    Write-Host "  Uptime: $($response.uptime)" -ForegroundColor White
} catch {
    Write-Host "WARNING: Health check failed: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host "Check logs at: C:\Logs\BCHealthMonitor\" -ForegroundColor Gray
}

Write-Host ""
Write-Host "=== Update Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "To check debug logs:" -ForegroundColor White
Write-Host "  Get-Content 'C:\Logs\BCHealthMonitor\*.log' -Tail 100 | Select-String 'DBG|Failed|SQL|Session'" -ForegroundColor Gray
Write-Host ""
