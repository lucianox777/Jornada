param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('up','reset','clean','status')]
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
    $args=@('-Action',$ClusterAction,'-EnvFile',$EnvFile,'-RuntimeMode',$RuntimeMode)
    if($NoBuild){$args+='-NoBuild'}
    if($ConfirmDestructive){$args+='-ConfirmProductionReset'}
    Write-Host ("# pwsh -NoProfile -File scripts/local-cluster.ps1 "+($args -join ' '))
    & $Cluster @args
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

function Complete-ConsoleBootstrap {
    Write-Host ''
    Write-Host "Infraestrutura básica pronta: modo=$RuntimeMode; SQL Server + schema + NAS + referência IBGE + NODE1/NODE2."
    Write-Host ''
    Write-Host 'Garantindo modelo BOOTSTRAP inicial ATIVO (IBGE + corpus sintético de calibração)...'
    & (Join-Path $PSScriptRoot 'dev-console-operations.ps1') -Action calibrate-initial
    if($LASTEXITCODE -ne 0){throw "Garantia do modelo BOOTSTRAP inicial falhou ($LASTEXITCODE)."}
    Write-Host ''
    Write-Host 'Gerando configuração inicial da Console (JSON + HTML)...'
    & (Join-Path $PSScriptRoot 'dev-console-initial-config.ps1')
    if($LASTEXITCODE -ne 0){throw "Geração da configuração inicial falhou ($LASTEXITCODE)."}

    Write-Host ''
    Write-Host 'Gerando bundle inicial de contratos e configurações exigido pela ingestão...'
    & (Join-Path $PSScriptRoot 'dev-console-contract-bundle.ps1')
    if($LASTEXITCODE -ne 0){throw "Geração do bundle inicial falhou ($LASTEXITCODE)."}

    Write-Host 'O serviço jornada-reference-bootstrap é um init one-shot: Exited (0) significa CONCLUÍDO com sucesso, não falha.'
    Write-Host "# docker compose --env-file $(Split-Path -Leaf $EnvFile) ps -a"
    Push-Location $Root
    try {
        & docker compose --env-file $EnvFile ps -a
        if($LASTEXITCODE -ne 0){ throw "docker compose ps -a falhou ($LASTEXITCODE)." }
    } finally { Pop-Location }
}

switch($Action){
    'up' {
        $sourceRevision=Get-CurrentSourceRevision
        $imageRevision=Get-LocalRuntimeImageRevision
        $runtimeInputsDirty=Test-RuntimeInputsDirty
        $imageFresh=(-not [string]::IsNullOrWhiteSpace($sourceRevision)) -and
            ($imageRevision -eq $sourceRevision) -and
            (-not $runtimeInputsDirty)

        if($imageFresh){
            Write-Host "Imagem jornada-node:test já corresponde à revisão $sourceRevision; reutilizando sem rebuild."
            Invoke-Cluster 'up' -NoBuild
        }else{
            if($runtimeInputsDirty){
                Write-Host 'Há alterações locais em entradas de runtime; reconstruindo a imagem para evitar binários obsoletos.'
            }elseif([string]::IsNullOrWhiteSpace($imageRevision)){
                Write-Host 'Imagem local sem revisão rastreável; fazendo build único para sincronizar com o código atual.'
            }else{
                Write-Host "Imagem local está na revisão $imageRevision e o código está em $sourceRevision; reconstruindo uma vez."
            }
            Invoke-Cluster 'up'
        }
        Complete-ConsoleBootstrap
    }
    'reset' {
        if($RuntimeMode -eq 'PROD' -and -not $ConfirmProductionReset){
            throw 'Reset bloqueado em PROD. Execute o comando explicitamente com -ConfirmProductionReset; a interface não oferece essa confirmação.'
        }
        Invoke-Cluster 'reset' -ConfirmDestructive:$ConfirmProductionReset
        Complete-ConsoleBootstrap
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
        Write-Host 'Histórico de execuções foi preservado; recibos, bundles, ZIPs e artefatos ligados ao ambiente foram limpos.'
        Write-Host "Ambiente $RuntimeMode destruído: containers, volumes e órfãos locais removidos."
        Write-Host 'Na próxima subida, a infraestrutura será recriada automaticamente.'
    }
    'status' {
        Invoke-Cluster 'status'
        Write-Host ''
        Write-Host "Modo runtime: $RuntimeMode."
        Write-Host 'Nota: jornada-reference-bootstrap deve aparecer como processo concluído (Exited 0) após materializar/validar a referência.'
    }
}
