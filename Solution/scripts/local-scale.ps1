param([ValidateSet('smoke','medium','million','custom')][string]$Profile='smoke')
$ErrorActionPreference='Stop'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
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
$seed=if($env:JORNADA_SCALE_SEED){[int]$env:JORNADA_SCALE_SEED}else{355}; $collisionModulo=if($env:JORNADA_SCALE_COLLISION_MODULO){[int]$env:JORNADA_SCALE_COLLISION_MODULO}else{37}; $birthShiftModulo=if($env:JORNADA_SCALE_BIRTH_SHIFT_MODULO){[int]$env:JORNADA_SCALE_BIRTH_SHIFT_MODULO}else{29}; $parallel=if($env:JORNADA_SCALE_PARALLELISM){[int]$env:JORNADA_SCALE_PARALLELISM}else{4}; $batch=if($env:JORNADA_SCALE_BATCH_SIZE){[int]$env:JORNADA_SCALE_BATCH_SIZE}else{10000}
& (Join-Path $PSScriptRoot 'local-db.ps1') -Action reset
$vars=@{}; Get-Content (Join-Path $Root '.env') | % { $l=$_.Trim(); if($l -and -not $l.StartsWith('#') -and $l.Contains('=')){ $p=$l.Split('=',2); $vars[$p[0].Trim()]=$p[1] } }
$port=if($vars['JORNADA_SQL_PORT']){$vars['JORNADA_SQL_PORT']}else{'14333'}; $db=if($vars['JORNADA_SQL_DATABASE']){$vars['JORNADA_SQL_DATABASE']}else{'JornadaLocal'}; $pwd=$vars['JORNADA_SQL_SA_PASSWORD']
function SqlCmd {
    param([Parameter(Mandatory=$true)][string[]]$SqlCmdArgs)
    Push-Location $Root
    try {
        & docker compose --env-file .env exec -T -e "SQLCMDPASSWORD=$pwd" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b @SqlCmdArgs
        if($LASTEXITCODE -ne 0){throw 'sqlcmd falhou.'}
    }
    finally { Pop-Location }
}
function Scalar([string]$Query){ Push-Location $Root; try { $o = (& docker compose --env-file .env exec -T -e "SQLCMDPASSWORD=$pwd" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d $db -h -1 -W -y 0 -Q "SET NOCOUNT ON; $Query"); if($LASTEXITCODE -ne 0){throw 'sqlcmd falhou.'}; return ($o | ? { $_.Trim() } | Select-Object -Last 1).Trim() } finally { Pop-Location } }
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
  $env:LinkageParameters__Operation='VALIDATE'; $env:LinkageParameters__TargetVersion="$model"; dotnet run --project src/Jornada.Linkage.Parameters.Worker --configuration Release --no-build; if($LASTEXITCODE-ne 0){throw 'VALIDATE falhou'}
  $env:LinkageParameters__Operation='ACTIVATE'; dotnet run --project src/Jornada.Linkage.Parameters.Worker --configuration Release --no-build; if($LASTEXITCODE-ne 0){throw 'ACTIVATE falhou'}
  $corr=[guid]::NewGuid(); $sw=[Diagnostics.Stopwatch]::StartNew(); dotnet run --project src/Jornada.Linkage.Runner --configuration Release --no-build -- --mode MODEL_VALIDATION --model-version $model --max-records $pending --batch-size $batch --max-parallelism $parallel --publish false --requested-by V373_SCALE_HARNESS --reason $Profile --correlation-id $corr; if($LASTEXITCODE-ne 0){throw 'Runner falhou'}; $sw.Stop(); $runnerMs=$sw.ElapsedMilliseconds
} finally { Pop-Location }
$row=(Scalar "SELECT CONCAT(status,'|',registros_elegiveis,'|',avaliados,'|',resolvidos,'|',nao_resolvidos,'|',conflitos,'|',sem_candidato_no_bloco) FROM identidade.linkage_run WHERE correlation_id='$corr';").Split('|')
$runtimeScopeJson=Scalar "SELECT escopo_json FROM identidade.linkage_run WHERE correlation_id='$corr';"
if([string]::IsNullOrWhiteSpace($runtimeScopeJson)){throw 'escopo_json do linkage_run ausente.'}
$runtimeScope=$runtimeScopeJson | ConvertFrom-Json
$gitCommitSha=((& git -C $Root rev-parse HEAD) | Select-Object -Last 1).Trim().ToLowerInvariant()
if($LASTEXITCODE -ne 0 -or $gitCommitSha -notmatch '^[0-9a-f]{40}$'){throw 'SHA Git inválido para evidência de escala.'}
$outDir=Join-Path $Root '.local/performance'; New-Item -ItemType Directory -Force $outDir|Out-Null; $out=Join-Path $outDir ("scale-{0}-{1}.json" -f $Profile,(Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ'))
$report=[ordered]@{reportVersion='LINKAGE_SCALE_EVIDENCE_V1';gitCommitSha=$gitCommitSha;profile=$Profile;seed=$seed;collisionModulo=$collisionModulo;birthShiftModulo=$birthShiftModulo;goldPeople=$people;pairedPeople=$paired;pendingWithoutCpf=$pending;trainingSampleSize=$sample;trainingPoolSize=$pool;modelVersion=$model;runtimeScope=$runtimeScope;parametersGenerateMilliseconds=$paramMs;runnerMilliseconds=$runnerMs;runner=[ordered]@{status=$row[0];eligible=[int64]$row[1];evaluated=[int64]$row[2];resolved=[int64]$row[3];unresolved=[int64]$row[4];conflicts=[int64]$row[5];noCandidateInBirthDateBlock=[int64]$row[6]};correlationId="$corr";generatedAtUtc=(Get-Date).ToUniversalTime().ToString('o')} | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText($out, $report + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
$latest=Join-Path $outDir ("scale-{0}-latest.json" -f $Profile); Copy-Item $out $latest -Force
Push-Location $Root; try { python3 scripts/performance-evidence-gate.py $out --minimum-eligible 1 --baseline config/hml/performance-baseline.json --summary (Join-Path $outDir ("scale-{0}-validation.json" -f $Profile)); if($LASTEXITCODE-ne 0){throw 'evidência de escala inválida'} } finally { Pop-Location }
Write-Host "Scale harness concluído: $out"; Get-Content $out
