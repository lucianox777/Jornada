param(
    [ValidateRange(10000, 5000000)]
    [int]$PairCount = 1000000,

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

Write-Host "# Set-Location '$Root'" -ForegroundColor DarkGray
Set-Location -LiteralPath $Root

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

Write-CommandLine 'docker' $workerArgs
& docker @workerArgs
Assert-ExitCode 'REPORT_IBGE_U_BOOTSTRAP'

$copyArgs = @(
    'compose','--env-file',$EnvFile,'cp',
    "jornada-node2:$ContainerReport",
    $ReportPath
)

Write-CommandLine 'docker' $copyArgs
& docker @copyArgs
Assert-ExitCode 'docker compose cp relatório IBGE u'

$report = Get-Content -Raw -Encoding UTF8 $ReportPath | ConvertFrom-Json

Write-Host ''
Write-Host '=== MONTE CARLO IBGE DE u NOMINAL ==='
Write-Host "Referência: $($report.reference.code) / SHA=$($report.reference.contentSha256)"
Write-Host "Pessoa: prenome=$($report.methodology.personFirstNameSex); sobrenome=$($report.methodology.surnameSex)"
Write-Host "Mãe:    prenome=$($report.methodology.motherFirstNameSex); sobrenome=$($report.methodology.surnameSex)"
Write-Host "Método: $($report.methodology.methodVersion)"
Write-Host "Construção: $($report.methodology.jointConstructionVersion)"
Write-Host "Canal de observação: $($report.methodology.observationChannelVersion)"
Write-Host ''

Write-Host '--- NOME DA PESSOA ---'
Write-Host "Seed=$($report.personName.sampling.seed); pares=$($report.personName.sampling.pairCount)"
Write-Host "P(EXACT) analítico sintético=$($report.personName.analytic.exactSyntheticFullNameProbability)"
foreach ($stateName in @('EXACT','HIGH','MEDIUM','LOW')) {
    $state = $report.personName.states.$stateName
    if ($null -eq $state) { continue }
    $activeText = if ($null -eq $state.activeModelProbability) {
        'modelo_ativo=n/a'
    }
    else {
        "modelo_ativo=$($state.activeModelProbability); suporte_modelo_mc=$($state.activeModelMonteCarloSupport)"
    }
    Write-Host "$($stateName): mc=$($state.monteCarloProbability); suporte=$($state.monteCarloSupport); se=$($state.monteCarloStandardError); $activeText"
}

Write-Host ''
Write-Host '--- NOME DA MÃE ---'
Write-Host "Seed=$($report.motherName.sampling.seed); pares=$($report.motherName.sampling.pairCount); massa_presente_modelo=$($report.motherName.activeModelPresentMass)"
Write-Host "P(EXACT) analítico sintético condicional à presença=$($report.motherName.analytic.exactSyntheticFullNameProbability)"
foreach ($stateName in @('EXACT','HIGH','MEDIUM','LOW')) {
    $state = $report.motherName.states.$stateName
    if ($null -eq $state) { continue }
    $activeText = if ($null -eq $state.activeModelProbabilityGivenPresent) {
        'modelo_ativo_cond_presenca=n/a'
    }
    else {
        "modelo_ativo_cond_presenca=$($state.activeModelProbabilityGivenPresent); conjunto=$($state.activeModelJointProbability); suporte_modelo_mc=$($state.activeModelMonteCarloSupport)"
    }
    Write-Host "$($stateName): mc_cond_presenca=$($state.monteCarloProbabilityGivenPresent); suporte=$($state.monteCarloSupport); se=$($state.monteCarloStandardError); $activeText"
}

if ($null -eq $report.activeModel) {
    Write-Host 'Nenhum modelo calibrado ATIVO: relatório IBGE foi produzido sem comparação operacional.' -ForegroundColor DarkYellow
}
else {
    Write-Host ''
    Write-Host "Modelo ATIVO: v$($report.activeModel.version) / $($report.activeModel.modelId) / amostra=$($report.activeModel.sampleMethod)"
}

Write-Host ''
Write-Host 'Este relatório é read-only. Não cria, valida, ativa ou altera modelo.' -ForegroundColor Yellow
Write-Host 'Datas de nascimento permanecem fora deste Monte Carlo nominal.' -ForegroundColor Yellow
Write-Host "Relatório: $ReportPath"
Write-Host 'IBGE NOMINAL U MONTE CARLO REPORT: OK' -ForegroundColor Green
