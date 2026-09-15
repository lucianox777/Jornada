[CmdletBinding()]
param(
    [ValidateSet('standard', 'full')]
    [string]$Suite = 'full'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$CurrentPowerShell = (Get-Process -Id $PID).Path
$Results = [System.Collections.Generic.List[object]]::new()
$OverallStatus = 'FAILED'
$FailureMessage = $null
$testedSha = $null
$originalBranch = $null
$originalSha = $null
$stashCommit = $null
$stashCreated = $false

function Invoke-Git {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    & git @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') falhou ($LASTEXITCODE)." }
}

function Invoke-GitCapture {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    $output = @(& git @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') falhou ($LASTEXITCODE)." }
    return $output
}

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    Write-Host ''
    Write-Host "=== $Name ==="
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action
        $watch.Stop()
        $Results.Add([ordered]@{ name = $Name; status = 'OK'; seconds = [math]::Round($watch.Elapsed.TotalSeconds, 1) })
        Write-Host ("=== {0}: OK ({1:n1}s) ===" -f $Name, $watch.Elapsed.TotalSeconds)
    }
    catch {
        $watch.Stop()
        $Results.Add([ordered]@{ name = $Name; status = 'FAILED'; seconds = [math]::Round($watch.Elapsed.TotalSeconds, 1); error = $_.Exception.Message })
        throw
    }
}

function Invoke-PowerShellScript {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptName,
        [string[]]$Arguments = @()
    )

    $path = Join-Path $PSScriptRoot $ScriptName
    if (-not (Test-Path -LiteralPath $path)) { throw "Script não encontrado: $path" }
    & $CurrentPowerShell -NoLogo -NoProfile -ExecutionPolicy Bypass -File $path @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$ScriptName falhou ($LASTEXITCODE)." }
}

function Invoke-ClusterAction {
    param([Parameter(Mandatory = $true)][ValidateSet('clean','up','calibrate','linkage','linkage-diagnose')][string]$Action)
    Invoke-PowerShellScript 'local-cluster.ps1' @($Action)
}

function Get-BashExecutable {
    $git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1

    # No Windows, `bash` no PATH pode resolver para o launcher do WSL. A suíte local usa
    # ferramentas e caminhos do host Windows, então deve preferir explicitamente o Git Bash.
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

function Invoke-LinkageEvaluationSmoke {
    $bash = Get-BashExecutable
    if ([string]::IsNullOrWhiteSpace($bash)) {
        throw 'Bash compatível não encontrado. No Windows, a auditoria local exige o Git Bash do Git for Windows e não usa automaticamente o launcher do WSL.'
    }
    Write-Host "Bash selecionado para a auditoria read-only: $bash"

    $envFile = Join-Path $Root '.env'
    if (-not (Test-Path -LiteralPath $envFile)) { throw '.env não encontrado após preparação local.' }
    $vars = @{}
    Get-Content $envFile | ForEach-Object {
        $line = $_.Trim()
        if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
            $parts = $line.Split('=', 2)
            $vars[$parts[0].Trim()] = $parts[1]
        }
    }
    $password = $vars['JORNADA_SQL_SA_PASSWORD']
    $port = if ($vars['JORNADA_SQL_PORT']) { $vars['JORNADA_SQL_PORT'] } else { '14333' }
    $db = if ($vars['JORNADA_SQL_DATABASE']) { $vars['JORNADA_SQL_DATABASE'] } else { 'JornadaLocal' }
    if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD não definido em .env.' }

    Push-Location $Root
    try {
        $containerId = (& docker compose --env-file .env ps -q sqlserver | Select-Object -First 1).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) { throw 'Container SQL Server local não encontrado.' }

        $old = @{
            Connection = $env:ConnectionStrings__Jornada
            Password = $env:JORNADA_EVALUATION_SQL_PASSWORD
            Database = $env:JORNADA_EVALUATION_DATABASE
            People = $env:JORNADA_EVALUATION_SCALE_PEOPLE
            Seed = $env:JORNADA_EVALUATION_SCALE_SEED
            Count = $env:JORNADA_EVALUATION_LABEL_COUNT
            Container = $env:JORNADA_EVALUATION_SQL_CONTAINER_ID
        }
        try {
            $env:ConnectionStrings__Jornada = "Server=localhost,$port;Database=$db;User Id=sa;Password=$password;TrustServerCertificate=true;Encrypt=false"
            $env:JORNADA_EVALUATION_SQL_PASSWORD = $password
            $env:JORNADA_EVALUATION_DATABASE = $db
            $env:JORNADA_EVALUATION_SCALE_PEOPLE = '10000'
            $env:JORNADA_EVALUATION_SCALE_SEED = '355'
            $env:JORNADA_EVALUATION_LABEL_COUNT = '100'
            $env:JORNADA_EVALUATION_SQL_CONTAINER_ID = $containerId

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
        }
    }
    finally { Pop-Location }
}

foreach ($command in @('git', 'docker', 'dotnet')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { throw "Comando '$command' não encontrado no PATH." }
}

Push-Location $Root
try {
    $originalBranch = ((Invoke-GitCapture @('branch','--show-current')) -join '').Trim()
    $originalSha = ((Invoke-GitCapture @('rev-parse','HEAD')) -join '').Trim()
    if ([string]::IsNullOrWhiteSpace($originalSha)) { throw 'Não foi possível determinar o SHA Git original.' }

    $dirty = @(Invoke-GitCapture @('status','--porcelain','--untracked-files=normal'))
    if ($dirty.Count -gt 0) {
        $stashLabel = "jornada-local-test-all-$([Guid]::NewGuid().ToString('N'))"
        Write-Host 'Alterações locais detectadas; preservando automaticamente em stash temporário...'
        Invoke-Git @('stash','push','-u','-m',$stashLabel)
        $stashCommit = ((Invoke-GitCapture @('rev-parse','--verify','refs/stash')) -join '').Trim()
        if ([string]::IsNullOrWhiteSpace($stashCommit)) { throw 'Stash temporário não pôde ser identificado.' }
        $stashCreated = $true
        Write-Host "Stash temporário: $stashCommit"
    }

    Write-Host 'Atualizando master antes da suíte local...'
    Invoke-Git @('fetch','origin','master')
    Invoke-Git @('checkout','master')
    Invoke-Git @('pull','--ff-only','origin','master')

    $testedSha = ((Invoke-GitCapture @('rev-parse','HEAD')) -join '').Trim()
    Write-Host ''
    Write-Host 'Jornada - suíte local canônica'
    Write-Host "Suite:  $Suite"
    Write-Host 'Branch: master'
    Write-Host "SHA:    $testedSha"
    Write-Host 'A suíte é destrutiva para os bancos/volumes locais de teste, mas preserva alterações Git automaticamente.'

    Invoke-Step 'Core: contratos + runtime SQL + Unit + Integration' {
        Invoke-PowerShellScript 'local-test.ps1'
    }

    Invoke-Step 'DDL upgrade 3.65 -> 3.70 + idempotência' {
        Invoke-PowerShellScript 'local-ddl-upgrade.ps1'
    }

    Invoke-Step 'E2E HTTP -> Bronze -> Silver -> Gold -> Serving -> HTTP' {
        Invoke-PowerShellScript 'local-e2e.ps1'
    }

    Invoke-Step 'Fault injection do gate serial' {
        Invoke-PowerShellScript 'local-fault-injection.ps1'
    }

    Invoke-Step 'Cluster limpo: rebuild + calibrate + linkage + diagnose' {
        Invoke-ClusterAction 'clean'
        Invoke-ClusterAction 'up'
        Invoke-ClusterAction 'calibrate'
        Invoke-ClusterAction 'linkage'
        Invoke-ClusterAction 'linkage-diagnose'
    }

    if ($Suite -eq 'full') {
        Invoke-Step 'Encerrar cluster antes do harness de escala' {
            Invoke-ClusterAction 'clean'
        }

        Invoke-Step 'Scale harness smoke' {
            Invoke-PowerShellScript 'local-scale.ps1' @('-Profile', 'smoke')
        }

        Invoke-Step 'Auditoria read-only de candidate recall/rank' {
            Invoke-LinkageEvaluationSmoke
        }
    }

    $OverallStatus = 'OK'
}
catch {
    $FailureMessage = $_.Exception.Message
    Write-Warning "Suíte interrompida: $FailureMessage"
}
finally {
    try {
        if (-not [string]::IsNullOrWhiteSpace($originalSha)) {
            $currentBranch = ((Invoke-GitCapture @('branch','--show-current')) -join '').Trim()
            if (-not [string]::IsNullOrWhiteSpace($originalBranch)) {
                if ($currentBranch -ne $originalBranch) {
                    Write-Host "Restaurando branch original '$originalBranch'..."
                    Invoke-Git @('checkout',$originalBranch)
                }
            }
            else {
                $nowSha = ((Invoke-GitCapture @('rev-parse','HEAD')) -join '').Trim()
                if ($nowSha -ne $originalSha -or -not [string]::IsNullOrWhiteSpace($currentBranch)) {
                    Write-Host "Restaurando HEAD destacado original $originalSha..."
                    Invoke-Git @('checkout','--detach',$originalSha)
                }
            }
        }

        if ($stashCreated -and -not [string]::IsNullOrWhiteSpace($stashCommit)) {
            Write-Host 'Restaurando alterações locais preservadas...'
            & git stash apply --index $stashCommit
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Restauração automática encontrou conflito. O stash foi PRESERVADO em $stashCommit."
                $OverallStatus = 'FAILED'
                $FailureMessage = "Não foi possível restaurar automaticamente o stash $stashCommit."
            }
            else {
                $stashEntry = @(Invoke-GitCapture @('stash','list','--format=%gd %H')) |
                    Where-Object { $_ -match "\s$([regex]::Escape($stashCommit))$" } |
                    Select-Object -First 1
                if ($null -ne $stashEntry) {
                    $stashRef = ($stashEntry -split '\s+', 2)[0]
                    Invoke-Git @('stash','drop',$stashRef)
                    Write-Host 'Alterações locais restauradas; stash temporário removido.'
                }
            }
        }

        $reportDir = Join-Path $Root '.local/test-all'
        New-Item -ItemType Directory -Force $reportDir | Out-Null
        $reportPath = Join-Path $reportDir 'latest.json'
        [ordered]@{
            status = $OverallStatus
            suite = $Suite
            gitCommitSha = $testedSha
            generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
            failure = $FailureMessage
            steps = @($Results)
        } | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 $reportPath

        Write-Host ''
        Write-Host "Resumo: $reportPath"
        foreach ($result in $Results) {
            Write-Host ("{0,-58} {1,7} {2,8:n1}s" -f $result.name, $result.status, $result.seconds)
        }
    }
    finally {
        Pop-Location
    }
}

if ($OverallStatus -ne 'OK') {
    if ([string]::IsNullOrWhiteSpace($FailureMessage)) { $FailureMessage = 'Suíte local falhou.' }
    throw $FailureMessage
}

Write-Host ''
Write-Host 'LOCAL TEST ALL: OK'
