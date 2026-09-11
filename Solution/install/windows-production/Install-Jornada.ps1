[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$ConfigPath,
    [Parameter(Mandatory = $true)] [string]$PayloadRoot,
    [switch]$ValidateOnly,
    [switch]$PlanOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Copy-ConfigObject($Value) {
    return ($Value | ConvertTo-Json -Depth 50 | ConvertFrom-Json)
}

function Write-TemporaryConfig($Value, [string]$Prefix) {
    $path = Join-Path ([IO.Path]::GetTempPath()) ("{0}-{1}.json" -f $Prefix,[Guid]::NewGuid().ToString('N'))
    $Value | ConvertTo-Json -Depth 50 | Set-Content -Encoding UTF8 -LiteralPath $path
    return $path
}

function Invoke-SqlScalar([string]$ConnectionString, [string]$SqlText) {
    $connection = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        try {
            $command.CommandTimeout = 30
            $command.CommandText = $SqlText
            return $command.ExecuteScalar()
        }
        finally { $command.Dispose() }
    }
    finally { $connection.Dispose() }
}

function Invoke-SqlBatches([string]$ConnectionString, [string]$Path) {
    $sql = Get-Content -Raw -Encoding UTF8 -LiteralPath $Path
    $connection = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
    try {
        $connection.Open()
        foreach ($batch in [Text.RegularExpressions.Regex]::Split($sql, '(?im)^\s*GO\s*(?:--.*)?$')) {
            if ([string]::IsNullOrWhiteSpace($batch)) { continue }
            $command = $connection.CreateCommand()
            try {
                $command.CommandTimeout = 0
                $command.CommandText = $batch
                $null = $command.ExecuteNonQuery()
            }
            finally { $command.Dispose() }
        }
    }
    finally { $connection.Dispose() }
}

function Test-TargetConnection([string]$ConnectionString) {
    $connection = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
    try {
        $connection.Open()
        return $true
    }
    catch { return $false }
    finally { $connection.Dispose() }
}

function Ensure-TargetDatabase($SqlConfig) {
    $target = New-Object System.Data.SqlClient.SqlConnectionStringBuilder([string]$SqlConfig.connectionString)
    $database = $target.InitialCatalog
    if ([string]::IsNullOrWhiteSpace($database)) { throw 'connectionString deve conter Database/Initial Catalog.' }
    if (Test-TargetConnection $target.ConnectionString) { return }
    if ([string]$SqlConfig.mode -eq 'External') {
        throw 'sql.mode=External exige banco de destino previamente provisionado; a conexão configurada não abriu o banco.'
    }

    $master = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($target.ConnectionString)
    $master.InitialCatalog = 'master'
    $escapedName = $database.Replace(']', ']]')
    $escapedLiteral = $database.Replace("'", "''")
    $connection = New-Object System.Data.SqlClient.SqlConnection($master.ConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        try {
            $command.CommandTimeout = 0
            $command.CommandText = "IF DB_ID(N'$escapedLiteral') IS NULL CREATE DATABASE [$escapedName];"
            $null = $command.ExecuteNonQuery()
        }
        finally { $command.Dispose() }
    }
    finally { $connection.Dispose() }

    if (-not (Test-TargetConnection $target.ConnectionString)) { throw "Banco $database não ficou acessível após provisionamento." }
}

function Get-DatabaseInitializationState([string]$ConnectionString) {
    $sql = @"
SELECT CONCAT(
  CASE WHEN OBJECT_ID(N'ingestao.entrega',N'U') IS NULL THEN '0' ELSE '1' END, '|',
  CASE WHEN EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema') THEN '1' ELSE '0' END, '|',
  CASE WHEN OBJECT_ID(N'jornada.schema_migration',N'U') IS NULL THEN '0' ELSE '1' END
);
"@
    $parts = ([string](Invoke-SqlScalar $ConnectionString $sql)).Split('|')
    if ($parts.Count -ne 3) { throw 'Não foi possível classificar o estado do banco Jornada.' }
    $hasBaseline = $parts[0] -eq '1'
    $hasSchemaMarker = $parts[1] -eq '1'
    $hasLedger = $parts[2] -eq '1'
    if (-not $hasBaseline -and -not $hasSchemaMarker -and -not $hasLedger) { return 'EMPTY' }
    if ($hasBaseline) { return 'INITIALIZED' }
    return 'PARTIAL'
}

function Invoke-CoreInstaller([string]$CoreInstaller, [string]$Config, [string]$Payload, [switch]$CoreValidateOnly, [switch]$CorePlanOnly) {
    $args = @('-ConfigPath',$Config,'-PayloadRoot',$Payload)
    if ($CoreValidateOnly) { $args += '-ValidateOnly' }
    if ($CorePlanOnly) { $args += '-PlanOnly' }
    & $CoreInstaller @args
    if ($LASTEXITCODE -ne 0) { throw "Instalador core falhou. ExitCode=$LASTEXITCODE" }
}

$configFull = (Resolve-Path -LiteralPath $ConfigPath).Path
$payloadFull = (Resolve-Path -LiteralPath $PayloadRoot).Path
$config = Get-Content -Raw -Encoding UTF8 $configFull | ConvertFrom-Json
$coreInstaller = Join-Path $PSScriptRoot 'Install-JornadaProduction.ps1'
$migrationRunner = Join-Path $PSScriptRoot 'Invoke-JornadaMigrationLedger.ps1'
$migrationRoot = Join-Path $payloadFull 'database\migrations'
$baselinePath = Join-Path $payloadFull 'database\Jornada_Fase1.sql'

if (-not (Test-Path -LiteralPath $coreInstaller)) { throw "Instalador core ausente: $coreInstaller" }
if (-not (Test-Path -LiteralPath $migrationRunner)) { throw "Runner de migrações ausente: $migrationRunner" }
if (-not (Test-Path -LiteralPath $baselinePath)) { throw "DDL baseline ausente: $baselinePath" }

$initializeDatabase = [bool]$config.sql.initializeDatabase
if ($initializeDatabase) {
    & $migrationRunner -ConnectionString ([string]$config.sql.connectionString) -MigrationRoot $migrationRoot -ValidateOnly
    if ($LASTEXITCODE -ne 0) { throw 'Manifesto/runner de migrações inválido.' }
}

if ($ValidateOnly -or $PlanOnly) {
    Invoke-CoreInstaller $coreInstaller $configFull $payloadFull -CoreValidateOnly:$ValidateOnly -CorePlanOnly:$PlanOnly
    exit 0
}

if (-not $initializeDatabase) {
    Invoke-CoreInstaller $coreInstaller $configFull $payloadFull
    exit 0
}

$bootstrapPath = $null
$finalPath = $null
try {
    # Bootstrap instala/valida runtimes, Docker/SQL e payload, mas não altera o
    # schema nem registra tarefas. Assim uma falha de migração não publica um novo
    # conjunto de processos antes do banco estar fechado no manifesto normativo.
    $bootstrap = Copy-ConfigObject $config
    $bootstrap.sql.initializeDatabase = $false
    foreach ($task in @($bootstrap.tasks)) { $task.enabled = $false }
    $bootstrapPath = Write-TemporaryConfig $bootstrap 'jornada-bootstrap'
    Invoke-CoreInstaller $coreInstaller $bootstrapPath $payloadFull

    Ensure-TargetDatabase $config.sql
    $state = Get-DatabaseInitializationState ([string]$config.sql.connectionString)
    switch ($state) {
        'EMPTY' {
            Write-Host 'Banco sem baseline detectado; aplicando Jornada_Fase1.sql antes do manifesto.'
            Invoke-SqlBatches ([string]$config.sql.connectionString) $baselinePath
        }
        'INITIALIZED' { Write-Host 'Banco existente detectado; baseline não será reaplicado.' }
        'PARTIAL' { throw 'Banco parcialmente inicializado: marker/ledger existe sem ingestao.entrega. Corrija ou restaure o banco antes do upgrade.' }
        default { throw "Estado de banco inesperado: $state" }
    }

    & $migrationRunner -ConnectionString ([string]$config.sql.connectionString) -MigrationRoot $migrationRoot
    if ($LASTEXITCODE -ne 0) { throw "Aplicação do ledger de migrações falhou. ExitCode=$LASTEXITCODE" }

    # Finalização não reinstala mídia SQL/Moby e não reaplica DDL. Ela registra
    # somente o estado operacional original depois que schema+ledger estão válidos.
    $final = Copy-ConfigObject $config
    $final.sql.initializeDatabase = $false
    if ([string]$final.sql.mode -eq 'InstallFromMedia') { $final.sql.mode = 'Existing' }
    if ([string]$final.docker.mode -eq 'MobyWindows') { $final.docker.mode = 'Existing' }
    $finalPath = Write-TemporaryConfig $final 'jornada-finalize'
    Invoke-CoreInstaller $coreInstaller $finalPath $payloadFull
}
finally {
    if ($bootstrapPath) { Remove-Item -Force -LiteralPath $bootstrapPath -ErrorAction SilentlyContinue }
    if ($finalPath) { Remove-Item -Force -LiteralPath $finalPath -ErrorAction SilentlyContinue }
}

Write-Host 'Instalação/upgrade com ledger de migrações: OK'
