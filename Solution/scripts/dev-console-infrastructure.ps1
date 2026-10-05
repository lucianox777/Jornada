param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('up','base','reference','nodes','corpus','blocking','model','finalize','reset','clean','status')]
    [string]$Action,
    [switch]$ConfirmProductionReset
)

$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$OutputEncoding=[Text.UTF8Encoding]::new($false)
if($PSVersionTable.PSVersion.Major -ge 7){$PSStyle.OutputRendering='PlainText'}
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'dev-console-env.ps1')
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Cluster=Join-Path $PSScriptRoot 'local-cluster.ps1'
$EnvFile=$DevConsoleEnvFile
$RuntimeMode=$DevConsoleRuntimeMode

function Invoke-Cluster([string]$ClusterAction,[switch]$NoBuild,[switch]$ConfirmDestructive){
    $displayArgs=@('-Action',$ClusterAction,'-EnvFile',$EnvFile,'-RuntimeMode',$RuntimeMode)
    $invokeParams=@{Action=$ClusterAction;EnvFile=$EnvFile;RuntimeMode=$RuntimeMode}
    if($NoBuild){$displayArgs+='-NoBuild';$invokeParams['NoBuild']=$true}
    if($ConfirmDestructive){$displayArgs+='-ConfirmProductionReset';$invokeParams['ConfirmProductionReset']=$true}
    Write-Host ("# pwsh -NoProfile -File scripts/local-cluster.ps1 "+($displayArgs -join ' '))
    & $Cluster @invokeParams
    if($LASTEXITCODE -ne 0){ throw "local-cluster.ps1 $ClusterAction falhou ($LASTEXITCODE)." }
}

function Get-CurrentSourceRevision {
    try {
        $value=(& git -C $Root rev-parse HEAD 2>$null | Out-String).Trim()
        if($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($value)){return $value}
    } catch {}
    return $null
}

function Test-RuntimeInputsDirty {
    try {
        $changes=@(& git -C $Root status --porcelain -- src config database install/container-test docker-compose.yml Directory.Build.props global.json 2>$null)
        return $LASTEXITCODE -ne 0 -or $changes.Count -gt 0
    } catch { return $true }
}

function Get-LocalRuntimeImageRevision {
    try {
        $value=(& docker image inspect --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}' jornada-node:test 2>$null | Out-String).Trim()
        if($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($value)){return $value}
    } catch {}
    return $null
}

function Test-LocalRuntimeImageExists {
    & docker image inspect jornada-node:test *> $null
    return $LASTEXITCODE -eq 0
}

function Invoke-ReferenceStage {
    Write-Host ''
    Write-Host '=== Etapa 2/7 · Referência IBGE ===' -ForegroundColor Cyan
    $sourceRevision=Get-CurrentSourceRevision
    $imageRevision=Get-LocalRuntimeImageRevision
    $runtimeInputsDirty=Test-RuntimeInputsDirty
    $imageFresh=(-not [string]::IsNullOrWhiteSpace($sourceRevision)) -and
        ($imageRevision -eq $sourceRevision) -and
        (-not $runtimeInputsDirty)

    if($imageFresh){
        Write-Host "Imagem jornada-node:test já corresponde à revisão $sourceRevision; reutilizando sem rebuild."
        Invoke-Cluster 'reference' -NoBuild
    }else{
        if($runtimeInputsDirty){
            Write-Host 'Há alterações locais em entradas de runtime; reconstruindo a imagem uma vez nesta etapa.'
        }elseif([string]::IsNullOrWhiteSpace($imageRevision)){
            Write-Host 'Imagem local sem revisão rastreável; fazendo build para sincronizar com o código atual.'
        }else{
            Write-Host "Imagem local está na revisão $imageRevision e o código está em $sourceRevision; reconstruindo."
        }
        Invoke-Cluster 'reference'
    }
}

function Invoke-BaseStage {
    Write-Host ''
    Write-Host '=== Etapa 1/7 · Banco e serviços básicos ===' -ForegroundColor Cyan
    Invoke-Cluster 'base'
}

function Invoke-NodesStage {
    Write-Host ''
    Write-Host '=== Etapa 3/7 · NODE1 / NODE2 ===' -ForegroundColor Cyan
    # A etapa de referência é a autoridade de build da imagem de runtime. Evitar
    # um segundo build caro no mesmo fluxo; se a imagem não existe, recuperar aqui.
    if(Test-LocalRuntimeImageExists){
        Invoke-Cluster 'nodes' -NoBuild
    }else{
        Write-Host 'Imagem jornada-node:test ausente; construindo como recuperação da etapa de nós.'
        Invoke-Cluster 'nodes'
    }
}

function Invoke-CorpusStage {
    Write-Host ''
    Write-Host '=== Etapa 4/7 · Corpus de calibração ===' -ForegroundColor Cyan
    Invoke-Cluster 'synthetic-identities' -NoBuild
    & (Join-Path $PSScriptRoot 'dev-console-operations.ps1') -Action bootstrap-corpus
    if($LASTEXITCODE -ne 0){throw "Materialização do corpus de calibração falhou ($LASTEXITCODE)."}
}

function Invoke-BlockingStage {
    Write-Host ''
    Write-Host '=== Etapa 5/7 · Blocking ===' -ForegroundColor Cyan
    Invoke-Cluster 'blocking-refresh' -NoBuild
}

function Invoke-ModelStage {
    Write-Host ''
    Write-Host '=== Etapa 6/7 · Modelo inicial ===' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'dev-console-operations.ps1') -Action calibrate-initial
    if($LASTEXITCODE -ne 0){throw "Garantia do modelo BOOTSTRAP inicial falhou ($LASTEXITCODE)."}
}

function Invoke-FinalizeStage {
    Write-Host ''
    Write-Host '=== Etapa 7/7 · Finalização ===' -ForegroundColor Cyan
    Write-Host 'Gerando configuração inicial da Console (JSON + HTML)...'
    & (Join-Path $PSScriptRoot 'dev-console-initial-config.ps1')
    if($LASTEXITCODE -ne 0){throw "Geração da configuração inicial falhou ($LASTEXITCODE)."}

    Write-Host ''
    Write-Host 'Gerando bundle inicial de contratos e configurações exigido pela ingestão...'
    & (Join-Path $PSScriptRoot 'dev-console-contract-bundle.ps1')
    if($LASTEXITCODE -ne 0){throw "Geração do bundle inicial falhou ($LASTEXITCODE)."}

    Write-Host ''
    Write-Host "Ambiente preparado: modo=$RuntimeMode; SQL/schema + NAS + referência IBGE + NODE1/NODE2 + corpus + blocking + modelo ATIVO."
    Write-Host 'O serviço jornada-reference-bootstrap é um init one-shot: Exited (0) significa CONCLUÍDO com sucesso, não falha.'
    Write-Host "# docker compose --env-file $(Split-Path -Leaf $EnvFile) ps -a"
    Push-Location $Root
    try {
        & docker compose --env-file $EnvFile ps -a
        if($LASTEXITCODE -ne 0){ throw "docker compose ps -a falhou ($LASTEXITCODE)." }
    } finally { Pop-Location }
}

function Invoke-AllStages {
    Invoke-BaseStage
    Invoke-ReferenceStage
    Invoke-NodesStage
    Invoke-CorpusStage
    Invoke-BlockingStage
    Invoke-ModelStage
    Invoke-FinalizeStage
}

switch($Action){
    'up' { Invoke-AllStages }
    'base' { Invoke-BaseStage }
    'reference' { Invoke-ReferenceStage }
    'nodes' { Invoke-NodesStage }
    'corpus' { Invoke-CorpusStage }
    'blocking' { Invoke-BlockingStage }
    'model' { Invoke-ModelStage }
    'finalize' { Invoke-FinalizeStage }
    'reset' {
        if($RuntimeMode -eq 'PROD' -and -not $ConfirmProductionReset){
            throw 'Reset bloqueado em PROD. Execute o comando explicitamente com -ConfirmProductionReset; a interface não oferece essa confirmação.'
        }
        Invoke-Cluster 'reset' -ConfirmDestructive:$ConfirmProductionReset
        Invoke-CorpusStage
        Invoke-BlockingStage
        Invoke-ModelStage
        Invoke-FinalizeStage
    }
    'clean' {
        if($RuntimeMode -eq 'PROD' -and -not $ConfirmProductionReset){
            throw 'Clean destrutivo bloqueado em PROD. Execute o comando explicitamente com -ConfirmProductionReset; a interface não oferece essa confirmação.'
        }
        Invoke-Cluster 'clean' -ConfirmDestructive:$ConfirmProductionReset
        $stateDir=Join-Path $Root '.local/dev-console'
        if(Test-Path $stateDir){
            Remove-Item -Recurse -Force $stateDir
            Write-Host "Estado transitório da Console removido: $stateDir"
        }
        Write-Host 'Histórico e contadores de execução foram preservados; recibos, bundles, ZIPs e artefatos ligados ao ambiente foram limpos.'
        Write-Host "Ambiente $RuntimeMode destruído: containers, volumes e órfãos locais removidos."
        Write-Host 'Na próxima subida, as etapas poderão ser executadas individualmente ou pelo preparo completo.'
    }
    'status' {
        Invoke-Cluster 'status'
        Write-Host ''
        Write-Host "Modo runtime: $RuntimeMode."
        Write-Host 'Nota: jornada-reference-bootstrap deve aparecer como processo concluído (Exited 0) após materializar/validar a referência.'
    }
}
