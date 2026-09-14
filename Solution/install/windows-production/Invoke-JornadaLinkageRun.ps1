[CmdletBinding()]
param(
    [ValidateSet('INCREMENTAL','FULL','REPLAY','MODEL_VALIDATION')]
    [string]$Mode = 'INCREMENTAL',
    [bool]$Publish = $true,
    [string]$RequestedBy = 'MANUAL_VM',
    [string]$Reason = 'manual-vm-run',
    [string]$RuntimeConfig = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$installRoot = (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($RuntimeConfig)) {
    $RuntimeConfig = Join-Path $installRoot 'config\manual-runtime.json'
}
$configPath = (Resolve-Path -LiteralPath $RuntimeConfig).Path
$config = Get-Content -Raw -Encoding UTF8 $configPath | ConvertFrom-Json
foreach ($property in $config.environment.PSObject.Properties) {
    [Environment]::SetEnvironmentVariable($property.Name, [string]$property.Value, 'Process')
}

$connectionString = [string]$config.environment.ConnectionStrings__Jornada
$executable = [string]$config.executables.linkageRunner
if ([string]::IsNullOrWhiteSpace($connectionString)) { throw 'ConnectionStrings__Jornada ausente em manual-runtime.json.' }
if (-not (Test-Path -LiteralPath $executable)) { throw "Linkage Runner não encontrado: $executable" }

$connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
try {
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 30
    $command.CommandText = """
        SELECT COUNT(*)
        FROM identidade.modelo_linkage
        WHERE status='ATIVO'
          AND ISNULL(amostra_metodo,'') <> 'SEED_DEV_FIXO_NAO_TREINADO';
        """
    $calibratedActiveCount = [int]$command.ExecuteScalar()
    if ($calibratedActiveCount -ne 1) {
        throw "Linkage bloqueado: é necessário exatamente um modelo calibrado ATIVO; encontrados=$calibratedActiveCount. O modelo seed sintético não libera execução. Execute primeiro jobs\\Invoke-JornadaLinkageCalibration.ps1."
    }
    $command.CommandText = """
        SELECT TOP(1) versao
        FROM identidade.modelo_linkage
        WHERE status='ATIVO'
          AND ISNULL(amostra_metodo,'') <> 'SEED_DEV_FIXO_NAO_TREINADO'
        ORDER BY versao DESC;
        """
    $activeVersion = [int]$command.ExecuteScalar()
}
finally {
    if ($connection.State -ne [Data.ConnectionState]::Closed) { $connection.Close() }
    $connection.Dispose()
}

$publishText = $Publish.ToString().ToLowerInvariant()
Write-Host "Linkage liberado pelo modelo calibrado ATIVO v$activeVersion. Mode=$Mode Publish=$publishText"
& $executable '--mode' $Mode '--publish' $publishText '--requested-by' $RequestedBy '--reason' $Reason
if ($LASTEXITCODE -ne 0) { throw "Linkage Runner falhou. ExitCode=$LASTEXITCODE" }
