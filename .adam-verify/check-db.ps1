Add-Type -Path "src/Adam.BrokerService/bin/Debug/net10.0/Microsoft.Data.Sqlite.dll"
$conn = New-Object Microsoft.Data.Sqlite.SqliteConnection("Data Source=catalog.db")
$conn.Open()

Write-Host "=== TABLES ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"
$rdr = $cmd.ExecuteReader()
while ($rdr.Read()) { Write-Host "  $($rdr.GetString(0))" }
$rdr.Close()

Write-Host "=== ROLES ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT Id, Name FROM Roles"
$rdr = $cmd.ExecuteReader()
while ($rdr.Read()) { Write-Host "  $($rdr.GetString(0)) | $($rdr.GetString(1))" }
$rdr.Close()

Write-Host "=== USERS ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT Id, Username, Email, RoleId, IsActive FROM Users"
$rdr = $cmd.ExecuteReader()
while ($rdr.Read()) {
    $active = $rdr.GetBoolean(4)
    Write-Host "  $($rdr.GetString(0)) | $($rdr.GetString(1)) | $($rdr.GetString(2)) | Role: $($rdr.GetString(3)) | Active: $active"
}
$rdr.Close()

Write-Host "=== COLLECTIONS ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT Count(*) FROM Collections"
$count = [int]$cmd.ExecuteScalar()
Write-Host "  $count collections"

Write-Host "=== DIGITAL ASSETS ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT Count(*) FROM DigitalAssets"
$count = [int]$cmd.ExecuteScalar()
Write-Host "  $count digital assets"

$conn.Close()
Write-Host "=== DONE ==="
