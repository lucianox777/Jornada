[CmdletBinding()]
param(
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
$executable = [string]$config.executables.linkageParameters
$conferenceExecutable = [string]$config.executables.linkageConference
$conferenceTolerance = [string]$config.environment.LinkageParameters__ConferenceToleranceConfigPath
if ([string]::IsNullOrWhiteSpace($connectionString)) { throw 'ConnectionStrings__Jornada ausente em manual-runtime.json.' }
if (-not (Test-Path -LiteralPath $executable)) { throw "Linkage Parameters não encontrado: $executable" }
if (-not (Test-Path -LiteralPath $conferenceExecutable)) { throw "Linkage Conference não encontrado: $conferenceExecutable" }
if (-not (Test-Path -LiteralPath $conferenceTolerance)) { throw "Configuração de tolerância da conferência não encontrada: $conferenceTolerance" }

function Invoke-Scalar([string]$Sql) {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 30
        $command.CommandText = $Sql
        return $command.ExecuteScalar()
    }
    finally {
        if ($connection.State -ne [Data.ConnectionState]::Closed) { $connection.Close() }
        $connection.Dispose()
    }
}

function Invoke-Parameters([string]$Operation, [Nullable[int]]$TargetVersion = $null) {
    [Environment]::SetEnvironmentVariable('LinkageParameters__Operation', $Operation, 'Process')
    [Environment]::SetEnvironmentVariable('LinkageParameters__RunOnce', 'true', 'Process')
    [Environment]::SetEnvironmentVariable(
        'LinkageParameters__TargetVersion',
        $(if ($null -eq $TargetVersion) { $null } else { $TargetVersion.Value.ToString([Globalization.CultureInfo]::InvariantCulture) }),
        'Process')

    Write-Host "Linkage Parameters: $Operation$(if ($null -ne $TargetVersion) { " v$($TargetVersion.Value)" } else { '' })"
    & $executable
    if ($LASTEXITCODE -ne 0) { throw "Linkage Parameters falhou em $Operation. ExitCode=$LASTEXITCODE" }
}

# A referência IBGE é bootstrap do ambiente. ENSURE retorna imediatamente quando o
# snapshot canônico já está materializado e só executa a carga completa em banco novo.
# Isso evita reler milhões de linhas a cada recalibração, mantendo fallback fail-closed.
Invoke-Parameters 'ENSURE_NAME_FREQUENCY_SNAPSHOT'
$activeReference = [int](Invoke-Scalar "SELECT COUNT(*) FROM ref.frequencia_nome_versao WHERE status='ATIVA' AND conteudo_sha256 IS NOT NULL;")
if ($activeReference -ne 1) { throw "Calibração exige exatamente uma referência de frequências ATIVA; encontradas=$activeReference." }

$before = [int](Invoke-Scalar "SELECT ISNULL(MAX(versao),0) FROM identidade.modelo_linkage;")
Invoke-Parameters 'GENERATE_DRAFT'

$count = [int](Invoke-Scalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE versao > $before AND status='RASCUNHO';")
if ($count -ne 1) {
    throw "Calibração esperava exatamente um novo RASCUNHO após v$before; encontrados=$count. Nenhum modelo será ativado automaticamente."
}
$version = [int](Invoke-Scalar "SELECT MAX(versao) FROM identidade.modelo_linkage WHERE versao > $before AND status='RASCUNHO';")
$modelId = [string](Invoke-Scalar "SELECT CONVERT(varchar(36),modelo_id) FROM identidade.modelo_linkage WHERE versao=$version;")
if ([string]::IsNullOrWhiteSpace($modelId)) { throw "modelo_id ausente para v$version." }

$sourceRevision = "WINDOWS_MANUAL_" + [string]$config.configurationBundleVersion
Write-Host "Linkage Conference: modelo v$version / $modelId"
& $conferenceExecutable --model-id $modelId --tolerance-config $conferenceTolerance --source-revision $sourceRevision
if ($LASTEXITCODE -ne 0) {
    throw "Linkage Conference bloqueou a promoção. ExitCode=$LASTEXITCODE. Verifique a tolerância governada e a evidência CONFORME."
}

Invoke-Parameters 'VALIDATE' $version
Invoke-Parameters 'ACTIVATE' $version

$active = [int](Invoke-Scalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE versao=$version AND status='ATIVO';")
if ($active -ne 1) { throw "Modelo v$version não ficou ATIVO ao fim da calibração." }

Write-Host "CALIBRAÇÃO CONCLUÍDA: modelo v$version ATIVO. O Linkage Runner está liberado."
