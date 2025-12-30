# Check Active Session table schema
Write-Host "Checking Active Session table schema in NMDALDEV..." -ForegroundColor Cyan
Write-Host ""

try {
    $conn = New-Object System.Data.SqlClient.SqlConnection
    $conn.ConnectionString = "Server=localhost;Database=NMDALDEV;Integrated Security=true;TrustServerCertificate=true"
    $conn.Open()

    # Get column info for Active Session table
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = @"
SELECT
    COLUMN_NAME,
    DATA_TYPE,
    CHARACTER_MAXIMUM_LENGTH,
    IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Active Session'
ORDER BY ORDINAL_POSITION
"@

    $reader = $cmd.ExecuteReader()

    Write-Host "Columns in [Active Session] table:" -ForegroundColor Yellow
    Write-Host ""
    while ($reader.Read()) {
        $colName = $reader[0]
        $dataType = $reader[1]
        $maxLen = if ($reader[2] -eq [DBNull]::Value) { "N/A" } else { $reader[2] }
        $nullable = $reader[3]
        Write-Host "  $colName : $dataType($maxLen) [$nullable]" -ForegroundColor White
    }
    $reader.Close()

    Write-Host ""
    Write-Host "Sample data from [Client Type] column:" -ForegroundColor Yellow
    Write-Host ""

    # Get sample values
    $cmd.CommandText = "SELECT DISTINCT [Client Type], SQL_VARIANT_PROPERTY([Client Type], 'BaseType') as BaseType FROM [dbo].[Active Session]"
    $reader = $cmd.ExecuteReader()

    while ($reader.Read()) {
        $value = $reader[0]
        $baseType = if ($reader[1] -eq [DBNull]::Value) { "NULL" } else { $reader[1] }
        Write-Host "  Value: $value (Type: $($value.GetType().Name), SQL BaseType: $baseType)" -ForegroundColor White
    }
    $reader.Close()

    $conn.Close()

    Write-Host ""
    Write-Host "Done" -ForegroundColor Green

} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
}
