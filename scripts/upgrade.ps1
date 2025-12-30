#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Upgrades BC Health Monitor Windows Service.

.DESCRIPTION
    This script stops the service, backs up configuration, copies new files,
    restores configuration, and starts the service.

.PARAMETER ServiceName
    Name of the Windows service. Default: BCHealthMonitor

.PARAMETER InstallPath
    Installation directory. Default: C:\Services\BCHealthMonitor

.EXAMPLE
    .\upgrade.ps1
#>

param(
    [string]$ServiceName = "BCHealthMonitor",
    [string]$InstallPath = "C:\Services\BCHealthMonitor"
)

$ErrorActionPreference = "Stop"

Write-Host "======================================" -ForegroundColor Cyan
Write-Host "BC Health Monitor - Upgrade Script" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$settingsPath = Join-Path $InstallPath "appsettings.json"
$backupPath = Join-Path $InstallPath "appsettings.backup.json"

# Verify service exists
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Host "Service '$ServiceName' not found. Please run install.ps1 first." -ForegroundColor Red
    exit 1
}

# Stop service
Write-Host "Stopping service..." -ForegroundColor Yellow
Stop-Service -Name $ServiceName -Force
Start-Sleep -Seconds 3

# Backup configuration
if (Test-Path $settingsPath) {
    Write-Host "Backing up configuration..." -ForegroundColor Green
    Copy-Item -Path $settingsPath -Destination $backupPath -Force
}

# Copy new files (except appsettings.json)
Write-Host "Copying new files..." -ForegroundColor Green
$filesToCopy = Get-ChildItem -Path $scriptDir -File | Where-Object { $_.Name -ne "appsettings.json" -and $_.Extension -ne ".ps1" }

foreach ($file in $filesToCopy) {
    Copy-Item -Path $file.FullName -Destination $InstallPath -Force
    Write-Host "  Copied: $($file.Name)" -ForegroundColor Gray
}

# Restore configuration
if (Test-Path $backupPath) {
    Write-Host "Restoring configuration..." -ForegroundColor Green
    Copy-Item -Path $backupPath -Destination $settingsPath -Force
    Remove-Item -Path $backupPath -Force
}

# Start service
Write-Host "Starting service..." -ForegroundColor Green
Start-Service -Name $ServiceName

# Wait and verify
Start-Sleep -Seconds 3
$service = Get-Service -Name $ServiceName

if ($service.Status -eq "Running") {
    Write-Host ""
    Write-Host "======================================" -ForegroundColor Green
    Write-Host "Upgrade completed successfully!" -ForegroundColor Green
    Write-Host "======================================" -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "Warning: Service not running after upgrade. Status: $($service.Status)" -ForegroundColor Yellow
    Write-Host "Check the Event Log for errors." -ForegroundColor Yellow
}
