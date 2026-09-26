param([ValidateSet('smoke','medium','million','custom')][string]$Profile='smoke')
$ErrorActionPreference='Stop'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$DefaultEnvFile=Join-Path $Root '.env'
$EnvFile=if([string]::IsNullOrWhiteSpace($env:JORNADA_LOCAL_ENV_FILE)){$DefaultEnvFile}else{[IO.Path]::GetFullPath($env:JORNADA_LOCAL_ENV_FILE)}
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
$blockingAuditLabelCount=if($env:JORNADA_SCALE_BLOCKING_AUDIT_LABEL_COUNT){[int]$env:JORNADA_SCALE_BLOCKING_AUDIT_LABEL_COUNT}else{[int][Math]::Min(1000,[int64]$pending)}
if($blockingAuditLabelCount -le 0){throw 'JORNADA_SCALE_BLOCKING_AUDIT_LABEL_COUNT deve ser > 0.'}
if($blockingAuditLabelCount -gt $pending){$blockingAuditLabelCount=[int]$pending}
$outDir=Join-Path $Root '.local/performance'; New-Item -ItemType Directory -Force $outDir|Out-Null
$blockingAuditLabelsPath=Join-Path $outDir ("scale-{0}-blocking-labels.csv" -f $Profile)
$blockingAuditPath=Join-Path $outDir ("scale-{0}-blocking-pass-audit.json" -f $Profile)

# O harness de escala é dono da massa SCALE. O reset prepara apenas schema+seed;
# depois o próprio harness gera o volume solicitado pelo perfil e fecha o backfill.
& $LocalDbScript -Action reset -NoSyntheticCorpus
$vars=@{}; Get-Content $EnvFile | % { $l=$_.Trim(); if($l -and -not $l.StartsWith('#') -and $l.Contains('=')){ $p=$l.Split('=',2); $vars[$p[0].Trim()]=$p[1] } }
$port=if($vars['JORNADA_SQL_PORT']){$vars['JORNADA_SQL_PORT']}else{'14333'}
$db=if($vars['JORNADA_SQL_DATABASE']){$vars['JORNADA_SQL_DATABASE']}else{'JornadaLocal'}
$sqlPassword=$vars['JORNADA_SQL_SA_PASSWORD']
$conn="Server=localhost,$port;Database=$db;User Id=sa;Password=$sqlPassword;TrustServerCertificate=true;Encrypt=false"

function SqlCmd {
    param([Parameter(Mandatory=$true)][string[]]$SqlCmdArgs)
    $previousSqlcmdPassword=[Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','Process')
    Push-Location $Root
    try {
        $env:SQLCMDPASSWORD=$sqlPassword
        & docker compose --env-file $EnvFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b @SqlCmdArgs
        if($LASTEXITCODE -ne 0){throw 'sqlcmd falhou.'}
    }
    finally {
        try { Pop-Location }
        finally {
            if($null -eq $previousSqlcmdPassword){
                Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
            } else {
                $env:SQLCMDPASSWORD=$previousSqlcmdPassword
            }
        }
    }
}
function Scalar([string]$Query){
    $previousSqlcmdPassword=[Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','Process')
    Push-Location $Root
    try {
        $env:SQLCMDPASSWORD=$sqlPassword
        $o = (& docker compose --env-file $EnvFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d $db -y 0 -w 65535 -Q "SET NOCOUNT ON; $Query")
        if($LASTEXITCODE -ne 0){throw 'sqlcmd falhou.'}
        return ($o | ? { $_.Trim() } | Select-Object -Last 1).Trim()
    }
    finally {
        try { Pop-Location }
        finally {
            if($null -eq $previousSqlcmdPassword){
                Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
            } else {
                $env:SQLCMDPASSWORD=$previousSqlcmdPassword
            }
        }
    }
}
function QueryLines([string]$Query){
    $previousSqlcmdPassword=[Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','Process')
    Push-Location $Root
    try {
        $env:SQLCMDPASSWORD=$sqlPassword
        $o = @(& docker compose --env-file $EnvFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d $db -W -h -1 -w 65535 -Q "SET NOCOUNT ON; $Query")
        if($LASTEXITCODE -ne 0){throw 'sqlcmd falhou.'}
        return @($o | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }
    finally {
        try { Pop-Location }
        finally {
            if($null -eq $previousSqlcmdPassword){
                Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
            } else {
                $env:SQLCMDPASSWORD=$previousSqlcmdPassword
            }
        }
    }
}

$previousDotnetEnvironment=$env:DOTNET_ENVIRONMENT
$previousConnection=$env:ConnectionStrings__Jornada
$previousLinkageOperation=$env:LinkageParameters__Operation
$previousSnapshotManifest=$env:NameFrequencySnapshot__ManifestPath
Push-Location $Root
try {
  # Este é um harness local/DEV. Loader, Calibrador, Runner e rebuild devem observar o mesmo ambiente
  # do cluster local para que a evidência seja comparável e não dependa do default Production.
  $env:DOTNET_ENVIRONMENT='Development'
  if($env:JORNADA_LOCKED_RESTORE -eq 'true'){ dotnet restore Jornada.sln --locked-mode } else { dotnet restore Jornada.sln }; if($LASTEXITCODE-ne 0){throw 'restore falhou'}
  dotnet build Jornada.sln --configuration Release --no-restore -warnaserror; if($LASTEXITCODE-ne 0){throw 'build falhou'}
  $env:ConnectionStrings__Jornada=$conn; $env:PipelineCoordination__HeartbeatSeconds='2'; $env:PipelineCoordination__ExclusiveIntentTimeoutSeconds='5'

  # O schema 3.70 corrente já contém o contrato de frequências. O snapshot versionado é
  # carregado pelo mesmo loader operacional usado na calibração, sem parser paralelo no SQL.
  $env:LinkageParameters__Operation='LOAD_NAME_FREQUENCY_SNAPSHOT'
  $env:NameFrequencySnapshot__ManifestPath=(Join-Path $Root 'data/reference/ibge-nomes-2022/manifest.json')
  Write-Host 'Carregando referência IBGE canônica para geração da massa SCALE...'
  dotnet run --project src/Jornada.Linkage.Parameters.Worker --configuration Release --no-build
  if($LASTEXITCODE-ne 0){throw 'LOAD_NAME_FREQUENCY_SNAPSHOT falhou'}
  $activeNameReference=Scalar "SELECT TOP(1) codigo FROM ref.frequencia_nome_versao WHERE status=N'ATIVA';"
  if($activeNameReference -ne 'CENSO2022_NOMES_BRASIL_V1'){throw "Referência IBGE ATIVA inesperada após carga: $activeNameReference"}
  Write-Host "Referência de frequências ativa: $activeNameReference"
  # Derived reference is provisioned once, before the synthetic load.
  $env:LinkageParameters__Operation='ENSURE_IBGE_NOMINAL_U_REFERENCE'
  dotnet run --project src/Jornada.Linkage.Parameters.Worker --configuration Release --no-build
  if($LASTEXITCODE-ne 0){throw 'ENSURE_IBGE_NOMINAL_U_REFERENCE falhou'}

  SqlCmd -SqlCmdArgs @('-d',$db,'-v',"SCALE_PEOPLE=$people","SCALE_PAIRED=$paired","SCALE_PENDING=$pending","SCALE_SEED=$seed","SCALE_COLLISION_MODULO=$collisionModulo","SCALE_BIRTH_SHIFT_MODULO=$birthShiftModulo",'-i','/workspace/database/Jornada_Dev_SyntheticScale.sql')
  SqlCmd -SqlCmdArgs @('-d',$db,'-v',"SCALE_PEOPLE=$people","SCALE_SEED=$seed","SCALE_COLLISION_MODULO=$collisionModulo",'-i','/workspace/database/Jornada_Dev_SyntheticScale_Diversify.sql')
  & $LocalDbScript -Action backfill

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

  $conferenceTolerance = Join-Path $Root '.local\linkage-conference-tolerance.scale.json'
  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $conferenceTolerance) | Out-Null
  [ordered]@{
    schemaVersion = 1
    methodVersion = 'JORNADA_IMPLEMENTATION_CONFERENCE_STATE_VECTOR_V1'
    status = 'FROZEN'
    scope = 'SCORER_POLICY_ONLY_STATES_AND_GUARD_INPUTS_PRECOMPUTED_COMPARATORS_OUT_OF_SCOPE'
    maxAbsolutePairLlrDifference = 0.000001
    decisionEquivalence = 'EXACT_FINAL_OPERATIONAL_DECISION'
    primaryGates = @('PAIR_LLR_WITHIN_FROZEN_TOLERANCE','EXACT_FINAL_OPERATIONAL_DECISION')
    diagnosticsOnly = @('SPEARMAN_RANK_CORRELATION','SAME_TOP1','MAX_ABSOLUTE_LOG_ODDS_DIFFERENCE')
    statisticalValidation = 'SEPARATE_ISSUE_31'
    note = 'TEST_ONLY local scale fixture; never a production governance tolerance.'
    toleranceVersion = 'TEST_ONLY_LOCAL_SCALE_V1'
  } | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 -Path $conferenceTolerance

  dotnet run --project src/Jornada.Linkage.Conference --configuration Release --no-build -- --model-id $modelId --tolerance-config $conferenceTolerance --source-revision LOCAL_SCALE_TEST
  if($LASTEXITCODE-ne 0){throw 'CONFERENCIA falhou'}

  $env:LinkageParameters__ConferenceToleranceConfigPath=$conferenceTolerance
  $env:LinkageParameters__Operation='VALIDATE'; $env:LinkageParameters__TargetVersion="$model"; dotnet run --project src/Jornada.Linkage.Parameters.Worker --configuration Release --no-build; if($LASTEXITCODE-ne 0){throw 'VALIDATE falhou'}
  $env:LinkageParameters__Operation='ACTIVATE'; dotnet run --project src/Jornada.Linkage.Parameters.Worker --configuration Release --no-build; if($LASTEXITCODE-ne 0){throw 'ACTIVATE falhou'}

  Write-Host "Auditando fan-out efetivo do ruleset calibrado em $blockingAuditLabelCount observações SCALE..."
  $labelQuery=@"
WITH pend AS (
    SELECT TOP ($blockingAuditLabelCount)
           po.pessoa_observacao_id,
           po.codigo_pessoa_origem,
           TRY_CONVERT(bigint,RIGHT(po.codigo_pessoa_origem,10)) AS n
      FROM silver.pessoa_observacao po
     WHERE po.cpf IS NULL
       AND po.codigo_pessoa_origem LIKE N'SCALE-PEND-%'
       AND TRY_CONVERT(bigint,RIGHT(po.codigo_pessoa_origem,10)) IS NOT NULL
     ORDER BY TRY_CONVERT(bigint,RIGHT(po.codigo_pessoa_origem,10)),po.codigo_pessoa_origem
)
SELECT CONCAT(p.pessoa_observacao_id,',',CONVERT(varchar(36),tv.pessoa_uuid))
  FROM pend p
  JOIN silver.pessoa_observacao tpo
    ON tpo.codigo_pessoa_origem=REPLACE(p.codigo_pessoa_origem,N'SCALE-PEND-',N'SCALE-SEHAB-')
  JOIN ref.gestor tg ON tg.gestor_id=tpo.gestor_id AND tg.codigo=N'SEHAB'
  JOIN identidade.v_vinculo_corrente tv
    ON tv.pessoa_observacao_id=tpo.pessoa_observacao_id
   AND tv.status=N'RESOLVIDO'
   AND tv.pessoa_uuid IS NOT NULL
 ORDER BY p.n,p.codigo_pessoa_origem;
"@
  $labelLines=@('pessoa_observacao_id,pessoa_uuid_verdade') + @(QueryLines $labelQuery)
  if(($labelLines.Count-1) -ne $blockingAuditLabelCount){throw "Rótulos SCALE insuficientes para auditoria de blocking: obtidos=$($labelLines.Count-1); esperados=$blockingAuditLabelCount."}
  [IO.File]::WriteAllLines($blockingAuditLabelsPath,$labelLines,[Text.UTF8Encoding]::new($false))
  dotnet run --project src/Jornada.Linkage.Runner --configuration Release --no-build -- --blocking-pass-audit-labels $blockingAuditLabelsPath --blocking-pass-audit-output $blockingAuditPath --ProbabilisticLinkage:CommandTimeoutSeconds 300
  if($LASTEXITCODE-ne 0){throw 'Auditoria de blocking por passe do SCALE falhou'}
  $fanoutArgs=@($Python3.Prefix)+@('scripts/blocking-pass-fanout-gate.py',$blockingAuditPath,'--population',"$people")
  & $Python3.Exe @fanoutArgs
  if($LASTEXITCODE-ne 0){throw 'Fan-out efetivo do ruleset calibrado excedeu o contrato de escala'}
  $blockingPassPressure=Get-Content -Raw -Encoding UTF8 $blockingAuditPath | ConvertFrom-Json

  $corr=[guid]::NewGuid(); $sw=[Diagnostics.Stopwatch]::StartNew(); dotnet run --project src/Jornada.Linkage.Runner --configuration Release --no-build -- --mode MODEL_VALIDATION --model-version $model --since $pendingSince --max-records $pending --batch-size $batch --max-parallelism $parallel --publish false --requested-by V373_SCALE_HARNESS --reason $Profile --correlation-id $corr; if($LASTEXITCODE-ne 0){throw 'Runner falhou'}; $sw.Stop(); $runnerMs=$sw.ElapsedMilliseconds
} finally {
  $env:DOTNET_ENVIRONMENT=$previousDotnetEnvironment
  $env:ConnectionStrings__Jornada=$previousConnection
  $env:LinkageParameters__Operation=$previousLinkageOperation
  $env:NameFrequencySnapshot__ManifestPath=$previousSnapshotManifest
  Pop-Location
}

$row=(Scalar "SELECT CONCAT(status,'|',registros_elegiveis,'|',avaliados,'|',resolvidos,'|',nao_resolvidos,'|',conflitos,'|',sem_candidato_no_bloco) FROM identidade.linkage_run WHERE correlation_id='$corr';").Split('|')
$runtimeScopeJson=Scalar "SELECT escopo_json FROM identidade.linkage_run WHERE correlation_id='$corr';"
if([string]::IsNullOrWhiteSpace($runtimeScopeJson)){throw 'escopo_json do linkage_run ausente.'}
$runtimeScope=$runtimeScopeJson | ConvertFrom-Json
$decisionQualityQuery=@"
DECLARE @run uniqueidentifier=(SELECT linkage_run_id FROM identidade.linkage_run WHERE correlation_id='$corr');
WITH truth AS (
  SELECT r.*,po.codigo_pessoa_origem,
         TRY_CONVERT(bigint,RIGHT(po.codigo_pessoa_origem,10)) AS scale_n,
         tv.pessoa_uuid AS truth_uuid
  FROM identidade.linkage_resultado r
  JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id
  JOIN silver.pessoa_observacao tpo ON tpo.codigo_pessoa_origem=REPLACE(po.codigo_pessoa_origem,'SCALE-PEND-','SCALE-SEHAB-')
  JOIN ref.gestor tg ON tg.gestor_id=tpo.gestor_id AND tg.codigo='SEHAB'
  JOIN identidade.v_vinculo_corrente tv ON tv.pessoa_observacao_id=tpo.pessoa_observacao_id AND tv.status='RESOLVIDO'
  WHERE r.linkage_run_id=@run AND po.codigo_pessoa_origem LIKE 'SCALE-PEND-%'
)
SELECT (
  SELECT
    COUNT_BIG(*) AS totalScale,
    SUM(CASE WHEN status='RESOLVIDO' THEN 1 ELSE 0 END) AS resolved,
    SUM(CASE WHEN status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END) AS resolvedCorrect,
    SUM(CASE WHEN status='RESOLVIDO' AND (pessoa_uuid_resolvido IS NULL OR pessoa_uuid_resolvido<>truth_uuid) THEN 1 ELSE 0 END) AS falsePositives,
    SUM(CASE WHEN status='CONFLITO' THEN 1 ELSE 0 END) AS conflicts,
    SUM(CASE WHEN status='CONFLITO' AND (melhor_candidato_uuid=truth_uuid OR segundo_candidato_uuid=truth_uuid) THEN 1 ELSE 0 END) AS conflictsTruthTop2,
    SUM(CASE WHEN status='CONFLITO' AND NOT(segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001) AND melhor_candidato_uuid=truth_uuid THEN 1 ELSE 0 END) AS conflictsTruthFirstWithoutTie,
    SUM(CASE WHEN status='CONFLITO' AND segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001 AND (melhor_candidato_uuid=truth_uuid OR segundo_candidato_uuid=truth_uuid) THEN 1 ELSE 0 END) AS conflictsTruthInTop2Tie,
    SUM(CASE WHEN status='CONFLITO' AND NOT(segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001) AND segundo_candidato_uuid=truth_uuid AND (melhor_candidato_uuid IS NULL OR melhor_candidato_uuid<>truth_uuid) THEN 1 ELSE 0 END) AS conflictsTruthSecondWithoutTie,
    SUM(CASE WHEN status='CONFLITO' AND ISNULL(melhor_candidato_uuid,'00000000-0000-0000-0000-000000000000')<>truth_uuid AND ISNULL(segundo_candidato_uuid,'00000000-0000-0000-0000-000000000000')<>truth_uuid THEN 1 ELSE 0 END) AS conflictsTruthOutsideTop2,
    SUM(CASE WHEN status='CONFLITO' AND motivo='DOIS_CANDIDATOS_ACIMA_T_LINKAGE' THEN 1 ELSE 0 END) AS dualThresholdConflicts,
    SUM(CASE WHEN status='CONFLITO' AND score_melhor>=0.9999 THEN 1 ELSE 0 END) AS conflictsBestPosteriorGe9999,
    SUM(CASE WHEN status='CONFLITO' AND score_segundo>=0.999 THEN 1 ELSE 0 END) AS conflictsSecondPosteriorGe999,
    SUM(CASE WHEN status='CONFLITO' AND score_segundo>=0.9999 THEN 1 ELSE 0 END) AS conflictsSecondPosteriorGe9999,
    CAST(MIN(CASE WHEN status='CONFLITO' THEN margem END) AS decimal(30,12)) AS conflictMarginMin,
    CAST(AVG(CASE WHEN status='CONFLITO' THEN CONVERT(decimal(30,12),margem) END) AS decimal(30,12)) AS conflictMarginAvg,
    CAST(MAX(CASE WHEN status='CONFLITO' THEN margem END) AS decimal(30,12)) AS conflictMarginMax,
    SUM(CASE WHEN status='NAO_RESOLVIDO' THEN 1 ELSE 0 END) AS unresolved,
    SUM(CASE WHEN status='NAO_RESOLVIDO' AND NOT(segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001) AND melhor_candidato_uuid=truth_uuid THEN 1 ELSE 0 END) AS unresolvedTruthFirstWithoutTie,
    SUM(CASE WHEN status='NAO_RESOLVIDO' AND segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001 AND (melhor_candidato_uuid=truth_uuid OR segundo_candidato_uuid=truth_uuid) THEN 1 ELSE 0 END) AS unresolvedTruthInTop2Tie,
    SUM(CASE WHEN status='NAO_RESOLVIDO' AND NOT(segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001) AND segundo_candidato_uuid=truth_uuid AND (melhor_candidato_uuid IS NULL OR melhor_candidato_uuid<>truth_uuid) THEN 1 ELSE 0 END) AS unresolvedTruthSecondWithoutTie,
    SUM(CASE WHEN status='NAO_RESOLVIDO' AND ISNULL(melhor_candidato_uuid,'00000000-0000-0000-0000-000000000000')<>truth_uuid AND ISNULL(segundo_candidato_uuid,'00000000-0000-0000-0000-000000000000')<>truth_uuid THEN 1 ELSE 0 END) AS unresolvedTruthOutsideTop2,
    SUM(CASE WHEN status='NAO_RESOLVIDO' AND segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001 THEN 1 ELSE 0 END) AS unresolvedTieTop2,
    SUM(CASE WHEN scale_n%10=0 THEN 1 ELSE 0 END) AS birthOutOfUniverseEveryTenthTotal,
    SUM(CASE WHEN scale_n%10=0 AND status='RESOLVIDO' THEN 1 ELSE 0 END) AS birthOutOfUniverseEveryTenthResolved,
    SUM(CASE WHEN scale_n%10=0 AND status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END) AS birthOutOfUniverseEveryTenthResolvedCorrect,
    SUM(CASE WHEN scale_n%10=0 AND status='CONFLITO' THEN 1 ELSE 0 END) AS birthOutOfUniverseEveryTenthConflicts,
    SUM(CASE WHEN scale_n%10=0 AND status='NAO_RESOLVIDO' THEN 1 ELSE 0 END) AS birthOutOfUniverseEveryTenthUnresolved,
    SUM(CASE WHEN scale_n%10<>0 THEN 1 ELSE 0 END) AS birthInUniverseOtherRowsTotal,
    SUM(CASE WHEN scale_n%10<>0 AND status='RESOLVIDO' THEN 1 ELSE 0 END) AS birthInUniverseOtherRowsResolved,
    SUM(CASE WHEN scale_n%10<>0 AND status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END) AS birthInUniverseOtherRowsResolvedCorrect,
    SUM(CASE WHEN scale_n%10<>0 AND status='CONFLITO' THEN 1 ELSE 0 END) AS birthInUniverseOtherRowsConflicts,
    SUM(CASE WHEN scale_n%10<>0 AND status='NAO_RESOLVIDO' THEN 1 ELSE 0 END) AS birthInUniverseOtherRowsUnresolved,
    CAST(100.0*SUM(CASE WHEN status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END)/NULLIF(SUM(CASE WHEN status='RESOLVIDO' THEN 1 ELSE 0 END),0) AS decimal(9,4)) AS ppvPct,
    CAST(100.0*SUM(CASE WHEN status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END)/NULLIF(COUNT_BIG(*),0) AS decimal(9,4)) AS sensitivityPct,
    CAST(CASE
      WHEN SUM(CASE WHEN status='RESOLVIDO' AND (pessoa_uuid_resolvido IS NULL OR pessoa_uuid_resolvido<>truth_uuid) THEN 1 ELSE 0 END)=0
       AND SUM(CASE WHEN status='RESOLVIDO' THEN 1 ELSE 0 END)>0
      THEN 300.0/SUM(CASE WHEN status='RESOLVIDO' THEN 1 ELSE 0 END)
    END AS decimal(9,4)) AS zeroFpUpper95PctRuleOfThree
  FROM truth
  FOR JSON PATH,WITHOUT_ARRAY_WRAPPER
);
"@
$decisionQualityJson=Scalar $decisionQualityQuery
if([string]::IsNullOrWhiteSpace($decisionQualityJson)){throw 'qualidade contra ground truth SCALE ausente.'}
$decisionQuality=$decisionQualityJson | ConvertFrom-Json
# Limite de FP SCALE: orçamento congelado, NÃO reotimizar sobre este corpus.
$scaleFpBudgetText=Scalar "SELECT CONVERT(int,valor) FROM identidade.parametro_linkage WHERE modelo_id='$modelId' AND nome='FS_DECISION_CALIBRATION_MAX_FP_TEST_BP';"
$scaleFpBudgetBp=0
$scaleFpParsed=[int]::TryParse([string]$scaleFpBudgetText,[ref]$scaleFpBudgetBp)
if(-not $scaleFpParsed -or $scaleFpBudgetBp -lt 0 -or $scaleFpBudgetBp -gt 10000){
  throw 'budget FP congelado ausente/invalido no modelo SCALE'
}
$blockingPressureJson=Scalar "DECLARE @ruleset uniqueidentifier=(SELECT ruleset_id FROM identidade.linkage_ruleset WHERE modelo_id='$modelId'); SELECT (SELECT (SELECT COUNT(*) FROM identidade.linkage_ruleset_passe WHERE ruleset_id=@ruleset) AS ruleSetPassCount, (SELECT COUNT_BIG(*) FROM identidade.blocking_chave WHERE vigencia_fim IS NULL) AS blockingRows, (SELECT COUNT_BIG(*) FROM (SELECT atributo,valor_normalizado FROM identidade.blocking_chave WHERE vigencia_fim IS NULL GROUP BY atributo,valor_normalizado) d) AS distinctKeys, (SELECT ISNULL(MAX(people_per_key),0) FROM (SELECT COUNT_BIG(DISTINCT pessoa_uuid) people_per_key FROM identidade.blocking_chave WHERE vigencia_fim IS NULL GROUP BY atributo,valor_normalizado) q) AS maxPeoplePerKey, JSON_QUERY((SELECT a.atributo AS attribute, COUNT_BIG(*) AS rows, COUNT_BIG(DISTINCT a.valor_normalizado) AS distinctValues, (SELECT ISNULL(MAX(people_per_value),0) FROM (SELECT COUNT_BIG(DISTINCT b.pessoa_uuid) people_per_value FROM identidade.blocking_chave b WHERE b.vigencia_fim IS NULL AND b.atributo=a.atributo GROUP BY b.valor_normalizado) z) AS maxPeoplePerValue FROM identidade.blocking_chave a WHERE a.vigencia_fim IS NULL GROUP BY a.atributo FOR JSON PATH)) AS attributes FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);"
if([string]::IsNullOrWhiteSpace($blockingPressureJson)){throw 'métricas de pressão de blocking ausentes.'}
$blockingPressure=$blockingPressureJson | ConvertFrom-Json

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
  reportVersion='LINKAGE_SCALE_EVIDENCE_V1';gitCommitSha=$gitCommitSha;profile=$Profile;seed=$seed;collisionModulo=$collisionModulo;birthShiftModulo=$birthShiftModulo;goldPeople=$people;pairedPeople=$paired;pendingWithoutCpf=$pending;trainingSampleSize=$sample;trainingPoolSize=$pool;modelVersion=$model;runtimeScope=$runtimeScope;parametersGenerateMilliseconds=$paramMs;runnerMilliseconds=$runnerMs;blockingPressure=$blockingPressure;blockingPassPressure=$blockingPassPressure;blockingAuditSampleSize=$blockingAuditLabelCount;scaleFpBudgetBasisPoints=$scaleFpBudgetBp;scaleFpBudgetSource='MODEL_TEST_BP_SYNTHETIC_SCALE_MONITOR';decisionQuality=$decisionQuality;
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
  & $Python3.Exe @($Python3.Prefix) 'scripts/linkage-decision-quality-gate.py' $out; if($LASTEXITCODE-ne 0){throw 'evidência diagnóstica de decisão do linkage inválida'}
} finally { Pop-Location }
Write-Host "Scale harness concluído: $out"; Get-Content $out
){throw 'budget FP congelado ausente no modelo SCALE'}
$scaleFpBudgetBp=[int]$scaleFpBudgetText
if($scaleFpBudgetBp -gt 10000){throw 'budget FP congelado inválido no modelo SCALE'}
$blockingPressureJson=Scalar "DECLARE @ruleset uniqueidentifier=(SELECT ruleset_id FROM identidade.linkage_ruleset WHERE modelo_id='$modelId'); SELECT (SELECT (SELECT COUNT(*) FROM identidade.linkage_ruleset_passe WHERE ruleset_id=@ruleset) AS ruleSetPassCount, (SELECT COUNT_BIG(*) FROM identidade.blocking_chave WHERE vigencia_fim IS NULL) AS blockingRows, (SELECT COUNT_BIG(*) FROM (SELECT atributo,valor_normalizado FROM identidade.blocking_chave WHERE vigencia_fim IS NULL GROUP BY atributo,valor_normalizado) d) AS distinctKeys, (SELECT ISNULL(MAX(people_per_key),0) FROM (SELECT COUNT_BIG(DISTINCT pessoa_uuid) people_per_key FROM identidade.blocking_chave WHERE vigencia_fim IS NULL GROUP BY atributo,valor_normalizado) q) AS maxPeoplePerKey, JSON_QUERY((SELECT a.atributo AS attribute, COUNT_BIG(*) AS rows, COUNT_BIG(DISTINCT a.valor_normalizado) AS distinctValues, (SELECT ISNULL(MAX(people_per_value),0) FROM (SELECT COUNT_BIG(DISTINCT b.pessoa_uuid) people_per_value FROM identidade.blocking_chave b WHERE b.vigencia_fim IS NULL AND b.atributo=a.atributo GROUP BY b.valor_normalizado) z) AS maxPeoplePerValue FROM identidade.blocking_chave a WHERE a.vigencia_fim IS NULL GROUP BY a.atributo FOR JSON PATH)) AS attributes FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);"
if([string]::IsNullOrWhiteSpace($blockingPressureJson)){throw 'métricas de pressão de blocking ausentes.'}
$blockingPressure=$blockingPressureJson | ConvertFrom-Json

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
  reportVersion='LINKAGE_SCALE_EVIDENCE_V1';gitCommitSha=$gitCommitSha;profile=$Profile;seed=$seed;collisionModulo=$collisionModulo;birthShiftModulo=$birthShiftModulo;goldPeople=$people;pairedPeople=$paired;pendingWithoutCpf=$pending;trainingSampleSize=$sample;trainingPoolSize=$pool;modelVersion=$model;runtimeScope=$runtimeScope;parametersGenerateMilliseconds=$paramMs;runnerMilliseconds=$runnerMs;blockingPressure=$blockingPressure;blockingPassPressure=$blockingPassPressure;blockingAuditSampleSize=$blockingAuditLabelCount;decisionQuality=$decisionQuality;
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
  & $Python3.Exe @($Python3.Prefix) 'scripts/linkage-decision-quality-gate.py' $out; if($LASTEXITCODE-ne 0){throw 'evidência diagnóstica de decisão do linkage inválida'}
} finally { Pop-Location }
Write-Host "Scale harness concluído: $out"; Get-Content $out
