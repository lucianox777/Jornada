[CmdletBinding()]
param(
    [switch]$KeepData,
    [switch]$RemoveImages
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = $PSScriptRoot
$ClusterScript = Join-Path $Root 'scripts\local-cluster.ps1'
$EnvFile = Join-Path $Root '.env'
$EnvExistedBefore = Test-Path -LiteralPath $EnvFile

function Write-Banner {
    Write-Host ''
    Write-Host '============================================================'
    Write-Host ' JORNADA DO CIDADÃO - LIMPEZA LOCAL DOCKER'
    Write-Host '============================================================'
    Write-Host ''
}

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)] [string]$FilePath,
        [Parameter(Mandatory = $true)] [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath falhou com ExitCode=$LASTEXITCODE."
    }
}

function Test-ImageExists([string]$Image) {
    & docker image inspect $Image *> $null
    return $LASTEXITCODE -eq 0
}

Write-Banner

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker não encontrado no PATH.'
}

Write-Host ('[1/3] Removendo containers e {0}...' -f $(if ($KeepData) { 'preservando volumes' } else { 'volumes Jornada' }))
try {
    if ($KeepData) {
        & $ClusterScript down
    }
    else {
        & $ClusterScript clean
    }
    if ($LASTEXITCODE -ne 0) { throw "Falha ao limpar o cluster Jornada. ExitCode=$LASTEXITCODE" }
}
finally {
    if (-not $EnvExistedBefore -and (Test-Path -LiteralPath $EnvFile)) {
        Remove-Item -Force -LiteralPath $EnvFile
    }
}
Write-Host '      OK'

Write-Host '[2/3] Verificando recursos Docker Jornada...'
if (Test-Path -LiteralPath $EnvFile) {
    Push-Location $Root
    try {
        Invoke-Native 'docker' @('compose','--env-file',$EnvFile,'ps','--all')
    }
    finally { Pop-Location }
}
else {
    Write-Host '      .env não existia antes da limpeza; nenhum arquivo foi deixado para trás.'
}
Write-Host '      OK'

Write-Host '[3/3] Tratando imagens locais da Jornada...'
if ($RemoveImages) {
    foreach ($image in @('jornada-node:test','jornada-nas:test')) {
        if (Test-ImageExists $image) {
            Invoke-Native 'docker' @('image','rm',$image)
            Write-Host "      removida: $image"
        }
        else {
            Write-Host "      ausente: $image"
        }
    }
}
else {
    Write-Host '      imagens preservadas para acelerar a próxima instalação.'
    Write-Host '      use .\Clean-JornadaLocal.ps1 -RemoveImages para removê-las.'
}

Write-Host ''
Write-Host '------------------------------------------------------------'
Write-Host ' LIMPEZA CONCLUÍDA'
Write-Host '------------------------------------------------------------'
if ($KeepData) {
    Write-Host 'Volumes de dados Jornada foram preservados.'
}
else {
    Write-Host 'Containers, rede Compose, volumes Jornada e órfãos foram removidos.'
}
if ($RemoveImages) {
    Write-Host 'As imagens jornada-node:test e jornada-nas:test também foram removidas.'
}
Write-Host 'Nenhum prune global do Docker foi executado.'
Write-Host 'Imagens, containers e volumes de outros projetos não foram removidos.'
