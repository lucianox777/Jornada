param([ValidateSet('smoke','medium','million','custom')][string]$Profile='smoke')
$ErrorActionPreference='Stop'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$LocalDbScript=(Join-Path $PSScriptRoot 'local-db.ps1')

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
$pendingSince=if($env:JORNADA_SCALE_PENDING_SINCE){$env:JORNADA_SCALE_PENDING_SINCE}else{'2026-08-31T01:00:00Z'}
$blockingAuditLabelCount=if($env:JORNADA_SCALE_BLOCKING_AUDIT_LABEL_COUNT){[int]$env:JORNADA_SCALE_BLOCKING_AUDIT_LABEL_COUNT}else{[int][Math]::Min([int64]200,[int64]$pending)}
if($blockingAuditLabelCount -le 0){throw 'JORNADA_SCALE_BLOCKING_AUDIT_LABEL_COUNT deve ser > 0.'}
if($blockingAuditLabelCount -gt [int64]$pending){$blockingAuditLabelCount=[int]$pending}

# O harness de escala é dono da massa SCALE. O reset prepara apenas schema+seed;
# depois o próprio harness gera o volume solicitado pelo perfil e fecha o backfill.
& $LocalDbScript -Action reset -NoSyntheticCorpus
$vars=@{}; Get-Content (Join-Path $Root '.env') | % { $l=$_.Trim(); if($l -and -not $l.StartsWith('#') -and $l.Contains('=')){ $p=$l.Split('=',2); $vars[$p[0].Trim()]=$p[1] } }
$port=if($vars['JORNADA_SQL_PORT']){$vars['JORNADA_SQL_PORT']}else{'14333'}
$db=if($vars['JORNADA_SQL_DATABASE']){$vars['JORNADA_SQL_DATABASE']}else{'JornadaLocal'}
$sqlPassword=$vars['JORNADA_SQL_SA_PASSWORD']

function SqlCmd {
    param([Parameter(Mandatory=$true)][string[]]$SqlCmdArgs)
    Push-Location $Root
    try {
        & docker compose --env-file .env exec -T -e "SQLCMDPASSWORD=$sqlPassword" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b @SqlCmdArgs
        if($LASTEXITCODE -ne 0){throw 'sqlcmd falhou.'}
    }
    finally { Pop-Location }
}
function Scalar([string]$Query){
    Push-Location $Root
    try {
        $o = (& docker compose --env-file .env exec -T -e "SQLCMDPASSWORD=$sqlPassword" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d $db -y 0 -w 65535 -Q "SET NOCOUNT ON; $Query")
        if($LASTEXITCODE -ne 0){throw 'sqlcmd falhou.'}
        return ($o | ? { $_.Trim() } | Select-Object -Last 1).Trim()
    }
    finally { Pop-Location }
}
function QueryLines([string]$Query){
    Push-Location $Root
    try {
        $o = @(& docker compose --env-file .env exec -T -e "SQLCMDPASSWORD=$sqlPassword" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d $db -W -h -1 -y 0 -w 65535 -Q "SET NOCOUNT ON; $Query")
        if($LASTEXITCODE -ne 0){throw 'sqlcmd falhou.'}
        return @($o | ? { -not [string]::IsNullOrWhiteSpace($_) } | % { $_.Trim() })
    }
    finally { Pop-Location }
}

SqlCmd -SqlCmdArgs @('-d',$db,'-v',"SCALE_PEOPLE=$people","SCALE_PAIRED=$paired","SCALE_PENDING=$pending","SCALE_SEED=$seed","SCALE_COLLISION_MODULO=$collisionModulo","SCALE_BIRTH_SHIFT_MODULO=$birthShiftModulo",'-i','/workspace/database/Jornada_Dev_SyntheticScale.sql')
SqlCmd -SqlCmdArgs @('-d',$db,'-v',"SCALE_PEOPLE=$people","SCALE_SEED=$seed","SCALE_COLLISION_MODULO=$collisionModulo",'-i','/workspace/database/Jornada_Dev_SyntheticScale_Diversify.sql')
& $LocalDbScript -Action backfill
$conn="Server=localhost,$port;Database=$db;User Id=sa;Password=$sqlPassword;TrustServerCertificate=true;Encrypt=false"
$outDir=Join-Path $Root '.local/performance'; New-Item -ItemType Directory -Force $outDir|Out-Null
$blockingAuditLabelsPath=Join-Path $outDir ("scale-{0}-blocking-pass-labels.csv" -f $Profile)
$blockingAuditOutputPath=Join-Path $outDir ("scale-{0}-blocking-pass-audit.json" -f $Profile)
$blockingAuditSummaryPath=Join-Path $outDir ("scale-{0}-blocking-pass-validation.json" -f $Profile)
$previousDotnetEnvironment=$env:DOTNET_ENVIRONMENT
Push-Location $Root
try {
  # Este é um harness local/DEV. Calibrador, Runner e rebuild devem observar o mesmo ambiente
  # do cluster local para que a evidência seja comparável e não dependa do default Production.
  $env:DOTNET_ENVIRONMENT='Development'
  if($env:JORNADA_LOCKED_RESTORE -eq 'true'){ dotnet restore Jornada.sln --locked-mode } else { dotnet restore Jornada.sln }; if($LASTEXITCODE-ne 0){throw 'restore falhou'}
  dotnet build Jornada.sln --configuration Release --no-restore -warnaserror; if($LASTEXITCODE-ne 0){throw 'build falhou'}
  $env:ConnectionStrings__Jornada=$conn; $env:PipelineCoordination__HeartbeatSeconds='2'; $env:PipelineCoordination__ExclusiveIntentTimeoutSeconds='5'

  Write-Host 'Materializando projeção canônica de blocking da massa SCALE antes da calibração...'
  $previousProcessorOperation=$env:Processor__Operation
  try {
    $env:Processor__Operation='REBUILD_LOCAL_BLOCKING'
    dotnet run --project src/Jornada.Processor.Worker --configuration Release --no-build
    if($LASTEXITCODE-ne 0){throw 'REBUILD_LOCAL_BLOCKING falhou'}
  }
  finally {
    $env:Processor__Operation=$previousProcessorOperation
  }
  $projectedScalePeople=[int64](Scalar "SELECT COUNT_BIG(*) FROM (SELECT DISTINCT vc.pessoa_uuid FROM silver.pessoa_observacao po JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id WHERE po.codigo_pessoa_origem LIKE N'SCALE-%' AND vc.status='RESOLVIDO' AND vc.pessoa_uuid IS NOT NULL AND EXISTS (SELECT 1 FROM identidade.blocking_chave bc WHERE bc.pessoa_uuid=vc.pessoa_uuid AND bc.vigencia_fim IS NULL)) projected;")
  if($projectedScalePeople -ne [int64]$people){throw "Projeção de blocking não materializada para toda a massa SCALE: projetadas=$projectedScalePeople; esperadas=$people."}
  Write-Host "Projeção de blocking validada: $projectedScalePeople Pessoas SCALE com chaves correntes."

  $env:LinkageParameters__Operation='GENERATE_DRAFT'; $env:LinkageParameters__RunOnce='true'; $env:LinkageParameters__TrainingSampleSize="$sample"; $env:LinkageParameters__TrainingSamplePoolSize="$pool"; $env:LinkageParameters__MinimumIndependentMatchedPairs=if($env:JORNADA_SCALE_MIN_MATCHED_PAIRS){$env:JORNADA_SCALE_MIN_MATCHED_PAIRS}else{'1000'}; $env:LinkageParameters__ReadCommandTimeoutSeconds=if($env:JORNADA_SCALE_COMMAND_TIMEOUT_SECONDS){$env:JORNADA_SCALE_COMMAND_TIMEOUT_SECONDS}else{'1800'}
  $sw=[Diagnostics.Stopwatch]::StartNew(); dotnet run --project src/Jornada.Linkage.Parameters.Worker --configuration Release --no-build; if($LASTEXITCODE-ne 0){throw 'GENERATE_DRAFT falhou'}; $sw.Stop(); $paramMs=$sw.ElapsedMilliseconds
  $model=[int](Scalar "SELECT TOP(1) versao FROM identidade.modelo_linkage WHERE status='RASCUNHO' ORDER BY versao DESC;")
  $modelId=Scalar "SELECT CONVERT(nvarchar(36),modelo_id) FROM identidade.modelo_linkage WHERE versao=$model;"
  if($modelId -notmatch '^[0-9a-fA-F-]{36}$'){throw 'modelo_id inválido para evidência de escala.'}
  $env:LinkageParameters__Operation='VALIDATE'; $env:LinkageParameters__TargetVersion="$model"; dotnet run --project src/Jornada.Linkage.Parameters.Worker --configuration Release --no-build; if($LASTEXITCODE-ne 0){throw 'VALIDATE falhou'}
  $env:LinkageParameters__Operation='ACTIVATE'; dotnet run --project src/Jornada.Linkage.Parameters.Worker --configuration Release --no-build; if($LASTEXITCODE-ne 0){throw 'ACTIVATE falhou'}

  $blockingLabelRows=@(QueryLines "WITH pend AS (SELECT TOP ($blockingAuditLabelCount) po.pessoa_observacao_id,po.codigo_pessoa_origem,TRY_CONVERT(bigint,RIGHT(po.codigo_pessoa_origem,10)) AS n FROM silver.pessoa_observacao po WHERE po.cpf IS NULL AND po.codigo_pessoa_origem LIKE 'SCALE-PEND-%' AND TRY_CONVERT(bigint,RIGHT(po.codigo_pessoa_origem,10)) IS NOT NULL ORDER BY HASHBYTES('SHA2_256',CONCAT('BLOCKING-AUDIT|',$seed,'|',po.codigo_pessoa_origem)),po.codigo_pessoa_origem) SELECT CONCAT(pessoa_observacao_id,',',CONVERT(varchar(36),CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('JORNADA-V355-',$seed,'-P-',((n-1)%$people)+1))))) FROM pend ORDER BY HASHBYTES('SHA2_256',CONCAT('BLOCKING-AUDIT|',$seed,'|',codigo_pessoa_origem)),codigo_pessoa_origem;")
  if($blockingLabelRows.Count -ne $blockingAuditLabelCount){throw "Amostra de blocking por passe incompleta: obtidos=$($blockingLabelRows.Count); esperados=$blockingAuditLabelCount."}
  $blockingLabelLines=@('pessoa_observacao_id,pessoa_uuid_verdade') + $blockingLabelRows
  [IO.File]::WriteAllLines($blockingAuditLabelsPath,$blockingLabelLines,[Text.UTF8Encoding]::new($false))
  dotnet run --project src/Jornada.Linkage.Runner --configuration Release --no-build -- --blocking-pass-audit-labels $blockingAuditLabelsPath --blocking-pass-audit-output $blockingAuditOutputPath --ProbabilisticLinkage:CommandTimeoutSeconds 300
  if($LASTEXITCODE-ne 0){throw 'auditoria de blocking por passe falhou'}
  $blockingGateArgs=@($Python3.Prefix) + @('scripts/blocking-pass-evidence-gate.py',$blockingAuditOutputPath,'--gold-population',"$people",'--policy','config/hml/blocking-fanout-policy.json','--expected-sample-size',"$blockingAuditLabelCount",'--expected-model-version',"$model",'--summary',$blockingAuditSummaryPath)
  & $Python3.Exe @blockingGateArgs; if($LASTEXITCODE-ne 0){throw 'gate de fan-out por passe falhou'}

  $corr=[guid]::NewGuid(); $sw=[Diagnostics.Stopwatch]::StartNew(); dotnet run --project src/Jornada.Linkage.Runner --configuration Release --no-build -- --mode MODEL_VALIDATION --model-version $model --since $pendingSince --max-records $pending --batch-size $batch --max-parallelism $parallel --publish false --requested-by V373_SCALE_HARNESS --reason $Profile --correlation-id $corr; if($LASTEXITCODE-ne 0){throw 'Runner falhou'}; $sw.Stop(); $runnerMs=$sw.ElapsedMilliseconds
} finally {
  $env:DOTNET_ENVIRONMENT=$previousDotnetEnvironment
  Pop-Location
}

$row=(Scalar "SELECT CONCAT(status,'|',registros_elegiveis,'|',avaliados,'|',resolvidos,'|',nao_resolvidos,'|',conflitos,'|',sem_candidato_no_bloco) FROM identidade.linkage_run WHERE correlation_id='$corr';").Split('|')
$runtimeScopeJson=Scalar "SELECT escopo_json FROM identidade.linkage_run WHERE correlation_id='$corr';"
if([string]::IsNullOrWhiteSpace($runtimeScopeJson)){throw 'escopo_json do linkage_run ausente.'}
$runtimeScope=$runtimeScopeJson | ConvertFrom-Json
$decisionQualityJson=Scalar "DECLARE @run uniqueidentifier=(SELECT linkage_run_id FROM identidade.linkage_run WHERE correlation_id='$corr'); WITH truth AS (SELECT r.*,po.codigo_pessoa_origem,tv.pessoa_uuid AS truth_uuid FROM identidade.linkage_resultado r JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id JOIN silver.pessoa_observacao tpo ON tpo.codigo_pessoa_origem=REPLACE(po.codigo_pessoa_origem,'SCALE-PEND-','SCALE-SEHAB-') JOIN ref.gestor tg ON tg.gestor_id=tpo.gestor_id AND tg.codigo='SEHAB' JOIN identidade.v_vinculo_corrente tv ON tv.pessoa_observacao_id=tpo.pessoa_observacao_id AND tv.status='RESOLVIDO' WHERE r.linkage_run_id=@run AND po.codigo_pessoa_origem LIKE 'SCALE-PEND-%') SELECT (SELECT COUNT_BIG(*) AS totalScale,SUM(CASE WHEN status='RESOLVIDO' THEN 1 ELSE 0 END) AS resolved,SUM(CASE WHEN status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END) AS resolvedCorrect,SUM(CASE WHEN status='RESOLVIDO' AND (pessoa_uuid_resolvido IS NULL OR pessoa_uuid_resolvido<>truth_uuid) THEN 1 ELSE 0 END) AS falsePositives,SUM(CASE WHEN status='CONFLITO' THEN 1 ELSE 0 END) AS conflicts,SUM(CASE WHEN status='CONFLITO' AND (melhor_candidato_uuid=truth_uuid OR segundo_candidato_uuid=truth_uuid) THEN 1 ELSE 0 END) AS conflictsTruthTop2,SUM(CASE WHEN status='NAO_RESOLVIDO' THEN 1 ELSE 0 END) AS unresolved,SUM(CASE WHEN status='NAO_RESOLVIDO' AND NOT(segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001) AND melhor_candidato_uuid=truth_uuid THEN 1 ELSE 0 END) AS unresolvedTruthFirstWithoutTie,SUM(CASE WHEN status='NAO_RESOLVIDO' AND segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001 AND (melhor_candidato_uuid=truth_uuid OR segundo_candidato_uuid=truth_uuid) THEN 1 ELSE 0 END) AS unresolvedTruthInTop2Tie,SUM(CASE WHEN status='NAO_RESOLVIDO' AND NOT(segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001) AND segundo_candidato_uuid=truth_uuid AND (melhor_candidato_uuid IS NULL OR melhor_candidato_uuid<>truth_uuid) THEN 1 ELSE 0 END) AS unresolvedTruthSecondWithoutTie,SUM(CASE WHEN status='NAO_RESOLVIDO' AND ISNULL(melhor_candidato_uuid,'00000000-0000-0000-0000-000000000000')<>truth_uuid AND ISNULL(segundo_candidato_uuid,'00000000-0000-0000-0000-000000000000')<>truth_uuid THEN 1 ELSE 0 END) AS unresolvedTruthOutsideTop2,SUM(CASE WHEN status='NAO_RESOLVIDO' AND segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001 THEN 1 ELSE 0 END) AS unresolvedTieTop2,CAST(100.0*SUM(CASE WHEN status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END)/NULLIF(SUM(CASE WHEN status='RESOLVIDO' THEN 1 ELSE 0 END),0) AS decimal(9,4)) AS ppvPct,CAST(100.0*SUM(CASE WHEN status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END)/NULLIF(COUNT_BIG(*),0) AS decimal(9,4)) AS sensitivityPct FROM truth FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);"
if([string]::IsNullOrWhiteSpace($decisionQualityJson)){throw 'qualidade contra ground truth SCALE ausente.'}
$decisionQuality=$decisionQualityJson | ConvertFrom-Json
$blockingPressureJson=Scalar "DECLARE @ruleset uniqueidentifier=(SELECT ruleset_id FROM identidade.linkage_ruleset WHERE modelo_id='$modelId'); SELECT (SELECT (SELECT COUNT(*) FROM identidade.linkage_ruleset_passe WHERE ruleset_id=@ruleset) AS ruleSetPassCount, (SELECT COUNT_BIG(*) FROM identidade.blocking_chave WHERE vigencia_fim IS NULL) AS blockingRows, (SELECT COUNT_BIG(*) FROM (SELECT atributo,valor_normalizado FROM identidade.blocking_chave WHERE vigencia_fim IS NULL GROUP BY atributo,valor_normalizado) d) AS distinctKeys, (SELECT ISNULL(MAX(people_per_key),0) FROM (SELECT COUNT_BIG(DISTINCT pessoa_uuid) people_per_key FROM identidade.blocking_chave WHERE vigencia_fim IS NULL GROUP BY atributo,valor_normalizado) q) AS maxPeoplePerKey, JSON_QUERY((SELECT a.atributo AS attribute, COUNT_BIG(*) AS rows, COUNT_BIG(DISTINCT a.valor_normalizado) AS distinctValues, (SELECT ISNULL(MAX(people_per_value),0) FROM (SELECT COUNT_BIG(DISTINCT b.pessoa_uuid) people_per_value FROM identidade.blocking_chave b WHERE b.vigencia_fim IS NULL AND b.atributo=a.atributo GROUP BY b.valor_normalizado) z) AS maxPeoplePerValue FROM identidade.blocking_chave a WHERE a.vigencia_fim IS NULL GROUP BY a.atributo FOR JSON PATH)) AS attributes FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);"
if([string]::IsNullOrWhiteSpace($blockingPressureJson)){throw 'métricas de pressão de blocking ausentes.'}
$blockingPressure=$blockingPressureJson | ConvertFrom-Json
$blockingPassAudit=Get-Content -Raw -Encoding UTF8 $blockingAuditOutputPath | ConvertFrom-Json

Push-Location $Root
try {
  $coordinationArgs = @($Python3.Prefix) + @('scripts/coordination-lock-probe.py', '--root', $Root, '--database', $db, '--delay-ms', "$lockHolderDelayMs")
  $coordinationProbeJson = (& $Python3.Exe @coordinationArgs | Select-Object -Last 1)
  if($LASTEXITCODE-ne 0 -or [string]::IsNullOrWhiteSpace($coordinationProbeJson)){throw 'probe sincronizado de coordenação falhou'}
  $coordinationProbe=$coordinationProbeJson | ConvertFrom-Json
} finally { Pop-Location }

$gitCommitSha=((& git -C $Root rev-parse HEAD) | Select-Object -Last 1).Trim().ToLowerInvariant()
if($LASTEXITCODE -ne 0 -or $gitCommitSha -notmatch '^[0-9a-f]{40}$'){throw 'SHA Git inválido para evidência de escala.'}
$out=Join-Path $outDir ("scale-{0}-{1}.json" -f $Profile,(Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ'))
$report=[ordered]@{
  reportVersion='LINKAGE_SCALE_EVIDENCE_V1';gitCommitSha=$gitCommitSha;profile=$Profile;seed=$seed;collisionModulo=$collisionModulo;birthShiftModulo=$birthShiftModulo;goldPeople=$people;pairedPeople=$paired;pendingWithoutCpf=$pending;trainingSampleSize=$sample;trainingPoolSize=$pool;modelVersion=$model;runtimeScope=$runtimeScope;parametersGenerateMilliseconds=$paramMs;runnerMilliseconds=$runnerMs;blockingPressure=$blockingPressure;blockingPassAuditLabelCount=$blockingAuditLabelCount;blockingPassAudit=$blockingPassAudit;decisionQuality=$decisionQuality;
  coordinationProbe=$coordinationProbe;
  runner=[ordered]@{status=$row[0];eligible=[int64]$row[1];evaluated=[int64]$row[2];resolved=[int64]$row[3];unresolved=[int64]$row[4];conflicts=[int64]$row[5];noCandidateInBirthDateBlock=[int64]$row[6]};correlationId="$corr";generatedAtUtc=(Get-Date).ToUniversalTime().ToString('o')
} | ConvertTo-Json -Depth 12
[IO.File]::WriteAllText($out, $report + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
$latest=Join-Path $outDir ("scale-{0}-latest.json" -f $Profile); Copy-Item $out $latest -Force
Push-Location $Root
try {
  $performanceArgs = @($Python3.Prefix) + @('scripts/performance-evidence-gate.py', $out, '--minimum-eligible', '1', '--baseline', 'config/hml/performance-baseline.json', '--summary', (Join-Path $outDir ("scale-{0}-validation.json" -f $Profile)))
  & $Python3.Exe @performanceArgs; if($LASTEXITCODE-ne 0){throw 'evidência de escala inválida'}
  $observabilityArgs = @($Python3.Prefix) + @('scripts/scale-observability-evidence-gate.py', $out)
  & $Python3.Exe @observabilityArgs; if($LASTEXITCODE-ne 0){throw 'evidência de observabilidade/qualidade de escala inválida'}
} finally { Pop-Location }
Write-Host "Scale harness concluído: $out"; Get-Content $out
