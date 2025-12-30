# Diagnose SQL Permission Issues
# This script helps identify why the service account cannot access SQL

Write-Host "=== BCHealthMonitor SQL Permission Diagnostics ===" -ForegroundColor Cyan
Write-Host ""

# 1. Get service account
Write-Host "[1] Service Account Information:" -ForegroundColor Yellow
$service = Get-WmiObject -Class Win32_Service -Filter "Name='BCHealthMonitor'"
$serviceAccount = $service.StartName
Write-Host "  Service Name: $($service.Name)" -ForegroundColor White
Write-Host "  Service Account: $serviceAccount" -ForegroundColor White
Write-Host "  Service Status: $($service.State)" -ForegroundColor $(if ($service.State -eq 'Running') { 'Green' } else { 'Red' })
Write-Host ""

# 2. Get current user
Write-Host "[2] Current User Information:" -ForegroundColor Yellow
$currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
Write-Host "  Current User: $currentUser" -ForegroundColor White
Write-Host ""

# 3. Test SQL connection as current user
Write-Host "[3] Testing SQL Connection as Current User ($currentUser):" -ForegroundColor Yellow
$connString = "Server=localhost;Database=NMDALDEV;Integrated Security=true;TrustServerCertificate=true"
try {
    $conn = New-Object System.Data.SqlClient.SqlConnection($connString)
    $conn.Open()
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT COUNT(*) as SessionCount FROM [dbo].[Active Session]"
    $result = $cmd.ExecuteScalar()
    Write-Host "  ✓ SUCCESS: Connected to NMDALDEV" -ForegroundColor Green
    Write-Host "  ✓ Found $result active sessions" -ForegroundColor Green
    $conn.Close()
} catch {
    Write-Host "  ✗ FAILED: $($_.Exception.Message)" -ForegroundColor Red
}
Write-Host ""

# 4. Check if we can test as service account (requires admin)
Write-Host "[4] Testing SQL Connection as Service Account:" -ForegroundColor Yellow
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "  ⚠ SKIPPED: Requires administrator privileges to impersonate service account" -ForegroundColor Yellow
    Write-Host "  To test as service account, run this script as Administrator" -ForegroundColor Gray
} else {
    Write-Host "  Note: Testing as service account requires custom impersonation code" -ForegroundColor Gray
    Write-Host "  Alternative: Check SQL Server logs for authentication failures" -ForegroundColor Gray
}
Write-Host ""

# 5. Generate SQL permission grant script
Write-Host "[5] SQL Permission Grant Script:" -ForegroundColor Yellow
Write-Host "  Run this in SQL Server Management Studio to grant permissions:" -ForegroundColor White
Write-Host ""

$sqlScript = @"
-- Grant SQL permissions to service account
USE [NMDALDEV]
GO

-- Get the computer name for the login
DECLARE @computerName NVARCHAR(128) = CAST(SERVERPROPERTY('MachineName') AS NVARCHAR(128))
DECLARE @serviceAccount NVARCHAR(256) = @computerName + '\l_lscentral'

PRINT 'Granting permissions to: ' + @serviceAccount

-- Create login if doesn't exist
IF NOT EXISTS (SELECT * FROM sys.server_principals WHERE name = @serviceAccount)
BEGIN
    DECLARE @createLoginSQL NVARCHAR(MAX) = 'CREATE LOGIN [' + @serviceAccount + '] FROM WINDOWS'
    EXEC sp_executesql @createLoginSQL
    PRINT '✓ Created login for ' + @serviceAccount
END
ELSE
BEGIN
    PRINT '✓ Login already exists for ' + @serviceAccount
END
GO

-- Create user in database
USE [NMDALDEV]
GO

DECLARE @computerName NVARCHAR(128) = CAST(SERVERPROPERTY('MachineName') AS NVARCHAR(128))
DECLARE @serviceAccount NVARCHAR(256) = @computerName + '\l_lscentral'
DECLARE @userName NVARCHAR(128) = 'l_lscentral'

IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = @userName)
BEGIN
    DECLARE @createUserSQL NVARCHAR(MAX) = 'CREATE USER [' + @userName + '] FOR LOGIN [' + @serviceAccount + ']'
    EXEC sp_executesql @createUserSQL
    PRINT '✓ Created user ' + @userName
END
ELSE
BEGIN
    PRINT '✓ User already exists: ' + @userName
END
GO

-- Grant read permissions
DECLARE @userName NVARCHAR(128) = 'l_lscentral'
DECLARE @addMemberSQL NVARCHAR(MAX) = 'ALTER ROLE [db_datareader] ADD MEMBER [' + @userName + ']'
EXEC sp_executesql @addMemberSQL
PRINT '✓ Added to db_datareader role'

-- Specific permission for Active Session table
DECLARE @grantSelectSQL NVARCHAR(MAX) = 'GRANT SELECT ON [dbo].[Active Session] TO [' + @userName + ']'
EXEC sp_executesql @grantSelectSQL
PRINT '✓ Granted SELECT on [Active Session] table'

PRINT ''
PRINT '=== Permissions granted successfully ==='
PRINT 'Next step: Restart BCHealthMonitor service'
GO
"@

Write-Host $sqlScript -ForegroundColor Cyan
Write-Host ""

# 6. Save SQL script to file
$sqlScriptPath = "C:\work\LS Retail\BCHealthMonitor\scripts\grant-sql-permissions.sql"
$sqlScript | Out-File -FilePath $sqlScriptPath -Encoding UTF8
Write-Host "  SQL script saved to: $sqlScriptPath" -ForegroundColor Green
Write-Host ""

# 7. Next steps
Write-Host "=== Next Steps ===" -ForegroundColor Yellow
Write-Host ""
Write-Host "Option A - Grant SQL Permissions (Recommended):" -ForegroundColor White
Write-Host "  1. Open SQL Server Management Studio" -ForegroundColor Gray
Write-Host "  2. Connect to localhost" -ForegroundColor Gray
Write-Host "  3. Open: $sqlScriptPath" -ForegroundColor Gray
Write-Host "  4. Execute the script" -ForegroundColor Gray
Write-Host "  5. Restart service: Restart-Service BCHealthMonitor (as admin)" -ForegroundColor Gray
Write-Host ""

Write-Host "Option B - Change Service Account:" -ForegroundColor White
Write-Host "  1. Open services.msc" -ForegroundColor Gray
Write-Host "  2. Find 'BC Health Monitor'" -ForegroundColor Gray
Write-Host "  3. Right-click → Properties → Log On" -ForegroundColor Gray
Write-Host "  4. Change to 'Local System account'" -ForegroundColor Gray
Write-Host "  5. Restart the service" -ForegroundColor Gray
Write-Host ""

Write-Host "After fixing permissions:" -ForegroundColor White
Write-Host "  1. Restart service (requires admin):" -ForegroundColor Gray
Write-Host "     Restart-Service BCHealthMonitor" -ForegroundColor Gray
Write-Host "  2. Wait 5 seconds, then test:" -ForegroundColor Gray
Write-Host "     Invoke-RestMethod http://localhost:5080/health/details" -ForegroundColor Gray
Write-Host "  3. Check logs for debug output:" -ForegroundColor Gray
Write-Host "     Get-Content 'C:\Logs\BCHealthMonitor\*.log' -Tail 50" -ForegroundColor Gray
Write-Host ""
