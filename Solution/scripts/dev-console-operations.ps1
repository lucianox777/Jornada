param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('reference-check','bronze-verify','ingest-latest','pipeline-status','blocking','calibrate','linkage','replay-latest','report')]
    [string]$Action
)

$ErrorActionPreference='Stop'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile=Join-Path $Root '.env'
$KeysFile=Join-Path $Root 'config/security/test-access-keys.json'
$OutDir=Join-Path $Root '.local/dev-console'
New-Item -ItemType Directory -Force $OutDir | Out-Null

if(-not(Test-Path $EnvFile)){throw '.env ausente. Suba a infraestrutura DEV primeiro.'}
$vars=@{}
Get-Content $EnvFile | ForEach-Object {
    $line=$_.Trim()
    if($line -and -not $line.StartsWith('#') -and $line.Contains('=')){
        $p=$line.Split('=',2)
        $vars[$p[0].Trim()]=$p[1].Trim()
    }
}
$db=if($vars['JORNADA_SQL_DATABASE']){$vars['JORNADA_SQL_DATABASE']}else{'JornadaLocal'}
$password=$vars['JORNADA_SQL_SA_PASSWORD']
if([string]::IsNullOrWhiteSpace($password)){throw 'JORNADA_SQL_SA_PASSWORD ausente.'}

function Invoke-Compose([string[]]$Args){
    Write-Host ('# docker compose --env-file .env '+($Args -join ' '))
    Push-Location $Root
    try {
        & docker compose --env-file $EnvFile @Args
        if($LASTEXITCODE -ne 0){throw "docker compose falhou ($LASTEXITCODE)."}
    } finally { Pop-Location }
}

function Invoke-SqlScalar([string]$Query){
    $old=$env:SQLCMDPASSWORD
    $env:SQLCMDPASSWORD=$password
    Push-Location $Root
    try {
        $raw=@(& docker compose --env-file $EnvFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $db -W -h -1 -Q "SET NOCOUNT ON; $Query")
        if($LASTEXITCODE -ne 0){throw "sqlcmd falhou ($LASTEXITCODE)."}
        return [string](@($raw | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ }) | Select-Object -Last 1)
    } finally {
        if($null -eq $old){Remove-Item Env:SQLCMDPASSWORD -ErrorAction SilentlyContinue}else{$env:SQLCMDPASSWORD=$old}
        Pop-Location
    }
}

function Ensure-ClusterRunning {
    $required=@('sqlserver','jornada-nas','jornada-node1','jornada-node2')
    $running=@()
    try {
        Push-Location $Root
        $running=@(& docker compose --env-file $EnvFile ps --status running --services 2>$null)
        Pop-Location
    } catch {
        try { Pop-Location } catch {}
        $running=@()
    }
    $missing=@($required | Where-Object { $_ -notin $running })
    if($missing.Count -eq 0){ return }

    Write-Host "Infraestrutura incompleta ($($missing -join ', ')); subindo automaticamente..."
    & (Join-Path $PSScriptRoot 'dev-console-infrastructure.ps1') -Action up
    if($LASTEXITCODE -ne 0){throw "Subida automática da infraestrutura falhou ($LASTEXITCODE)."}
}

function Invoke-ClusterAction([string]$ClusterAction){
    Ensure-ClusterRunning
    Write-Host "# pwsh -NoProfile -File scripts/local-cluster.ps1 -Action $ClusterAction"
    & (Join-Path $PSScriptRoot 'local-cluster.ps1') -Action $ClusterAction
    if($LASTEXITCODE -ne 0){throw "local-cluster.ps1 $ClusterAction falhou ($LASTEXITCODE)."}
}

function Get-DevCredential([string]$Gestor,[string]$RequiredScope){
    if(-not(Test-Path $KeysFile)){throw "Credenciais DEV não encontradas: $KeysFile"}
    $keys=Get-Content $KeysFile -Raw | ConvertFrom-Json
    $credential=@($keys.credentials | Where-Object {
        $_.type -eq 'GESTOR' -and $_.publicCode -eq $Gestor -and @($_.scopes) -contains $RequiredScope
    } | Select-Object -First 1)
    if($credential.Count -ne 1){throw "Credencial DEV GESTOR $Gestor sem scope $RequiredScope."}
    return $credential[0]
}

switch($Action){
    'reference-check' {
        Ensure-ClusterRunning
        Write-Host '# pwsh -NoProfile -File scripts/local-check-ibge-reference.ps1 -NoStart'
        & (Join-Path $PSScriptRoot 'local-check-ibge-reference.ps1') -NoStart
        if($LASTEXITCODE -ne 0){throw "Quick check IBGE falhou ($LASTEXITCODE)."}
    }

    'bronze-verify' {
        Ensure-ClusterRunning
        Invoke-Compose @('exec','-T','jornada-node2','dotnet','/opt/jornada/tools/Jornada.Bronze.Verify/Jornada.Bronze.Verify.dll','--minimum-count','0')
    }

    'ingest-latest' {
        Ensure-ClusterRunning
        $manualRoot=Join-Path $OutDir 'manual-zip'
        if(-not(Test-Path $manualRoot)){throw 'Nenhum ZIP manual foi gerado ainda.'}
        $zip=Get-ChildItem $manualRoot -Recurse -File -Filter '*.zip' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if($null -eq $zip){throw 'Nenhum ZIP manual foi gerado ainda.'}
        if($zip.Name -notmatch '^ENTREGA_([^_]+)_.+_v2_([0-9a-fA-F]{64})\.zip$'){throw "Nome de ZIP não canônico: $($zip.Name)"}
        $gestor=$Matches[1]
        $sha=$Matches[2].ToLowerInvariant()
        $credential=Get-DevCredential $gestor 'jornada.ingestao.write'
        $uri='http://127.0.0.1:5080/api/v1/ingestao/entregas'
        Write-Host "# POST $uri"
        Write-Host "ZIP: $($zip.FullName)"
        Write-Host "Gestor: $gestor"
        $headers=@{
            'X-Jornada-Gestor'=$gestor
            'X-Jornada-Access-Key'=[string]$credential.accessKey
            'Idempotency-Key'="sha256:$sha"
            'Content-Disposition'="attachment; filename=$($zip.Name)"
        }
        $response=Invoke-WebRequest -UseBasicParsing -Method Post -Uri $uri -Headers $headers -ContentType 'application/zip' -InFile $zip.FullName
        Write-Host "HTTP $([int]$response.StatusCode)"
        Write-Host $response.Content
        $receipt=$response.Content | ConvertFrom-Json
        $envelope=[ordered]@{
            zip=$zip.FullName
            gestor=$gestor
            receivedAt=(Get-Date).ToUniversalTime().ToString('o')
            receipt=$receipt
        }
        $result=Join-Path $OutDir 'last-ingestion.json'
        $envelope | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 $result
        Write-Host "Resultado salvo em: $result"
    }

    'pipeline-status' {
        Ensure-ClusterRunning
        $result=Join-Path $OutDir 'last-ingestion.json'
        if(-not(Test-Path $result)){throw 'Nenhuma ingestão registrada pela Console DEV.'}
        $saved=Get-Content $result -Raw | ConvertFrom-Json
        $entregaId=[string]$saved.receipt.entregaId
        if([string]::IsNullOrWhiteSpace($entregaId)){$entregaId=[string]$saved.receipt.EntregaId}
        if([string]::IsNullOrWhiteSpace($entregaId)){throw 'Recibo da última ingestão não contém entregaId.'}
        $gestor=[string]$saved.gestor
        $credential=Get-DevCredential $gestor 'jornada.ingestao.status'
        $uri="http://127.0.0.1:5080/api/v1/ingestao/entregas/$entregaId"
        Write-Host "# GET $uri"
        $headers=@{
            'X-Jornada-Gestor'=$gestor
            'X-Jornada-Access-Key'=[string]$credential.accessKey
        }
        $response=Invoke-WebRequest -UseBasicParsing -Method Get -Uri $uri -Headers $headers
        Write-Host "HTTP $([int]$response.StatusCode)"
        Write-Host $response.Content
        $statusPath=Join-Path $OutDir 'last-ingestion-status.json'
        $response.Content | Set-Content -Encoding UTF8 $statusPath
        Write-Host "Resultado salvo em: $statusPath"
    }

    'blocking' { Invoke-ClusterAction 'blocking' }

    'calibrate' { Invoke-ClusterAction 'calibrate' }

    'linkage' { Invoke-ClusterAction 'linkage' }

    'replay-latest' {
        Ensure-ClusterRunning
        $sourceRun=Invoke-SqlScalar "SELECT TOP(1) CONVERT(varchar(36),linkage_run_id) FROM identidade.linkage_run WHERE status=N'PUBLICADO' AND tipo_run<>N'REPLAY' ORDER BY publicado_em DESC,iniciado_em DESC;"
        if([string]::IsNullOrWhiteSpace($sourceRun)){throw 'Nenhum linkage PUBLICADO elegível para replay.'}
        Write-Host "Replay histórico do último run publicado: $sourceRun"
        Invoke-Compose @('exec','-T','jornada-node2','dotnet','/opt/jornada/apps/Jornada.Linkage.Runner/Jornada.Linkage.Runner.dll','--mode','REPLAY','--replay-source-run-id',$sourceRun,'--requested-by','DEV_CONSOLE','--reason','manual-dev-console-replay','--publish','false')
    }

    'report' { Invoke-ClusterAction 'linkage-diagnose' }
}
