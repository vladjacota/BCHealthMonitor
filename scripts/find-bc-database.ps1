# Find BC Database
$conn = New-Object System.Data.SqlClient.SqlConnection
$conn.ConnectionString = "Server=localhost;Database=master;Integrated Security=true;TrustServerCertificate=true"
$conn.Open()

$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT name FROM sys.databases WHERE name NOT IN ('master', 'model', 'msdb', 'tempdb') ORDER BY name"
$reader = $cmd.ExecuteReader()

Write-Host "Databases on this SQL Server:" -ForegroundColor Cyan
while ($reader.Read()) {
    Write-Host "  - $($reader.GetString(0))" -ForegroundColor White
}

$reader.Close()
$conn.Close()
