# Session Data Diagnostic Script
# This script helps diagnose why session counts show 0

Write-Host "BC Health Monitor - Session Data Diagnostic" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

$instanceName = "BC"

# 1. Check Performance Counters
Write-Host "[1] Checking Performance Counters..." -ForegroundColor Yellow
Write-Host ""

$perfCounterCategories = @(
    "Microsoft Dynamics 365 Business Central: $instanceName",
    "Microsoft Dynamics 365 Business Central"
)

foreach ($category in $perfCounterCategories) {
    try {
        if ([System.Diagnostics.PerformanceCounterCategory]::Exists($category)) {
            Write-Host "  ✓ Found category: $category" -ForegroundColor Green

            try {
                $counter = New-Object System.Diagnostics.PerformanceCounter($category, "# Active Sessions", "", $true)
                $value = $counter.NextValue()
                Write-Host "    Active Sessions: $value" -ForegroundColor Green
                $counter.Dispose()
            } catch {
                Write-Host "    Could not read '# Active Sessions' counter: $($_.Exception.Message)" -ForegroundColor Red
            }
        } else {
            Write-Host "  ✗ Category not found: $category" -ForegroundColor Red
        }
    } catch {
        Write-Host "  Error checking category '$category': $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""

# 2. Check SQL Databases
Write-Host "[2] Checking SQL Server Databases..." -ForegroundColor Yellow
Write-Host ""

$sqlServer = "localhost"
$integratedSecurity = $true

try {
    $connectionString = "Server=$sqlServer;Database=master;Integrated Security=true;TrustServerCertificate=true"
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()

    Write-Host "  ✓ Connected to SQL Server: $sqlServer" -ForegroundColor Green

    # Find BC databases
    $query = @"
SELECT name
FROM sys.databases
WHERE name LIKE '%BC%' OR name LIKE '%NAV%' OR name LIKE '%Tenant%'
ORDER BY name
"@

    $command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
    $reader = $command.ExecuteReader()

    Write-Host ""
    Write-Host "  Potential BC Databases:" -ForegroundColor Cyan
    $databases = @()
    while ($reader.Read()) {
        $dbName = $reader["name"]
        $databases += $dbName
        Write-Host "    - $dbName" -ForegroundColor White
    }
    $reader.Close()

    if ($databases.Count -eq 0) {
        Write-Host "    No BC-related databases found!" -ForegroundColor Red
        Write-Host "    Listing all databases..." -ForegroundColor Yellow

        $query = "SELECT name FROM sys.databases ORDER BY name"
        $command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
        $reader = $command.ExecuteReader()
        while ($reader.Read()) {
            Write-Host "    - $($reader['name'])" -ForegroundColor Gray
        }
        $reader.Close()
    }

    # Check each database for Active Session table
    Write-Host ""
    Write-Host "  Checking for [Active Session] table:" -ForegroundColor Cyan

    foreach ($db in $databases) {
        $connection.ChangeDatabase($db)

        $query = @"
SELECT COUNT(*) as TableExists
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_NAME = 'Active Session'
"@

        $command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
        $tableExists = $command.ExecuteScalar()

        if ($tableExists -gt 0) {
            Write-Host "    ✓ Database '$db' has [Active Session] table" -ForegroundColor Green

            # Check session counts
            $sessionQuery = "SELECT [Client Type], COUNT(*) as SessionCount FROM [dbo].[Active Session] GROUP BY [Client Type]"

            $command = New-Object System.Data.SqlClient.SqlCommand($sessionQuery, $connection)
            $reader = $command.ExecuteReader()

            $hasData = $false
            while ($reader.Read()) {
                $hasData = $true
                $clientType = $reader.GetString(0)
                $count = $reader.GetInt32(1)
                Write-Host "      - $clientType : $count" -ForegroundColor White
            }
            $reader.Close()

            if (-not $hasData) {
                Write-Host "      (No active sessions in this database)" -ForegroundColor Gray
            }
        } else {
            Write-Host "    ✗ Database '$db' does NOT have [Active Session] table" -ForegroundColor Yellow
        }
    }

    $connection.Close()

} catch {
    Write-Host "  ✗ Error connecting to SQL Server: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# 3. Check BC API
Write-Host "[3] Checking BC API..." -ForegroundColor Yellow
Write-Host ""

$baseUrl = "http://localhost:7048/BC"
$apiUrl = "$baseUrl/api/microsoft/runtime/v1.0/sessions"

try {
    $response = Invoke-WebRequest -Uri $apiUrl -UseDefaultCredentials -ErrorAction Stop

    if ($response.StatusCode -eq 200) {
        Write-Host "  ✓ API endpoint accessible: $apiUrl" -ForegroundColor Green

        $sessions = ($response.Content | ConvertFrom-Json).value
        Write-Host "    Total sessions: $($sessions.Count)" -ForegroundColor White

        $sessionTypes = $sessions | Group-Object -Property clientType
        foreach ($type in $sessionTypes) {
            Write-Host "    - $($type.Name): $($type.Count)" -ForegroundColor White
        }
    }
} catch {
    Write-Host "  ✗ API endpoint not accessible: $apiUrl" -ForegroundColor Red
    Write-Host "    Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# 4. Check Health Monitor API
Write-Host "[4] Checking Health Monitor API..." -ForegroundColor Yellow
Write-Host ""

$healthUrl = "http://localhost:5080/health/details"

try {
    $response = Invoke-RestMethod -Uri $healthUrl -ErrorAction Stop

    Write-Host "  ✓ Health Monitor accessible: $healthUrl" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Current Session Data:" -ForegroundColor Cyan
    Write-Host "    Source: $($response.sessions.source)" -ForegroundColor White
    Write-Host "    Web Client: $($response.sessions.web_client)" -ForegroundColor White
    Write-Host "    Web Service: $($response.sessions.web_service)" -ForegroundColor White
    Write-Host "    Background: $($response.sessions.background)" -ForegroundColor White
    Write-Host "    Total: $($response.sessions.total)" -ForegroundColor White

} catch {
    Write-Host "  ✗ Health Monitor not accessible: $healthUrl" -ForegroundColor Red
    Write-Host "    Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Diagnostic Complete!" -ForegroundColor Cyan
Write-Host ""
Write-Host "RECOMMENDED ACTIONS:" -ForegroundColor Yellow
Write-Host "1. Update appsettings.json with the correct database name from above" -ForegroundColor White
Write-Host "2. Ensure the database has the [Active Session] table" -ForegroundColor White
Write-Host "3. If SQL does not work, verify the API endpoint is accessible" -ForegroundColor White
Write-Host "4. If both fail, performance counters should work as fallback" -ForegroundColor White
Write-Host ""
