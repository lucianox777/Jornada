param(
    [ValidateSet('up','reset','down','clean','status','logs')]
    [string]$Action = 'up'
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'
$Example = Join-Path $Root '.env.example'
$LocalDb = Join-Path $PSScriptRoot 'local-db.ps1'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker não encontrado no PATH.'
}

if (-not (Test-Path -LiteralPath $EnvFile)) {
    Copy-Item -LiteralPath $Example -Destination $EnvFile
    Write-Host 'Criado .env local com as credenciais sintéticas padrão de teste.'
}

function Invoke-Compose {
    param([Parameter(Mandatory=$true)][string[]]$ComposeArgs)
    Push-Location $Root
    try {
        & docker compose --env-file $EnvFile @ComposeArgs
        if ($LASTEXITCODE -ne 0) { throw "docker compose falhou ($LASTEXITCODE)." }
    }
    finally { Pop-Location }
}

function Wait-NodeReady([string]$Name, [string]$Url) {
    for ($i = 0; $i -lt 120; $i++) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 2
            if ([int]$response.StatusCode -eq 200) {
                Write-Host "$Name ready: $Url"
                return
            }
        }
        catch { }
        Start-Sleep -Seconds 1
    }

    Invoke-Compose -ComposeArgs @('logs','--tail','120',$Name)
    throw "$Name não ficou ready: $Url"
}

function Start-Nodes([switch]$Build) {
    $args = @('up','-d')
    if ($Build) { $args += '--build' }
    $args += @('jornada-node1','jornada-node2')
    Invoke-Compose -ComposeArgs $args
    Wait-NodeReady 'jornada-node1' 'http://127.0.0.1:5080/health/ready'
    Wait-NodeReady 'jornada-node2' 'http://127.0.0.1:5180/health/ready'
    Write-Host 'Cluster local pronto: NODE1=http://127.0.0.1:5080 NODE2=http://127.0.0.1:5180 SQL=localhost:14333'
}

switch ($Action) {
    'up' {
        & $LocalDb up
        if ($LASTEXITCODE -ne 0) { throw "local-db.ps1 up falhou ($LASTEXITCODE)." }
        Start-Nodes -Build
    }
    'reset' {
        Invoke-Compose -ComposeArgs @('stop','jornada-node1','jornada-node2')
        & $LocalDb reset
        if ($LASTEXITCODE -ne 0) { throw "local-db.ps1 reset falhou ($LASTEXITCODE)." }
        Start-Nodes
    }
    'down' {
        Invoke-Compose -ComposeArgs @('down')
    }
    'clean' {
        Invoke-Compose -ComposeArgs @('down','-v','--remove-orphans')
    }
    'status' {
        Invoke-Compose -ComposeArgs @('ps')
    }
    'logs' {
        Invoke-Compose -ComposeArgs @('logs','-f','jornada-node1','jornada-node2')
    }
}
