#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Uninstalls BC Health Monitor Windows Service.

.DESCRIPTION
    This script stops and removes the BC Health Monitor service,
    removes firewall rules, and optionally removes installation files.

.PARAMETER ServiceName
    Name of the Windows service. Default: BCHealthMonitor

.PARAMETER InstallPath
    Installation directory. Default: C:\Services\BCHealthMonitor

.PARAMETER RemoveFiles
    If specified, removes installation files and logs.

.PARAMETER Port
    Port used by the service (for firewall rule removal). Default: 5080

.EXAMPLE
    .\uninstall.ps1

.EXAMPLE
    .\uninstall.ps1 -RemoveFiles
#>

param(
    [string]$ServiceName = "BCHealthMonitor",
    [string]$InstallPath = "C:\Services\BCHealthMonitor",
    [switch]$RemoveFiles,
    [int]$Port = 5080
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "BC Health Monitor - Uninstallation Script" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# Stop service if running
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -eq "Running") {
        Write-Host "Stopping service..." -ForegroundColor Yellow
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 3
    }

    # Remove service
    Write-Host "Removing service..." -ForegroundColor Yellow
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    Write-Host "  Service removed." -ForegroundColor Green
}
else {
    Write-Host "Service '$ServiceName' not found." -ForegroundColor Yellow
}

# Remove firewall rule
$ruleName = "BCHealthMonitor-HTTP-$Port"
$firewallRule = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
if ($firewallRule) {
    Write-Host "Removing firewall rule..." -ForegroundColor Yellow
    Remove-NetFirewallRule -DisplayName $ruleName
    Write-Host "  Firewall rule removed." -ForegroundColor Green
}

# Remove Event Log source
if ([System.Diagnostics.EventLog]::SourceExists("BCHealthMonitor")) {
    Write-Host "Removing Event Log source..." -ForegroundColor Yellow
    [System.Diagnostics.EventLog]::DeleteEventSource("BCHealthMonitor")
    Write-Host "  Event Log source removed." -ForegroundColor Green
}

# Remove files if requested
if ($RemoveFiles) {
    if (Test-Path $InstallPath) {
        Write-Host "Removing installation files from: $InstallPath" -ForegroundColor Yellow
        Remove-Item -Path $InstallPath -Recurse -Force
        Write-Host "  Installation files removed." -ForegroundColor Green
    }

    $logPath = "C:\Logs\BCHealthMonitor"
    if (Test-Path $logPath) {
        $response = Read-Host "Remove log files at $logPath? (Y/N)"
        if ($response -eq 'Y' -or $response -eq 'y') {
            Remove-Item -Path $logPath -Recurse -Force
            Write-Host "  Log files removed." -ForegroundColor Green
        }
    }
}
else {
    Write-Host ""
    Write-Host "Note: Installation files were not removed." -ForegroundColor Yellow
    Write-Host "      Use -RemoveFiles switch to remove them." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Green
Write-Host "Uninstallation completed." -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
