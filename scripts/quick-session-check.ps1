# Quick Session Check
Write-Host "BC Health Monitor - Quick Session Check" -ForegroundColor Cyan
Write-Host ""

# 1. Check what source the health monitor is using
Write-Host "[1] Checking Health Monitor..." -ForegroundColor Yellow
try {
    $health = Invoke-RestMethod -Uri "http://localhost:5080/health/details"
    Write-Host "  Source: $($health.sessions.source)" -ForegroundColor $(if ($health.sessions.source -eq "none") { "Red" } else { "Green" })
    Write-Host "  Web Client: $($health.sessions.web_client)" -ForegroundColor White
    Write-Host "  Total: $($health.sessions.total)" -ForegroundColor White
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# 2. Check SQL
Write-Host "[2] Checking SQL Database (BC_Tenant1)..." -ForegroundColor Yellow
try {
    $conn = New-Object System.Data.SqlClient.SqlConnection
    $conn.ConnectionString = "Server=localhost;Database=BC_Tenant1;Integrated Security=true;TrustServerCertificate=true"
    $conn.Open()

    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT [Client Type], COUNT(*) as cnt FROM [dbo].[Active Session] GROUP BY [Client Type]"
    $reader = $cmd.ExecuteReader()

    $found = $false
    while ($reader.Read()) {
        $found = $true
        Write-Host "  $($reader.GetString(0)): $($reader.GetInt32(1))" -ForegroundColor Green
    }

    if (-not $found) {
        Write-Host "  No sessions found in database" -ForegroundColor Yellow
    }

    $reader.Close()
    $conn.Close()
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# 3. Check Performance Counters
Write-Host "[3] Checking Performance Counters..." -ForegroundColor Yellow
try {
    $counter = New-Object System.Diagnostics.PerformanceCounter("Microsoft Dynamics 365 Business Central: BC", "# Active Sessions", "", $true)
    $value = $counter.NextValue()
    Write-Host "  Active Sessions: $value" -ForegroundColor Green
    $counter.Dispose()
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
