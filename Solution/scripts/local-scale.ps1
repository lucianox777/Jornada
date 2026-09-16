param([ValidateSet('smoke','medium','million','custom')][string]$Profile='smoke')
$ErrorActionPreference='Stop'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Resolve-Python3 {
    foreach ($candidate in @(
        @{ Name = 'python3'; Prefix = @() },
        @{ Name = 'python'; Prefix = @() },
        @{ Name = 'py'; Prefix = @('-3') }
    )) {
        $command = Get-Command $candidate.Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $command) { continue }
        $prefix = @($candidate.Prefix)
        & $command.Source @prefix -c 'import sys; raise SystemExit(0 if sys.version_info.major == 3 else 1)' 2>$null
        if ($LASTEXITCODE -eq 0) {
            return @{ Exe = $command.Source; Prefix = $prefix }
        }
    }
    throw 'Python 3 não encontrado (tentados: python3, python, py -3).'
}

$Python3 = Resolve-Python3
switch ($Profile) {
  'smoke'   { $people=10000;   $paired=6000;  $pending=5000;   $sample=5000;  $pool=10000 }
  'medium'  { $people=100000;  $paired=20000; $pending=25000;  $sample=10000; $pool=100000 }
  'million' { $people=1000000; $paired=50000; $pending=100000; $sample=25000; $pool=500000 }
  'custom'  {
    $people=[int64]$env:JORNADA_SCALE_PEOPLE; $paired=[int64]$env:JORNADA_SCALE_PAIRED; $pending=[int64]$env:JORNADA_SCALE_PENDING
    if ($people -le 0 -or $paired -le 0 -or $pending -le 0) { throw 'Defina JORNADA_SCALE_PEOPLE/JORNADA_SCALE_PAIRED/JORNADA_SCALE_PENDING.' }
    $sample=if($env:JORNADA_SCALE_SAMPLE){[int]$env:JORNADA_SCALE_SAMPLE}else{5000}; $pool=if($env:JORNADA_SCALE_POOL){[int]$env:JORNADA_SCALE_POOL}else{$people}
  }
}
$seed=if($env:JORNADA_SCALE_SEED){[int]$env:JORNADA_SCALE_SEED}else{355}
$collisionModulo=if($env:JORNADA_SCALE_COLLISION_MODULO){[int]$env:JORNADA_SCALE_COLLISION_MODULO}else{37}
$birthShiftModulo=if($env:JORNADA_SCALE_BIRTH_SHIFT_MODULO){[int]$env:JORNADA_SCALE_BIRTH_SHIFT_MODULO}else{29}
$parallel=if($env:JORNADA_SCALE_PARALLELISM){[int]$env:JORNADA_SCALE_PARALLELISM}else{4}
$batch=if($env:JORNADA_SCALE_BATCH_SIZE){[int]$env:JORNADA_SCALE_BATCH_SIZE}else{10000}
$lockHolderDelayMs=if($env:JORNADA_SCALE_LOCK_HOLDER_DELAY_MS){[int]$env:JORNADA_SCALE_LOCK_HOLDER_DELAY_MS}else{3000}

& (Join-Path $PSScriptRoot 'local-db.ps1') -Action reset
$vars=@{}; Get-Content (Join-Path $Root '.env') | % { $l=$_.Trim(); if($l -and -not $l.StartsWith('#') -and $l.Contains('=')){ $p=$l.Split('=',2); $vars[$p[0].Trim()]=$p[1] } }
$port=if($vars['JORNADA_SQL_PORT']){$vars['JORNADA_SQL_PORT']}else{'14333'}
$db=if($vars['JORNADA_SQL_DATABASE']){$vars['JORNADA_SQL_DATABASE']}else{'JornadaLocal'}
$pwd=$vars['JORNADA_SQL_SA_PASSWORD']
if([string]::IsNullOrWhiteSpace($pwd)){throw 'JORNADA_SQL_SA_PASSWORD não definido para o scale harness.'}

function Get-ContainerSqlPassword {
    Push-Location $Root
    try {
        $previousErrorActionPreference=$ErrorActionPreference
        try {
            $ErrorActionPreference='Continue'
            $raw=@(& docker compose --env-file .env exec -T sqlserver printenv MSSQL_SA_PASSWORD 2>$null)
            $exitCode=$LASTEXITCODE
        }
        finally { $ErrorActionPreference=$previousErrorActionPreference }
        if($exitCode -ne 0){throw 'Não foi possível ler MSSQL_SA_PASSWORD do container SQL Server.'}
        $value=($raw -join "`n").Trim()
        if([string]::IsNullOrWhiteSpace($value)){throw 'MSSQL_SA_PASSWORD efetivo do container está vazio.'}
        return $value
    }
    finally { Pop-Location }
}

$containerPwd=Get-ContainerSqlPassword
if(-not [string]::Equals($pwd,$containerPwd,[StringComparison]::Ordinal)){
    throw 'JORNADA_SQL_SA_PASSWORD da .env diverge da senha efetiva do container SQL Server.'
}
$pwd=$containerPwd

function Wait-SqlLogin {
    Push-Location $Root
    try {
        for($attempt=1;$attempt -le 30;$attempt++){
            $previousErrorActionPreference=$ErrorActionPreference
            try {
                $ErrorActionPreference='Continue'
                & docker compose --env-file .env exec -T -e "SQLCMDPASSWORD=$pwd" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -Q 'SET NOCOUNT ON; SELECT 1;' *> $null
                $exitCode=$LASTEXITCODE
            }
            finally { $ErrorActionPreference=$previousErrorActionPreference }
            if($exitCode -eq 0){
                if($attempt -gt 1){Write-Host "Login SQL do scale harness estabilizado na tentativa $attempt/30."}
                return
            }
            if($attempt -eq 1 -or $attempt % 5 -eq 0){Write-Host "Aguardando login SQL do scale harness... tentativa $attempt/30"}
            Start-Sleep -Seconds 1
        }
        & docker compose --env-file .env logs --tail 80 sqlserver
        throw 'SQL Server não aceitou autenticação do scale harness em 30 segundos após o reset.'
    }
    finally { Pop-Location }
}

function SqlCmd {
    param([Parameter(Mandatory=$true)][string[]]$SqlCmdArgs)
    Push-Location $Root
    try {
        & docker compose --env-file .env exec -T -e "SQLCMDPASSWORD=$pwd" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b @SqlCmdArgs
        if($LASTEXITCODE -ne 0){throw 'sqlcmd falhou.'}
    }
    finally { Pop-Location }
}
function Scalar([string]$Query){
    Push-Location $Root
    try {
        $o = (& docker compose --env-file .env exec -T -e "SQLCMDPASSWORD=$pwd" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d $db -h -1 -y 0 -w 65535 -Q "SET NOCOUNT ON; $Query")
        if($LASTEXITCODE -ne 0){throw 'sqlcmd falhou.'}
        return ($o | ? { $_.Trim() } | Select-Object -Last 1).Trim()
    }
    finally { Pop-Location }
}
function Probe-Lock([string]$Resource,[int]$DelayMs){
    $delay=[TimeSpan]::FromMilliseconds($DelayMs).ToString('hh\:mm\:ss\.fff')
    $holderSql="DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'$Resource',@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; IF @r<0 THROW 51990,'probe holder lock failed',1; WAITFOR DELAY '$delay'; DECLARE @release int; EXEC @release=sys.sp_releaseapplock @Resource=N'$Resource',@LockOwner='Session';"
    $job=Start-Job -ScriptBlock {
        param($root,$pwd,$db,$sql)
        Push-Location $root
        try {
            & docker compose --env-file .env exec -T -e "SQLCMDPASSWORD=$pwd" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d $db -Q $sql | Out-Null
            if($LASTEXITCODE -ne 0){throw 'holder sqlcmd falhou'}
        } finally { Pop-Location }
    } -ArgumentList $Root,$pwd,$db,$holderSql
    Start-Sleep -Milliseconds 250
    $sw=[Diagnostics.Stopwatch]::StartNew()
    $result=[int](Scalar "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'$Resource',@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=10000; DECLARE @release int; IF @r>=0 EXEC @release=sys.sp_releaseapplock @Resource=N'$Resource',@LockOwner='Session'; SELECT @r;")
    $sw.Stop()
    Wait-Job $job | Out-Null
    Receive-Job $job | Out-Null
    Remove-Job $job
    return [ordered]@{resource=$Resource;lockResult=$result;waitMilliseconds=[int64]$sw.ElapsedMilliseconds}
}

Wait-SqlLogin
SqlCmd -SqlCmdArgs @('-d',$db,'-v',"SCALE_PEOPLE=$people","SCALE_PAIRED=$paired","SCALE_PENDING=$pending","SCALE_SEED=$seed","SCALE_COLLISION_MODULO=$collisionModulo","SCALE_BIRTH_SHIFT_MODULO=$birthShiftModulo",'-i','/workspace/database/Jornada_Dev_SyntheticScale.sql')
$conn="Server=localhost,$port;Database=$db;User Id=sa;Password=$pwd;TrustServerCertificate=true;Encrypt=false"
Push-Location $Root
try {
  if($env:JORNADA_LOCKED_RESTORE -eq 'true'){ dotnet restore Jornada.sln --locked-mode } else { dotnet restore Jornada.sln }; if($LASTEXITCODE-ne 0){throw 'restore falhou'}
  dotnet build Jornada.sln --configuration Release --no-restore -warnaserror; if($LASTEXITCODE-ne 0){throw 'build falhou'}
  $env:ConnectionStrings__Jornada=$conn; $env:PipelineCoordination__HeartbeatSeconds='2'; $env:PipelineCoordination__ExclusiveIntentTimeoutSeconds='5'
  $env:LinkageParameters__Operation='GENERATE_DRAFT'; $env:LinkageParameters__RunOnce='true'; $env:LinkageParameters__TrainingSampleSize="$sample"; $env:LinkageParameters__TrainingSamplePoolSize="$pool"; $env:LinkageParameters__MinimumIndependentMatchedPairs=if($env:JORNADA_SCALE_MIN_MATCHED_PAIRS){$env:JORNADA_SCALE_MIN_MATCHED_PAIRS}else{'1000'}; $env:LinkageParameters__ReadCommandTimeoutSeconds=if($env:JORNADA_SCALE_COMMAND_TIMEOUT_SECONDS){$env:JORNADA_SCALE_COMMAND_TIMEOUT_SECONDS}else{'1800'}
  $sw=[Diagnostics.Stopwatch]::StartNew(); dotnet run --project src/Jornada.Linkage.Parameters.Worker --configuration Release --no-build; if($LASTEXITCODE-ne 0){throw 'GENERATE_DRAFT falhou'}; $sw.Stop(); $paramMs=$sw.ElapsedMilliseconds
  $model=[int](Scalar "SELECT TOP(1) versao FROM identidade.modelo_linkage WHERE status='RASCUNHO' ORDER BY versao DESC;")
  $modelId=Scalar "SELECT CONVERT(nvarchar(36),modelo_id) FROM identidade.modelo_linkage WHERE versao=$model;"
  if($modelId -notmatch '^[0-9a-fA-F-]{36}$'){throw 'modelo_id inválido para evidência de escala.'}
  $env:LinkageParameters__Operation='VALIDATE'; $env:LinkageParameters__TargetVersion="$model"; dotnet run --project src/Jornada.Linkage.Parameters.Worker --configuration Release --no-build; if($LASTEXITCODE-ne 0){throw 'VALIDATE falhou'}
  $env:LinkageParameters__Operation='ACTIVATE'; dotnet run --project src/Jornada.Linkage.Parameters.Worker --configuration Release --no-build; if($LASTEXITCODE-ne 0){throw 'ACTIVATE falhou'}
  $corr=[guid]::NewGuid(); $sw=[Diagnostics.Stopwatch]::StartNew(); dotnet run --project src/Jornada.Linkage.Runner --configuration Release --no-build -- --mode MODEL_VALIDATION --model-version $model --max-records $pending --batch-size $batch --max-parallelism $parallel --publish false --requested-by V373_SCALE_HARNESS --reason $Profile --correlation-id $corr; if($LASTEXITCODE-ne 0){throw 'Runner falhou'}; $sw.Stop(); $runnerMs=$sw.ElapsedMilliseconds
} finally { Pop-Location }

$row=(Scalar "SELECT CONCAT(status,'|',registros_elegiveis,'|',avaliados,'|',resolvidos,'|',nao_resolvidos,'|',conflitos,'|',sem_candidato_no_bloco) FROM identidade.linkage_run WHERE correlation_id='$corr';").Split('|')
$runtimeScopeJson=Scalar "SELECT escopo_json FROM identidade.linkage_run WHERE correlation_id='$corr';"
if([string]::IsNullOrWhiteSpace($runtimeScopeJson)){throw 'escopo_json do linkage_run ausente.'}
$runtimeScope=$runtimeScopeJson | ConvertFrom-Json
$blockingPressureJson=Scalar "DECLARE @ruleset uniqueidentifier=(SELECT ruleset_id FROM identidade.linkage_ruleset WHERE modelo_id='$modelId'); SELECT (SELECT (SELECT COUNT(*) FROM identidade.linkage_ruleset_passe WHERE ruleset_id=@ruleset) AS ruleSetPassCount, (SELECT COUNT_BIG(*) FROM identidade.blocking_chave WHERE vigencia_fim IS NULL) AS blockingRows, (SELECT COUNT_BIG(*) FROM (SELECT atributo,valor_normalizado FROM identidade.blocking_chave WHERE vigencia_fim IS NULL GROUP BY atributo,valor_normalizado) d) AS distinctKeys, (SELECT ISNULL(MAX(people_per_key),0) FROM (SELECT COUNT_BIG(DISTINCT pessoa_uuid) people_per_key FROM identidade.blocking_chave WHERE vigencia_fim IS NULL GROUP BY atributo,valor_normalizado) q) AS maxPeoplePerKey, JSON_QUERY((SELECT a.atributo AS attribute, COUNT_BIG(*) AS rows, COUNT_BIG(DISTINCT a.valor_normalizado) AS distinctValues, (SELECT ISNULL(MAX(people_per_value),0) FROM (SELECT COUNT_BIG(DISTINCT b.pessoa_uuid) people_per_value FROM identidade.blocking_chave b WHERE b.vigencia_fim IS NULL AND b.atributo=a.atributo GROUP BY b.valor_normalizado) z) AS maxPeoplePerValue FROM identidade.blocking_chave a WHERE a.vigencia_fim IS NULL GROUP BY a.atributo FOR JSON PATH)) AS attributes FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);"
if([string]::IsNullOrWhiteSpace($blockingPressureJson)){throw 'métricas de pressão de blocking ausentes.'}
$blockingPressure=$blockingPressureJson | ConvertFrom-Json
$exclusiveProbe=Probe-Lock 'Jornada.Pipeline.ExclusiveRequest' $lockHolderDelayMs
$corpusProbe=Probe-Lock 'Jornada.Pipeline.Corpus' $lockHolderDelayMs

$gitCommitSha=((& git -C $Root rev-parse HEAD) | Select-Object -Last 1).Trim().ToLowerInvariant()
if($LASTEXITCODE -ne 0 -or $gitCommitSha -notmatch '^[0-9a-f]{40}$'){throw 'SHA Git inválido para evidência de escala.'}
$outDir=Join-Path $Root '.local/performance'; New-Item -ItemType Directory -Force $outDir|Out-Null
$out=Join-Path $outDir ("scale-{0}-{1}.json" -f $Profile,(Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ'))
$report=[ordered]@{
  reportVersion='LINKAGE_SCALE_EVIDENCE_V1';gitCommitSha=$gitCommitSha;profile=$Profile;seed=$seed;collisionModulo=$collisionModulo;birthShiftModulo=$birthShiftModulo;goldPeople=$people;pairedPeople=$paired;pendingWithoutCpf=$pending;trainingSampleSize=$sample;trainingPoolSize=$pool;modelVersion=$model;runtimeScope=$runtimeScope;parametersGenerateMilliseconds=$paramMs;runnerMilliseconds=$runnerMs;blockingPressure=$blockingPressure;
  coordinationProbe=[ordered]@{holderDelayMilliseconds=$lockHolderDelayMs;exclusiveRequest=$exclusiveProbe;corpus=$corpusProbe};
  runner=[ordered]@{status=$row[0];eligible=[int64]$row[1];evaluated=[int64]$row[2];resolved=[int64]$row[3];unresolved=[int64]$row[4];conflicts=[int64]$row[5];noCandidateInBirthDateBlock=[int64]$row[6]};correlationId="$corr";generatedAtUtc=(Get-Date).ToUniversalTime().ToString('o')
} | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText($out, $report + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
$latest=Join-Path $outDir ("scale-{0}-latest.json" -f $Profile); Copy-Item $out $latest -Force
Push-Location $Root
try {
  $performanceArgs = @($Python3.Prefix) + @('scripts/performance-evidence-gate.py', $out, '--minimum-eligible', '1', '--baseline', 'config/hml/performance-baseline.json', '--summary', (Join-Path $outDir ("scale-{0}-validation.json" -f $Profile)))
  & $Python3.Exe @performanceArgs; if($LASTEXITCODE-ne 0){throw 'evidência de escala inválida'}
  $observabilityArgs = @($Python3.Prefix) + @('scripts/scale-observability-evidence-gate.py', $out)
  & $Python3.Exe @observabilityArgs; if($LASTEXITCODE-ne 0){throw 'evidência de observabilidade de escala inválida'}
} finally { Pop-Location }
Write-Host "Scale harness concluído: $out"; Get-Content $out
