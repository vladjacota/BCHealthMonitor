#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs BC Health Monitor as a Windows Service.

.DESCRIPTION
    This script installs the BC Health Monitor service, creates necessary directories,
    sets up firewall rules, and configures the service to run under the specified account.

.PARAMETER ServiceName
    Name of the Windows service. Default: BCHealthMonitor

.PARAMETER InstallPath
    Installation directory. Default: C:\Services\BCHealthMonitor

.PARAMETER ServiceAccount
    Account to run the service under. Default: LocalSystem
    For BC access, use the same account as the BC service.

.PARAMETER Port
    Health check endpoint port. Default: 5080

.EXAMPLE
    .\install.ps1 -ServiceName "BCHealthMonitor" -Port 5080

.EXAMPLE
    .\install.ps1 -ServiceAccount "DOMAIN\BCServiceAccount"
#>

param(
    [string]$ServiceName = "BCHealthMonitor",
    [string]$InstallPath = "C:\Services\BCHealthMonitor",
    [string]$ServiceAccount = "LocalSystem",
    [int]$Port = 5080
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "BC Health Monitor - Installation Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if service already exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Service '$ServiceName' already exists." -ForegroundColor Yellow
    $response = Read-Host "Do you want to stop and remove it? (Y/N)"
    if ($response -eq 'Y' -or $response -eq 'y') {
        Write-Host "Stopping existing service..." -ForegroundColor Yellow
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        
        Write-Host "Removing existing service..." -ForegroundColor Yellow
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }
    else {
        Write-Host "Installation cancelled." -ForegroundColor Red
        exit 1
    }
}

# Create installation directory
Write-Host "Creating installation directory: $InstallPath" -ForegroundColor Green
if (-not (Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
}

# Copy files
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Write-Host "Copying files from: $scriptDir" -ForegroundColor Green

$filesToCopy = @(
    "BCHealthMonitor.exe",
    "appsettings.json"
)

foreach ($file in $filesToCopy) {
    $sourcePath = Join-Path $scriptDir $file
    if (Test-Path $sourcePath) {
        Copy-Item -Path $sourcePath -Destination $InstallPath -Force
        Write-Host "  Copied: $file" -ForegroundColor Gray
    }
    else {
        Write-Host "  Warning: $file not found in source directory" -ForegroundColor Yellow
    }
}

# Create log directory
$logPath = "C:\Logs\BCHealthMonitor"
Write-Host "Creating log directory: $logPath" -ForegroundColor Green
if (-not (Test-Path $logPath)) {
    New-Item -ItemType Directory -Path $logPath -Force | Out-Null
}

# Update appsettings.json with port if different from default
$settingsPath = Join-Path $InstallPath "appsettings.json"
if (Test-Path $settingsPath) {
    $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    if ($settings.Server.Port -ne $Port) {
        $settings.Server.Port = $Port
        $settings | ConvertTo-Json -Depth 10 | Set-Content $settingsPath
        Write-Host "Updated port in appsettings.json to: $Port" -ForegroundColor Green
    }
}

# Create Windows Service
$exePath = Join-Path $InstallPath "BCHealthMonitor.exe"
Write-Host "Creating Windows Service: $ServiceName" -ForegroundColor Green

$serviceParams = @{
    Name = $ServiceName
    BinaryPathName = $exePath
    DisplayName = "BC Health Monitor"
    Description = "Monitors Business Central health and controls task scheduler based on business hours"
    StartupType = "Automatic"
}

if ($ServiceAccount -ne "LocalSystem") {
    # Prompt for password if using a specific account
    $credential = Get-Credential -UserName $ServiceAccount -Message "Enter credentials for service account"
    New-Service @serviceParams -Credential $credential
}
else {
    New-Service @serviceParams
}

# Configure service recovery options
Write-Host "Configuring service recovery options..." -ForegroundColor Green
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

# Create firewall rule
Write-Host "Creating firewall rule for port $Port..." -ForegroundColor Green
$ruleName = "BCHealthMonitor-HTTP-$Port"
$existingRule = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
if ($existingRule) {
    Remove-NetFirewallRule -DisplayName $ruleName
}

New-NetFirewallRule -DisplayName $ruleName `
    -Direction Inbound `
    -Protocol TCP `
    -LocalPort $Port `
    -Action Allow `
    -Profile Domain,Private `
    -Description "Allow BC Health Monitor HTTP traffic" | Out-Null

# Create Event Log source
Write-Host "Registering Event Log source..." -ForegroundColor Green
if (-not [System.Diagnostics.EventLog]::SourceExists("BCHealthMonitor")) {
    [System.Diagnostics.EventLog]::CreateEventSource("BCHealthMonitor", "Application")
}

# Start the service
Write-Host "Starting service..." -ForegroundColor Green
Start-Service -Name $ServiceName

# Wait for service to start
Start-Sleep -Seconds 3

# Verify service is running
$service = Get-Service -Name $ServiceName
if ($service.Status -eq "Running") {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "Installation completed successfully!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Service Name:    $ServiceName"
    Write-Host "Install Path:    $InstallPath"
    Write-Host "Health Endpoint: http://localhost:$Port/health"
    Write-Host "Status Page:     http://localhost:$Port/status"
    Write-Host "Metrics:         http://localhost:$Port/metrics"
    Write-Host ""
    Write-Host "Configuration:   $settingsPath"
    Write-Host "Logs:            $logPath"
    Write-Host ""
}
else {
    Write-Host ""
    Write-Host "Warning: Service installed but not running. Status: $($service.Status)" -ForegroundColor Yellow
    Write-Host "Check the Event Log for errors." -ForegroundColor Yellow
}
