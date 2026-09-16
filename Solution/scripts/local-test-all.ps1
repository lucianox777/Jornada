[CmdletBinding()]
param(
    [ValidateSet('standard', 'full')]
    [string]$Suite = 'full',
    [switch]$IsolatedExecution,
    [switch]$CleanMasterExecution
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$CurrentPowerShell = (Get-Process -Id $PID).Path
$Results = [System.Collections.Generic.List[object]]::new()
$OverallStatus = 'FAILED'
$FailureMessage = $null
$testedSha = $null

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

function Get-GitStatePath {
    param([Parameter(Mandatory = $true)][string]$Name)
    return ((Invoke-GitCapture @('rev-parse','--git-path',$Name)) -join '').Trim()
}

function Stop-InProgressGitOperation {
    $rebaseMerge = Get-GitStatePath 'rebase-merge'
    $rebaseApply = Get-GitStatePath 'rebase-apply'
    $mergeHead = Get-GitStatePath 'MERGE_HEAD'
    $cherryPickHead = Get-GitStatePath 'CHERRY_PICK_HEAD'
    $revertHead = Get-GitStatePath 'REVERT_HEAD'

    if ((Test-Path -LiteralPath $rebaseMerge) -or (Test-Path -LiteralPath $rebaseApply)) {
        Write-Host 'Rebase em andamento detectado; abortando antes da sincronização limpa...'
        & git rebase --abort
        if ($LASTEXITCODE -ne 0) {
            Write-Warning 'git rebase --abort falhou; encerrando o estado de rebase com --quit antes do reset forçado.'
            & git rebase --quit
            if ($LASTEXITCODE -ne 0) { throw 'Não foi possível encerrar o rebase em andamento.' }
        }
    }

    if (Test-Path -LiteralPath $mergeHead) {
        Write-Host 'Merge em andamento detectado; abortando antes da sincronização limpa...'
        & git merge --abort
        if ($LASTEXITCODE -ne 0) {
            Write-Warning 'git merge --abort falhou; o reset --hard subsequente tentará limpar o estado.'
        }
    }

    if (Test-Path -LiteralPath $cherryPickHead) {
        Write-Host 'Cherry-pick em andamento detectado; abortando antes da sincronização limpa...'
        & git cherry-pick --abort
        if ($LASTEXITCODE -ne 0) {
            Write-Warning 'git cherry-pick --abort falhou; o reset --hard subsequente tentará limpar o estado.'
        }
    }

    if (Test-Path -LiteralPath $revertHead) {
        Write-Host 'Revert em andamento detectado; abortando antes da sincronização limpa...'
        & git revert --abort
        if ($LASTEXITCODE -ne 0) {
            Write-Warning 'git revert --abort falhou; o reset --hard subsequente tentará limpar o estado.'
        }
    }
}

function Sync-CleanMaster {
    Write-Host ''
    Write-Host 'ATENÇÃO: -Suite full descarta alterações Git locais e arquivos não rastreados.'
    Write-Host 'Arquivos ignorados pelo Git (por exemplo .env) são preservados.'
    Write-Host 'Buscando origin/master antes de alterar o working tree...'

    Invoke-Git @('fetch','origin','master')
    $remoteSha = ((Invoke-GitCapture @('rev-parse','origin/master')) -join '').Trim()
    if ($remoteSha -notmatch '^[0-9a-fA-F]{40}$') { throw 'SHA de origin/master inválido.' }

    Stop-InProgressGitOperation

    # Limpa conflitos e alterações rastreadas no checkout atual. A referência remota já foi
    # obtida com sucesso acima, então uma falha de rede nunca causa perda local antecipada.
    Invoke-Git @('reset','--hard','HEAD')
    Invoke-Git @('clean','-fd')

    $currentBranch = ((Invoke-GitCapture @('branch','--show-current')) -join '').Trim()
    if ($currentBranch -ne 'master') {
        Invoke-Git @('switch','--discard-changes','master')
    }

    Invoke-Git @('reset','--hard','origin/master')
    Invoke-Git @('clean','-fd')

    $localSha = ((Invoke-GitCapture @('rev-parse','HEAD')) -join '').Trim()
    $branch = ((Invoke-GitCapture @('branch','--show-current')) -join '').Trim()
    $dirty = @(Invoke-GitCapture @('status','--porcelain','--untracked-files=normal'))

    if ($branch -ne 'master') { throw "Branch esperada 'master', obtida '$branch'." }
    if ($localSha -ne $remoteSha) { throw "master local ($localSha) diverge de origin/master ($remoteSha)." }
    if ($dirty.Count -gt 0) { throw 'Working tree ainda possui alterações após a limpeza.' }

    Write-Host "master local sincronizado e limpo em $localSha."
    return $localSha
}

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

# -Suite full é o gate canônico destrutivo: sincroniza o próprio checkout com origin/master
# e executa a versão do script que acabou de ser obtida do remoto. -Suite standard mantém
# o modo isolado em worktree temporário para uso rápido durante desenvolvimento.
if (-not $IsolatedExecution -and -not $CleanMasterExecution) {
    if ($Suite -eq 'full') {
        Push-Location $Root
        try {
            $testedSha = Sync-CleanMaster
            $syncedScript = Join-Path $Root 'scripts/local-test-all.ps1'
            Write-Host 'Reiniciando a suíte a partir do script sincronizado do master...'
            & $CurrentPowerShell -NoLogo -NoProfile -ExecutionPolicy Bypass -File $syncedScript -Suite full -CleanMasterExecution
            $exitCode = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }

        if ($exitCode -ne 0) {
            throw 'Suíte local full falhou. Consulte .local/test-all/latest.json quando disponível.'
        }

        Write-Host ''
        Write-Host 'LOCAL TEST ALL: OK (master local limpo e sincronizado com origin/master)'
        return
    }

    $worktreePath = Join-Path ([IO.Path]::GetTempPath()) ("jornada-local-test-all-{0}" -f [Guid]::NewGuid().ToString('N'))
    $childReport = Join-Path $worktreePath 'Solution/.local/test-all/latest.json'
    $targetReportDir = Join-Path $Root '.local/test-all'
    $targetReport = Join-Path $targetReportDir 'latest.json'
    $exitCode = 1

    Push-Location $Root
    try {
        Write-Host 'Atualizando referência origin/master sem tocar no working tree atual...'
        Invoke-Git @('fetch','origin','master')
        $testedSha = ((Invoke-GitCapture @('rev-parse','origin/master')) -join '').Trim()
        if ($testedSha -notmatch '^[0-9a-fA-F]{40}$') { throw 'SHA de origin/master inválido.' }

        Write-Host "Criando worktree isolado para $testedSha..."
        Invoke-Git @('worktree','add','--detach',$worktreePath,$testedSha)
        $isolatedScript = Join-Path $worktreePath 'Solution/scripts/local-test-all.ps1'
        & $CurrentPowerShell -NoLogo -NoProfile -ExecutionPolicy Bypass -File $isolatedScript -Suite $Suite -IsolatedExecution
        $exitCode = $LASTEXITCODE
    }
    catch {
        $FailureMessage = $_.Exception.Message
        Write-Warning $FailureMessage
        $exitCode = 1
    }
    finally {
        if (Test-Path -LiteralPath $childReport) {
            New-Item -ItemType Directory -Force $targetReportDir | Out-Null
            Copy-Item -LiteralPath $childReport -Destination $targetReport -Force
            Write-Host "Resumo copiado para: $targetReport"
        }

        if (Test-Path -LiteralPath $worktreePath) {
            & git worktree remove --force $worktreePath
            if ($LASTEXITCODE -ne 0) { Write-Warning "Não foi possível remover automaticamente o worktree temporário: $worktreePath" }
        }
        & git worktree prune
        Pop-Location
    }

    if ($exitCode -ne 0) {
        throw 'Suíte local isolada falhou. Consulte .local/test-all/latest.json quando disponível.'
    }

    Write-Host ''
    Write-Host 'LOCAL TEST ALL: OK (worktree isolado; working tree do desenvolvedor preservado)'
    return
}

Push-Location $Root
try {
    $testedSha = ((Invoke-GitCapture @('rev-parse','HEAD')) -join '').Trim()
    if ($testedSha -notmatch '^[0-9a-fA-F]{40}$') { throw 'SHA Git inválido no checkout de teste.' }

    $executionMode = if ($CleanMasterExecution) { 'clean-master' } else { 'isolated-worktree' }
    $branchDescription = if ($CleanMasterExecution) { 'master sincronizado com origin/master' } else { 'detached worktree de origin/master' }

    Write-Host ''
    Write-Host 'Jornada - suíte local canônica'
    Write-Host "Suite:  $Suite"
    Write-Host "Branch: $branchDescription"
    Write-Host "SHA:    $testedSha"
    if ($CleanMasterExecution) {
        Write-Host 'O working tree foi limpo antes da suíte; alterações locais rastreadas e não rastreadas foram descartadas.'
    }
    else {
        Write-Host 'A suíte é destrutiva para bancos/volumes locais de teste, mas não altera o working tree do desenvolvedor.'
    }

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
    $reportDir = Join-Path $Root '.local/test-all'
    New-Item -ItemType Directory -Force $reportDir | Out-Null
    $reportPath = Join-Path $reportDir 'latest.json'
    [ordered]@{
        status = $OverallStatus
        suite = $Suite
        gitCommitSha = $testedSha
        executionMode = $executionMode
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        failure = $FailureMessage
        steps = @($Results)
    } | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 $reportPath

    Write-Host ''
    Write-Host "Resumo: $reportPath"
    foreach ($result in $Results) {
        Write-Host ("{0,-58} {1,7} {2,8:n1}s" -f $result.name, $result.status, $result.seconds)
    }
    Pop-Location
}

if ($OverallStatus -ne 'OK') {
    if ([string]::IsNullOrWhiteSpace($FailureMessage)) { $FailureMessage = 'Suíte local falhou.' }
    throw $FailureMessage
}

Write-Host ''
Write-Host 'LOCAL TEST ALL: OK'
