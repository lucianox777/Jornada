[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$ConnectionString,
    [Parameter(Mandatory = $true)] [string]$MigrationRoot,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$TargetSchema = '3.70'
$ExpectedMigrations = 13
$FinalMigration = '20260910_Schema_Consolidation_370.sql'
$manifestPath = Join-Path $MigrationRoot 'manifest.txt'

function Get-MigrationInventory {
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Manifesto de migrações ausente: $manifestPath" }
    $entries = @(
        Get-Content -Encoding UTF8 -LiteralPath $manifestPath |
            ForEach-Object { ($_ -replace '#.*$','').Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($entries.Count -ne $ExpectedMigrations) { throw "Manifesto 3.70 deve conter exatamente $ExpectedMigrations migrações; encontrado $($entries.Count)." }
    if ($entries[-1] -ne $FinalMigration) { throw "Manifesto 3.70 deve terminar em $FinalMigration." }

    $inventory = @()
    foreach ($entry in $entries) {
        if ($entry -notmatch '^[A-Za-z0-9_.-]+\.sql$') { throw "Entrada inválida no manifesto: $entry" }
        $path = Join-Path $MigrationRoot $entry
        if (-not (Test-Path -LiteralPath $path)) { throw "Migração declarada não existe: $entry" }
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
        $inventory += [pscustomobject]@{ Name = $entry; Path = $path; Sha256 = $hash }
    }
    return $inventory
}

function Split-SqlBatches([string]$SqlText) {
    return @([Text.RegularExpressions.Regex]::Split($SqlText, '(?im)^\s*GO\s*(?:--.*)?$') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function New-OpenConnection {
    $connection = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
    $connection.Open()
    return $connection
}

function Invoke-Command($Connection, $Transaction, [string]$SqlText) {
    $command = $Connection.CreateCommand()
    try {
        $command.CommandTimeout = 0
        $command.Transaction = $Transaction
        $command.CommandText = $SqlText
        return $command.ExecuteNonQuery()
    }
    finally { $command.Dispose() }
}

function Invoke-Scalar($Connection, $Transaction, [string]$SqlText) {
    $command = $Connection.CreateCommand()
    try {
        $command.CommandTimeout = 0
        $command.Transaction = $Transaction
        $command.CommandText = $SqlText
        return $command.ExecuteScalar()
    }
    finally { $command.Dispose() }
}

function Acquire-LedgerLock($Connection, $Transaction) {
    $command = $Connection.CreateCommand()
    try {
        $command.CommandTimeout = 70
        $command.Transaction = $Transaction
        $command.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'Jornada.SchemaMigrationLedger',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=60000; SELECT @r;"
        $result = [int]$command.ExecuteScalar()
        if ($result -lt 0) { throw "Não foi possível adquirir lock do ledger de migrações. sp_getapplock=$result" }
    }
    finally { $command.Dispose() }
}

function Ensure-Ledger {
    $connection = New-OpenConnection
    $transaction = $connection.BeginTransaction([Data.IsolationLevel]::Serializable)
    try {
        Acquire-LedgerLock $connection $transaction
        $sql = @"
IF SCHEMA_ID(N'jornada') IS NULL EXEC(N'CREATE SCHEMA jornada');
IF OBJECT_ID(N'jornada.schema_migration',N'U') IS NULL
BEGIN
    CREATE TABLE jornada.schema_migration(
        migration_name nvarchar(260) NOT NULL PRIMARY KEY,
        sha256 char(64) NOT NULL,
        applied_at datetime2(3) NOT NULL CONSTRAINT DF_jornada_schema_migration_applied_at DEFAULT SYSUTCDATETIME()
    );
END;
"@
        $null = Invoke-Command $connection $transaction $sql
        $transaction.Commit()
    }
    catch {
        try { $transaction.Rollback() } catch { }
        throw
    }
    finally {
        $transaction.Dispose()
        $connection.Dispose()
    }
}

function Apply-Migration($Migration) {
    $connection = New-OpenConnection
    $transaction = $connection.BeginTransaction([Data.IsolationLevel]::Serializable)
    try {
        Acquire-LedgerLock $connection $transaction
        $escapedName = $Migration.Name.Replace("'", "''")
        $existing = Invoke-Scalar $connection $transaction "SELECT sha256 FROM jornada.schema_migration WITH (UPDLOCK,HOLDLOCK) WHERE migration_name=N'$escapedName';"
        if ($null -ne $existing -and $existing -ne [DBNull]::Value) {
            if (-not [string]::Equals(([string]$existing).Trim(), $Migration.Sha256, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Checksum divergente para migração já aplicada: $($Migration.Name)"
            }
            $transaction.Commit()
            Write-Host "OK já aplicada: $($Migration.Name)"
            return
        }

        $sql = Get-Content -Raw -Encoding UTF8 -LiteralPath $Migration.Path
        foreach ($batch in Split-SqlBatches $sql) { $null = Invoke-Command $connection $transaction $batch }
        $null = Invoke-Command $connection $transaction "INSERT INTO jornada.schema_migration(migration_name,sha256) VALUES(N'$escapedName','$($Migration.Sha256)');"
        $transaction.Commit()
        Write-Host "Aplicada: $($Migration.Name)"
    }
    catch {
        try { $transaction.Rollback() } catch { }
        throw
    }
    finally {
        $transaction.Dispose()
        $connection.Dispose()
    }
}

$inventory = @(Get-MigrationInventory)
if ($ValidateOnly) {
    Write-Host "Migration manifest: OK ($($inventory.Count)/$ExpectedMigrations; final=$FinalMigration)"
    exit 0
}

$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($ConnectionString)
if ([string]::IsNullOrWhiteSpace($builder.InitialCatalog)) { throw 'connectionString deve conter Database/Initial Catalog.' }

Ensure-Ledger
foreach ($migration in $inventory) { Apply-Migration $migration }

$connection = New-OpenConnection
try {
    $ledgerCount = [int](Invoke-Scalar $connection $null 'SELECT COUNT(*) FROM jornada.schema_migration;')
    if ($ledgerCount -lt $ExpectedMigrations) { throw "Ledger incompleto: $ledgerCount/$ExpectedMigrations" }
    $schema = [string](Invoke-Scalar $connection $null "SELECT CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema'));" )
    if (-not [string]::Equals($schema.Trim(), $TargetSchema, [StringComparison]::Ordinal)) { throw "Jornada.SolutionSchema inválido após migrações: '$schema' (esperado $TargetSchema)." }
}
finally { $connection.Dispose() }

Write-Host "Migration ledger OK: $ExpectedMigrations/$ExpectedMigrations; Jornada.SolutionSchema=$TargetSchema"
