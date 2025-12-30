# Find which database has Active Session table
$databases = @("CLDEV", "Commerce", "GoCurrent", "JNS", "JNS-NEWBAK", "KitchenService", "LSC-43064", "LSCommerce", "LSOmni", "MILL", "NAV2017CU9_AXSYS_PROD", "NAV71", "NMDALDEV", "NMD-AL-DEV", "NMD-CAL", "POS", "SAFT", "Scandlines2018", "Storeserver", "UpdateService", "WebMonitorDB", "Woody OBJ")

Write-Host "Searching for [Active Session] table..." -ForegroundColor Cyan
Write-Host ""

foreach ($db in $databases) {
    try {
        $conn = New-Object System.Data.SqlClient.SqlConnection
        $conn.ConnectionString = "Server=localhost;Database=$db;Integrated Security=true;TrustServerCertificate=true"
        $conn.Open()

        $cmd = $conn.CreateCommand()
        $cmd.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Active Session'"
        $exists = $cmd.ExecuteScalar()

        if ($exists -gt 0) {
            Write-Host "  ✓ Database '$db' has [Active Session] table" -ForegroundColor Green

            # Check session count
            $cmd.CommandText = "SELECT COUNT(*) FROM [dbo].[Active Session]"
            $sessionCount = $cmd.ExecuteScalar()
            Write-Host "     Sessions: $sessionCount" -ForegroundColor White

            if ($sessionCount -gt 0) {
                $cmd.CommandText = "SELECT [Client Type], COUNT(*) as cnt FROM [dbo].[Active Session] GROUP BY [Client Type]"
                $reader = $cmd.ExecuteReader()
                while ($reader.Read()) {
                    Write-Host "      - $($reader.GetString(0)): $($reader.GetInt32(1))" -ForegroundColor Yellow
                }
                $reader.Close()
            }
        }

        $conn.Close()
    } catch {
        # Ignore errors for databases we can't access
    }
}

Write-Host ""
Write-Host "Done" -ForegroundColor Cyan
