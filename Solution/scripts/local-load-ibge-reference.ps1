param(
    [switch]$AllowLoad,
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'
$CheckScript = Join-Path $PSScriptRoot 'local-check-ibge-reference.ps1'

if (-not (Test-Path -LiteralPath $EnvFile -PathType Leaf)) { throw '.env local nao encontrado.' }
if (-not (Test-Path -LiteralPath $CheckScript -PathType Leaf)) { throw "Quick check IBGE nao encontrado: $CheckScript" }

function Invoke-Compose {
    param([Parameter(Mandatory = $true)][string[]]$ComposeArgs)

    Write-Host ("# docker compose --env-file .env " + ($ComposeArgs -join ' '))
    Push-Location $Root
    try {
        & docker compose --env-file $EnvFile @ComposeArgs
        if ($LASTEXITCODE -ne 0) { throw "docker compose falhou ($LASTEXITCODE)." }
    }
    finally { Pop-Location }
}

function Test-ReferenceReady {
    try {
        & $CheckScript -NoStart
        return $true
    }
    catch {
        Write-Host ''
        Write-Warning ("Quick check IBGE ainda nao passou: " + $_.Exception.Message)
        return $false
    }
}

# Nunca chama reset/clean. Apenas garante que o SQL existente esteja rodando.
Invoke-Compose -ComposeArgs @('up','-d','sqlserver')

if (Test-ReferenceReady) {
    Write-Host ''
    Write-Host 'IBGE REFERENCE LOAD: SKIPPED (referencia ja integra e ATIVA)' -ForegroundColor Green
    return
}

if (-not $AllowLoad) {
    Write-Host ''
    Write-Warning 'A referencia IBGE nao esta pronta. A carga canonica pode inserir 5.603.287 linhas.'
    Write-Host 'Para autorizar explicitamente a carga sem resetar o banco:'
    Write-Host '# .\scripts\local-load-ibge-reference.ps1 -AllowLoad'
    throw 'Carga IBGE nao autorizada. Informe -AllowLoad somente quando quiser materializar a referencia canonica.'
}

Write-Host ''
Write-Host 'Carga IBGE explicitamente autorizada: SIM.'
Write-Host 'O banco existente sera preservado; somente a referencia canonica sera assegurada/materializada.'

if (-not $NoBuild) {
    Invoke-Compose -ComposeArgs @('build','jornada-reference-bootstrap')
}

Invoke-Compose -ComposeArgs @('run','--rm','--no-deps','jornada-reference-bootstrap')

if (-not (Test-ReferenceReady)) {
    throw 'Loader terminou, mas o quick check da referencia IBGE ainda falha.'
}

Write-Host ''
Write-Host 'IBGE REFERENCE LOAD: OK' -ForegroundColor Green
Write-Host '  reset do banco: NAO'
Write-Host '  clean de volumes: NAO'
Write-Host '  fonte: snapshot local imutavel versionado no repositorio'
