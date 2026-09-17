param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'

function Get-BashExecutable {
    $git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($env:OS -eq 'Windows_NT') {
        if ($null -eq $git) { return $null }
        $gitCmdDir = Split-Path -Parent $git.Source
        $gitRoot = Split-Path -Parent $gitCmdDir
        foreach ($candidate in @(
            (Join-Path $gitRoot 'bin/bash.exe'),
            (Join-Path $gitRoot 'usr/bin/bash.exe')
        )) {
            if (Test-Path -LiteralPath $candidate) { return $candidate }
        }
        return $null
    }

    $bash = Get-Command bash -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $bash) { return $bash.Source }
    return $null
}

function Get-EnvValues {
    if (-not (Test-Path -LiteralPath $EnvFile)) { throw '.env não encontrado. Execute antes o preparo do ambiente local.' }
    $vars = @{}
    Get-Content -LiteralPath $EnvFile | ForEach-Object {
        $line = $_.Trim()
        if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
            $parts = $line.Split('=', 2)
            $vars[$parts[0].Trim()] = $parts[1]
        }
    }
    return $vars
}

function Resolve-LocalSqlContainerId {
    $canonicalName = 'jornada-sqlserver-local'
    $idOutput = @(& docker inspect --format '{{.Id}}' $canonicalName 2>$null)
    if ($LASTEXITCODE -ne 0 -or $idOutput.Count -eq 0) {
        throw "Container SQL Server local '$canonicalName' não existe. Execute .\scripts\local-cluster.ps1 -Action up."
    }
    $containerId = (($idOutput | Select-Object -First 1) -as [string]).Trim()
    if ([string]::IsNullOrWhiteSpace($containerId)) { throw "Container SQL Server local '$canonicalName' sem ID válido." }

    $stateOutput = @(& docker inspect --format '{{.State.Status}}|{{.State.Running}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' $containerId 2>$null)
    if ($LASTEXITCODE -ne 0 -or $stateOutput.Count -eq 0) { throw "Não foi possível consultar o estado de '$canonicalName'." }
    $state = (($stateOutput | Select-Object -First 1) -as [string]).Trim().Split('|')
    if ($state.Count -lt 3 -or $state[1].Trim().ToLowerInvariant() -ne 'true') {
        throw "Container SQL Server local '$canonicalName' não está em execução."
    }
    $health = $state[2].Trim().ToLowerInvariant()
    if ($health -ne 'none' -and $health -ne 'healthy') {
        throw "Container SQL Server local '$canonicalName' está com health=$health."
    }
    return $containerId
}

foreach ($command in @('git', 'docker', 'dotnet')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { throw "Comando '$command' não encontrado no PATH." }
}

$bash = Get-BashExecutable
if ([string]::IsNullOrWhiteSpace($bash)) {
    throw 'Bash compatível não encontrado. No Windows, instale/use o Git Bash do Git for Windows.'
}

$vars = Get-EnvValues
$password = $vars['JORNADA_SQL_SA_PASSWORD']
$port = if ($vars['JORNADA_SQL_PORT']) { $vars['JORNADA_SQL_PORT'] } else { '14333' }
$db = if ($vars['JORNADA_SQL_DATABASE']) { $vars['JORNADA_SQL_DATABASE'] } else { 'JornadaLocal' }
if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD não definido em .env.' }
$containerId = Resolve-LocalSqlContainerId

$old = @{
    Connection = $env:ConnectionStrings__Jornada
    Password = $env:JORNADA_EVALUATION_SQL_PASSWORD
    Database = $env:JORNADA_EVALUATION_DATABASE
    People = $env:JORNADA_EVALUATION_SCALE_PEOPLE
    Seed = $env:JORNADA_EVALUATION_SCALE_SEED
    Count = $env:JORNADA_EVALUATION_LABEL_COUNT
    Container = $env:JORNADA_EVALUATION_SQL_CONTAINER_ID
}

Push-Location $Root
try {
    $env:ConnectionStrings__Jornada = "Server=localhost,$port;Database=$db;User Id=sa;Password=$password;TrustServerCertificate=true;Encrypt=false"
    $env:JORNADA_EVALUATION_SQL_PASSWORD = $password
    $env:JORNADA_EVALUATION_DATABASE = $db
    $env:JORNADA_EVALUATION_SCALE_PEOPLE = '5000'
    $env:JORNADA_EVALUATION_SCALE_SEED = '355'
    $env:JORNADA_EVALUATION_LABEL_COUNT = '100'
    $env:JORNADA_EVALUATION_SQL_CONTAINER_ID = $containerId

    Write-Host "Executando linkage-evaluation-smoke.sh via $bash"
    & $bash ./scripts/linkage-evaluation-smoke.sh
    if ($LASTEXITCODE -ne 0) { throw "linkage-evaluation-smoke.sh falhou ($LASTEXITCODE)." }
}
finally {
    $env:ConnectionStrings__Jornada = $old.Connection
    $env:JORNADA_EVALUATION_SQL_PASSWORD = $old.Password
    $env:JORNADA_EVALUATION_DATABASE = $old.Database
    $env:JORNADA_EVALUATION_SCALE_PEOPLE = $old.People
    $env:JORNADA_EVALUATION_SCALE_SEED = $old.Seed
    $env:JORNADA_EVALUATION_LABEL_COUNT = $old.Count
    $env:JORNADA_EVALUATION_SQL_CONTAINER_ID = $old.Container
    Pop-Location
}

Write-Host 'LOCAL LINKAGE EVALUATION SMOKE: OK' -ForegroundColor Green
