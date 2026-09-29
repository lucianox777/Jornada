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

# O bootstrap IBGE é uma etapa única de preparação do ambiente. A geração abaixo
# valida os derivados imutáveis já persistidos e falha fechado se o primeiro bootstrap
# não existir; este wrapper não carrega, reativa nem recalcula a referência IBGE.

# DT-15: a geração não autoriza promoção. Congelar a identidade do ATIVO-base
# para avisar ao operador quando outra calibração/promocao alterar a base.
$activeBeforeCount = [int](Invoke-Scalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE status='ATIVO';")
if ($activeBeforeCount -gt 1) { throw "DT-15: múltiplos modelos ATIVOS antes da geração; revisão interrompida." }
$activeBeforeId = if ($activeBeforeCount -eq 1) {
    [string](Invoke-Scalar "SELECT CONVERT(varchar(36),modelo_id) FROM identidade.modelo_linkage WHERE status='ATIVO';")
} else { '' }
$activeBeforeVersion = if ($activeBeforeCount -eq 1) {
    [string](Invoke-Scalar "SELECT CONVERT(varchar(20),versao) FROM identidade.modelo_linkage WHERE status='ATIVO';")
} else { 'SEM_ATIVO' }

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

# A conferência técnica é evidência do modelo, NÃO a decisão do operador master.
# Nenhuma opção deste wrapper executa VALIDATE ou ACTIVATE até que os gates
# verificáveis de aprovação humana, o dossiê pareado e a página master existam.
$activeAfterCount = [int](Invoke-Scalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE status='ATIVO';")
$activeAfterId = if ($activeAfterCount -eq 1) {
    [string](Invoke-Scalar "SELECT CONVERT(varchar(36),modelo_id) FROM identidade.modelo_linkage WHERE status='ATIVO';")
} else { '' }
if ($activeAfterCount -ne $activeBeforeCount -or $activeAfterId -ne $activeBeforeId) {
    throw "DT-15: o modelo ATIVO mudou durante a geração/conferência. RASCUNHO v$version preservado; revisão da base obrigatória."
}
if ([int](Invoke-Scalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE modelo_id='$modelId' AND status='RASCUNHO';") -ne 1) {
    throw "DT-15: RASCUNHO v$version mudou de estado durante a conferência; nenhuma promoção será realizada pelo wrapper."
}
Write-Host "DT-15: modelo RASCUNHO v$version / $modelId preparado e conferido."
Write-Host "Modelo ATIVO-base: v$activeBeforeVersion / $(if ($activeBeforeId) { $activeBeforeId } else { 'SEM_ATIVO' })."
Write-Host "VALIDATE e ACTIVATE NÃO foram executados; o ATIVO permanece inalterado."
Write-Host "Próxima etapa: obter comparação pareada e decisão verificável do operador master (DT-15)."
Write-Host "A aprovação institucional e os gates do Parameters Worker também devem proteger invocações diretas."
