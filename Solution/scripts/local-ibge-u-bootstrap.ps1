param(
    [ValidateRange(10000, 5000000)]
    [int]$PairCount = 250000,

    [int]$Seed = 20260917
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'
$Cluster = Join-Path $PSScriptRoot 'local-cluster.ps1'
$OutDir = Join-Path $Root '.local\calibrador-ibge-u'
$ReportPath = Join-Path $OutDir 'ibge-u-bootstrap.json'
$ContainerReport = '/tmp/jornada-ibge-u-bootstrap.json'

function Format-CommandArgument {
    param([Parameter(Mandatory=$true)][AllowEmptyString()][string]$Value)

    if ($Value -notmatch '[\s''"$&|<>]') { return $Value }
    return "'" + $Value.Replace("'", "''") + "'"
}

function Write-CommandLine {
    param(
        [Parameter(Mandatory=$true)][string]$Executable,
        [string[]]$Arguments = @()
    )

    $tokens = @((Format-CommandArgument $Executable))
    $tokens += @($Arguments | ForEach-Object { Format-CommandArgument ([string]$_) })
    Write-Host ("# " + ($tokens -join ' ')) -ForegroundColor DarkGray
}

function Assert-ExitCode {
    param([Parameter(Mandatory=$true)][string]$Label)

    if ($LASTEXITCODE -ne 0) {
        throw "$Label falhou ($LASTEXITCODE)."
    }
}

# Set-Location <Solution>
Write-Host "# Set-Location '$Root'" -ForegroundColor DarkGray
Set-Location -LiteralPath $Root

# .\scripts\local-cluster.ps1 -Action up
Write-Host '# .\scripts\local-cluster.ps1 -Action up' -ForegroundColor DarkGray
& $Cluster -Action up
Assert-ExitCode '.\scripts\local-cluster.ps1 -Action up'

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$workerArgs = @(
    'compose','--env-file',$EnvFile,'exec','-T','jornada-node2',
    'env',
    'LinkageParameters__Operation=REPORT_IBGE_U_BOOTSTRAP',
    "LinkageParameters__IbgeUBootstrap__Seed=$Seed",
    "LinkageParameters__IbgeUBootstrap__PairCount=$PairCount",
    "LinkageParameters__IbgeUBootstrap__OutputPath=$ContainerReport",
    'dotnet',
    '/opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll'
)

# docker compose --env-file .env exec -T jornada-node2 env LinkageParameters__Operation=REPORT_IBGE_U_BOOTSTRAP ... dotnet /opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll
Write-CommandLine 'docker' $workerArgs
& docker @workerArgs
Assert-ExitCode 'REPORT_IBGE_U_BOOTSTRAP'

$copyArgs = @(
    'compose','--env-file',$EnvFile,'cp',
    "jornada-node2:$ContainerReport",
    $ReportPath
)

# docker compose --env-file .env cp jornada-node2:/tmp/jornada-ibge-u-bootstrap.json .local/calibrador-ibge-u/ibge-u-bootstrap.json
Write-CommandLine 'docker' $copyArgs
& docker @copyArgs
Assert-ExitCode 'docker compose cp relatório IBGE u'

$report = Get-Content -Raw -Encoding UTF8 $ReportPath | ConvertFrom-Json

Write-Host ''
Write-Host '=== BOOTSTRAP POPULACIONAL IBGE DE u NOMINAL ==='
Write-Host "Referência: $($report.reference.code) / SHA=$($report.reference.contentSha256)"
Write-Host "Método: $($report.methodology.methodVersion)"
Write-Host "Construção: $($report.methodology.jointConstructionVersion)"
Write-Host "Canal de observação: $($report.methodology.observationChannelVersion)"
Write-Host "Seed=$($report.sampling.seed); pares=$($report.sampling.pairCount)"
Write-Host "P(EXACT) analítico sintético=$($report.analytic.exactSyntheticFullNameProbability)"

foreach ($stateName in @('EXACT','HIGH','MEDIUM','LOW')) {
    $state = $report.states.$stateName
    if ($null -eq $state) { continue }

    $activeText = if ($null -eq $state.activeModelProbability) {
        'modelo_ativo=n/a'
    }
    else {
        "modelo_ativo=$($state.activeModelProbability); delta=$($state.deltaBootstrapMinusActive)"
    }

    Write-Host "$($stateName): ibge=$($state.ibgeBootstrapProbability); suporte=$($state.ibgeBootstrapSupport); se=$($state.ibgeBootstrapStandardError); $activeText"
}

if ($null -eq $report.activeModel) {
    Write-Host 'Nenhum modelo calibrado ATIVO: relatório IBGE foi produzido sem comparação operacional.' -ForegroundColor DarkYellow
}
else {
    Write-Host "Comparação: modelo ATIVO v$($report.activeModel.version) / $($report.activeModel.modelId) / amostra=$($report.activeModel.sampleMethod)"
}

Write-Host ''
Write-Host 'Este relatório é read-only e NÃO ativa nem altera o modelo.' -ForegroundColor Yellow
Write-Host "Relatório: $ReportPath"
Write-Host 'IBGE NOMINAL U BOOTSTRAP REPORT: OK' -ForegroundColor Green
