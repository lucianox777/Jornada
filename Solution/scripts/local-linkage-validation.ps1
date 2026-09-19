param()

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'
$Fixture = '/workspace/database/Jornada_Dev_LinkageValidation.sql'
$LocalCluster = Join-Path $PSScriptRoot 'local-cluster.ps1'
$OutDir = Join-Path $Root '.local\linkage-validation'
$LabelsPath = Join-Path $OutDir 'positive-labels.csv'
$BlockingAuditPath = Join-Path $OutDir 'blocking-pass-audit.json'
$ReportPath = Join-Path $OutDir 'validation-report.json'

if (-not (Test-Path -LiteralPath $EnvFile)) { throw "Arquivo .env ausente em $Root. Execute primeiro '.\scripts\local-cluster.ps1 -Action up'." }
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker não encontrado no PATH.' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Format-CommandArgument {
    param([Parameter(Mandatory=$true)][AllowEmptyString()][string]$Value)
    if ($Value -notmatch '[\s''"`$&|<>]') { return $Value }
    return "'" + $Value.Replace("'", "''") + "'"
}

function Write-CommandLine {
    param(
        [Parameter(Mandatory=$true)][string]$Executable,
        [string[]]$Arguments = @()
    )
    $tokens = @((Format-CommandArgument $Executable))
    $tokens += @($Arguments | ForEach-Object { Format-CommandArgument ([string]$_) })
    Write-Host ("# " + ($tokens -join ' ')) -ForegroundColor DarkGray
}

function Get-EnvValue([string]$Name) {
    foreach ($line in Get-Content -LiteralPath $EnvFile) {
        if ($line -match '^\s*#' -or [string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split '=', 2
        if ($parts.Count -eq 2 -and $parts[0].Trim() -eq $Name) { return $parts[1].Trim() }
    }
    return $null
}

function Invoke-Compose {
    param([Parameter(Mandatory=$true)][string[]]$ComposeArgs)
    Push-Location $Root
    try {
        Write-CommandLine 'docker' (@('compose','--env-file',$EnvFile) + $ComposeArgs)
        & docker compose --env-file $EnvFile @ComposeArgs
        if ($LASTEXITCODE -ne 0) { throw "docker compose falhou ($LASTEXITCODE)." }
    }
    finally { Pop-Location }
}

$password = Get-EnvValue 'JORNADA_SQL_SA_PASSWORD'
if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD ausente do .env.' }
$db = Get-EnvValue 'JORNADA_SQL_DATABASE'
if ([string]::IsNullOrWhiteSpace($db)) { $db = 'JornadaLocal' }

function Invoke-SqlFile([string]$ContainerPath) {
    Push-Location $Root
    try {
        Write-CommandLine 'docker' @('compose','--env-file',$EnvFile,'exec','-T','-e','SQLCMDPASSWORD=<redacted>','sqlserver','/opt/mssql-tools18/bin/sqlcmd','-S','localhost','-U','sa','-C','-b','-d',$db,'-i',$ContainerPath)
        & docker compose --env-file $EnvFile exec -T -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd `
            -S localhost -U sa -C -b -d $db -i $ContainerPath
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd -i falhou ($LASTEXITCODE)." }
    }
    finally { Pop-Location }
}

function Get-SqlLines([string]$Query) {
    Push-Location $Root
    try {
        Write-CommandLine 'docker' @('compose','--env-file',$EnvFile,'exec','-T','-e','SQLCMDPASSWORD=<redacted>','sqlserver','/opt/mssql-tools18/bin/sqlcmd','-S','localhost','-U','sa','-C','-b','-d',$db,'-W','-h','-1','-s','|','-Q',"SET NOCOUNT ON; $Query")
        $lines = @(& docker compose --env-file $EnvFile exec -T -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd `
            -S localhost -U sa -C -b -d $db -W -h -1 -s '|' -Q "SET NOCOUNT ON; $Query")
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd -Q falhou ($LASTEXITCODE)." }
        return @($lines | ForEach-Object { $_.Trim() } | Where-Object { $_ -and $_ -notmatch '^\([0-9]+ rows? affected\)$' })
    }
    finally { Pop-Location }
}

function Get-SqlScalar([string]$Query) {
    $lines = @(Get-SqlLines $Query)
    if ($lines.Count -eq 0) { return '' }
    return [string]$lines[-1]
}

function Parse-Decimal([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text) -or $Text -eq 'NULL') { return $null }
    return [decimal]::Parse($Text, [Globalization.CultureInfo]::InvariantCulture)
}

function Get-CompleteValidationRunId {
    param(
        [Parameter(Mandatory=$true)][string]$ModelId,
        [Parameter(Mandatory=$true)][string]$ModelShort
    )

    return Get-SqlScalar @"
SELECT TOP(1) CONVERT(varchar(36),lr.linkage_run_id)
FROM identidade.linkage_run lr
CROSS APPLY (
    SELECT
        SUM(CASE WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$ModelShort-POS-%' THEN 1 ELSE 0 END) AS pos_count,
        SUM(CASE WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$ModelShort-NEG-%' THEN 1 ELSE 0 END) AS neg_count,
        SUM(CASE WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$ModelShort-CONFLICT-%' THEN 1 ELSE 0 END) AS conflict_count
    FROM identidade.linkage_resultado r
    JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id
    WHERE r.linkage_run_id=lr.linkage_run_id
) c
WHERE lr.status='PUBLICADO'
  AND lr.tipo_run='ON_DEMAND'
  AND lr.modelo_id='$ModelId'
  AND c.pos_count=40
  AND c.neg_count=40
  AND c.conflict_count=10
ORDER BY lr.publicado_em DESC,lr.iniciado_em DESC,lr.linkage_run_id DESC;
"@
}

$activeModelId = Get-SqlScalar "SELECT TOP(1) CONVERT(varchar(36),modelo_id) FROM identidade.modelo_linkage WHERE status='ATIVO' AND ISNULL(amostra_metodo,'')<>'SEED_DEV_FIXO_NAO_TREINADO' ORDER BY versao DESC;"
if ([string]::IsNullOrWhiteSpace($activeModelId)) {
    throw "Nenhum modelo calibrado ATIVO. Execute primeiro '.\scripts\local-cluster.ps1 -Action calibrate'."
}
$modelShort = $activeModelId.Substring(0,8).ToLowerInvariant()
$modelVersion = [int](Get-SqlScalar "SELECT versao FROM identidade.modelo_linkage WHERE modelo_id='$activeModelId';")
$algorithmVersion = Get-SqlScalar "SELECT algoritmo_versao FROM identidade.modelo_linkage WHERE modelo_id='$activeModelId';"
Write-Host "Validação independente: modelo v$modelVersion / $activeModelId / $algorithmVersion"

Invoke-SqlFile $Fixture

# Reutiliza evidência completa já publicada para o mesmo modelo. Isso torna a validação idempotente:
# observações resolvidas deixam de entrar no próximo ON_DEMAND e um segundo run isolado seria parcial.
$runId = Get-CompleteValidationRunId -ModelId $activeModelId -ModelShort $modelShort
if ([string]::IsNullOrWhiteSpace($runId)) {
    Write-CommandLine $LocalCluster @('-Action','linkage')
    & $LocalCluster -Action linkage
    if ($LASTEXITCODE -ne 0) { throw "local-cluster.ps1 linkage falhou ($LASTEXITCODE)." }

    $runId = Get-CompleteValidationRunId -ModelId $activeModelId -ModelShort $modelShort
}
else {
    Write-Host "Reutilizando run completo já publicado para este modelo: $runId"
}

if ([string]::IsNullOrWhiteSpace($runId)) {
    throw 'Nenhum linkage completo de validação (40 positivos, 40 negativos, 10 conflitos) foi publicado.'
}
Write-Host "Run de validação: $runId"

$labelsQuery = @"
WITH pos AS (
    SELECT po.pessoa_observacao_id,TRY_CONVERT(int,RIGHT(po.codigo_pessoa_origem,6)) AS n
    FROM silver.pessoa_observacao po
    WHERE po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-%'
), truth AS (
    SELECT p.pessoa_observacao_id,vc.pessoa_uuid
    FROM pos p
    JOIN silver.pessoa_observacao tpo
      ON tpo.codigo_pessoa_origem=CONCAT(N'SCALE-SEHAB-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),p.n),10))
    JOIN identidade.v_vinculo_corrente vc
      ON vc.pessoa_observacao_id=tpo.pessoa_observacao_id
     AND vc.status=N'RESOLVIDO'
     AND vc.pessoa_uuid IS NOT NULL
)
SELECT CONCAT(pessoa_observacao_id,',',CONVERT(varchar(36),pessoa_uuid))
FROM truth
ORDER BY pessoa_observacao_id;
"@
$labelLines = @('pessoa_observacao_id,pessoa_uuid_verdade') + @(Get-SqlLines $labelsQuery)
if (($labelLines.Count - 1) -ne 40) { throw "Esperados 40 rótulos positivos; obtidos=$($labelLines.Count-1)." }
[IO.File]::WriteAllLines($LabelsPath, $labelLines, [Text.UTF8Encoding]::new($false))

$containerLabels = '/tmp/jornada-linkage-validation-labels.csv'
$containerAudit = '/tmp/jornada-linkage-validation-blocking.json'

Invoke-Compose -ComposeArgs @('cp',$LabelsPath,"jornada-node2:$containerLabels")

Invoke-Compose -ComposeArgs @(
    'exec','-T','jornada-node2','dotnet','/opt/jornada/apps/Jornada.Linkage.Runner/Jornada.Linkage.Runner.dll',
    '--blocking-pass-audit-labels',$containerLabels,
    '--blocking-pass-audit-output',$containerAudit,
    '--ProbabilisticLinkage:CommandTimeoutSeconds','300')

Invoke-Compose -ComposeArgs @('cp',"jornada-node2:$containerAudit",$BlockingAuditPath)
$blockingAudit = Get-Content -Raw -Encoding UTF8 $BlockingAuditPath | ConvertFrom-Json

$positiveMetricLine = Get-SqlScalar @"
WITH truth AS (
    SELECT r.*,po.codigo_pessoa_origem,vc.pessoa_uuid AS truth_uuid
    FROM identidade.linkage_resultado r
    JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id
    JOIN silver.pessoa_observacao tpo
      ON tpo.codigo_pessoa_origem=CONCAT(N'SCALE-SEHAB-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),TRY_CONVERT(int,RIGHT(po.codigo_pessoa_origem,6))),10))
    JOIN identidade.v_vinculo_corrente vc
      ON vc.pessoa_observacao_id=tpo.pessoa_observacao_id
     AND vc.status=N'RESOLVIDO'
     AND vc.pessoa_uuid IS NOT NULL
    WHERE r.linkage_run_id='$runId'
      AND po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-%'
)
SELECT CONCAT(
    COUNT_BIG(*),'|',
    SUM(CASE WHEN status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN status='RESOLVIDO' AND (pessoa_uuid_resolvido IS NULL OR pessoa_uuid_resolvido<>truth_uuid) THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN status<>'RESOLVIDO' THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN melhor_candidato_uuid=truth_uuid THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN segundo_candidato_uuid=truth_uuid THEN 1 ELSE 0 END))
FROM truth;
"@
$pos = $positiveMetricLine.Split('|')

$positiveScenarioLines = @(Get-SqlLines @"
WITH truth AS (
    SELECT
        CASE
            WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-EXACT-%' THEN N'EXACT'
            WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-NAME_ABBREV-%' THEN N'NAME_ABBREV'
            WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-MOTHER_ABBREV-%' THEN N'MOTHER_ABBREV'
            WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-BIRTH_SHIFT-%' THEN N'BIRTH_SHIFT'
            WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-COMBINED-%' THEN N'COMBINED'
            ELSE N'UNKNOWN'
        END AS scenario,
        r.*,
        vc.pessoa_uuid AS truth_uuid
    FROM identidade.linkage_resultado r
    JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id
    JOIN silver.pessoa_observacao tpo
      ON tpo.codigo_pessoa_origem=CONCAT(N'SCALE-SEHAB-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),TRY_CONVERT(int,RIGHT(po.codigo_pessoa_origem,6))),10))
    JOIN identidade.v_vinculo_corrente vc
      ON vc.pessoa_observacao_id=tpo.pessoa_observacao_id
     AND vc.status=N'RESOLVIDO'
     AND vc.pessoa_uuid IS NOT NULL
    WHERE r.linkage_run_id='$runId'
      AND po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-%'
)
SELECT CONCAT(
    scenario,'|',
    COUNT_BIG(*),'|',
    SUM(CASE WHEN status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN status='RESOLVIDO' AND pessoa_uuid_resolvido<>truth_uuid THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN status='CONFLITO' THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN status='NAO_RESOLVIDO' THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN melhor_candidato_uuid=truth_uuid THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN segundo_candidato_uuid=truth_uuid THEN 1 ELSE 0 END),'|',
    COALESCE(CONVERT(varchar(40),MIN(score_melhor)),'NULL'),'|',
    COALESCE(CONVERT(varchar(40),MAX(score_melhor)),'NULL'),'|',
    COALESCE(CONVERT(varchar(40),MIN(score_segundo)),'NULL'),'|',
    COALESCE(CONVERT(varchar(40),MAX(score_segundo)),'NULL'))
FROM truth
GROUP BY scenario
ORDER BY CASE scenario
    WHEN N'EXACT' THEN 1
    WHEN N'NAME_ABBREV' THEN 2
    WHEN N'MOTHER_ABBREV' THEN 3
    WHEN N'BIRTH_SHIFT' THEN 4
    WHEN N'COMBINED' THEN 5
    ELSE 6 END;
"@)

$positiveScenarioBreakdown = @(
    foreach ($line in $positiveScenarioLines) {
        $parts = $line.Split('|')
        [ordered]@{
            scenario = $parts[0]
            total = [int]$parts[1]
            resolvedCorrect = [int]$parts[2]
            resolvedWrong = [int]$parts[3]
            conflicts = [int]$parts[4]
            unresolved = [int]$parts[5]
            truthTop1 = [int]$parts[6]
            truthTop2 = [int]$parts[7]
            minBestScore = (Parse-Decimal $parts[8])
            maxBestScore = (Parse-Decimal $parts[9])
            minSecondScore = (Parse-Decimal $parts[10])
            maxSecondScore = (Parse-Decimal $parts[11])
        }
    }
)

$negativeMetricLine = Get-SqlScalar @"
SELECT CONCAT(
    COUNT_BIG(*),'|',
    SUM(CASE WHEN r.status='RESOLVIDO' THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN r.status<>'RESOLVIDO' THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN r.melhor_candidato_uuid IS NOT NULL THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN r.status='CONFLITO' THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN r.motivo='SEM_CANDIDATO_NO_BLOCO' THEN 1 ELSE 0 END))
FROM identidade.linkage_resultado r
JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id
WHERE r.linkage_run_id='$runId'
  AND po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-%';
"@
$neg = $negativeMetricLine.Split('|')

$negativeScenarioLines = @(Get-SqlLines @"
WITH n AS (
    SELECT
        CASE
            WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-EASY-%' THEN N'EASY'
            WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-NAME_COLLISION-%' THEN N'NAME_COLLISION'
            WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-MOTHER_COLLISION-%' THEN N'MOTHER_COLLISION'
            WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-HARD_HOMONYM-%' THEN N'HARD_HOMONYM'
            ELSE N'UNKNOWN'
        END AS scenario,
        r.*
    FROM identidade.linkage_resultado r
    JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id
    WHERE r.linkage_run_id='$runId'
      AND po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-%'
)
SELECT CONCAT(
    scenario,'|',
    COUNT_BIG(*),'|',
    SUM(CASE WHEN status='RESOLVIDO' THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN status='CONFLITO' THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN status='NAO_RESOLVIDO' THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN melhor_candidato_uuid IS NOT NULL THEN 1 ELSE 0 END),'|',
    COALESCE(CONVERT(varchar(40),MIN(score_melhor)),'NULL'),'|',
    COALESCE(CONVERT(varchar(40),MAX(score_melhor)),'NULL'))
FROM n
GROUP BY scenario
ORDER BY CASE scenario
    WHEN N'EASY' THEN 1
    WHEN N'NAME_COLLISION' THEN 2
    WHEN N'MOTHER_COLLISION' THEN 3
    WHEN N'HARD_HOMONYM' THEN 4
    ELSE 5 END;
"@)

$negativeScenarioBreakdown = @(
    foreach ($line in $negativeScenarioLines) {
        $parts = $line.Split('|')
        [ordered]@{
            scenario = $parts[0]
            total = [int]$parts[1]
            resolvedFalseMatches = [int]$parts[2]
            conflicts = [int]$parts[3]
            unresolved = [int]$parts[4]
            candidateExposure = [int]$parts[5]
            minBestScore = (Parse-Decimal $parts[6])
            maxBestScore = (Parse-Decimal $parts[7])
        }
    }
)

$negativeFalseMatchLines = @(Get-SqlLines @"
SELECT CONCAT(
    CASE
        WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-EASY-%' THEN N'EASY'
        WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-NAME_COLLISION-%' THEN N'NAME_COLLISION'
        WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-MOTHER_COLLISION-%' THEN N'MOTHER_COLLISION'
        WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-HARD_HOMONYM-%' THEN N'HARD_HOMONYM'
        ELSE N'UNKNOWN'
    END,'|',
    po.codigo_pessoa_origem,'|',
    CONVERT(varchar(40),r.score_melhor),'|',
    COALESCE(CONVERT(varchar(40),r.score_segundo),'NULL'),'|',
    COALESCE(CONVERT(varchar(40),r.margem),'NULL'),'|',
    COALESCE(CONVERT(varchar(36),r.melhor_candidato_uuid),'NULL'),'|',
    COALESCE(CONVERT(varchar(36),r.segundo_candidato_uuid),'NULL'),'|',
    REPLACE(po.nome_completo,'|',' '),'|',
    REPLACE(COALESCE(po.nome_mae,N''),'|',' '),'|',
    CONVERT(varchar(10),po.data_nascimento,23),'|',
    REPLACE(g.nome_completo,'|',' '),'|',
    REPLACE(COALESCE(g.nome_mae,N''),'|',' '),'|',
    CONVERT(varchar(10),g.data_nascimento,23))
FROM identidade.linkage_resultado r
JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id
JOIN gold.pessoa g ON g.pessoa_uuid=r.melhor_candidato_uuid
WHERE r.linkage_run_id='$runId'
  AND po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-%'
  AND r.status='RESOLVIDO'
ORDER BY po.codigo_pessoa_origem;
"@)

$negativeFalseMatchDetails = @(
    foreach ($line in $negativeFalseMatchLines) {
        $parts = $line.Split('|')
        [ordered]@{
            scenario = $parts[0]
            sourceCode = $parts[1]
            bestScore = (Parse-Decimal $parts[2])
            secondScore = (Parse-Decimal $parts[3])
            margin = (Parse-Decimal $parts[4])
            bestCandidateUuid = $parts[5]
            secondCandidateUuid = $parts[6]
            observationName = $parts[7]
            observationMotherName = $parts[8]
            observationBirthDate = $parts[9]
            bestCandidateName = $parts[10]
            bestCandidateMotherName = $parts[11]
            bestCandidateBirthDate = $parts[12]
        }
    }
)

$thresholdText = Get-SqlScalar "SELECT CONVERT(varchar(40),valor) FROM identidade.parametro_linkage WHERE modelo_id='$activeModelId' AND nome='T_LINKAGE';"
$threshold = Parse-Decimal $thresholdText
$conflictMarginText = Get-SqlScalar "SELECT CONVERT(varchar(40),valor) FROM identidade.parametro_linkage WHERE modelo_id='$activeModelId' AND nome='CONFLICT_MARGIN_LOG_ODDS';"
$conflictMarginLogOdds = Parse-Decimal $conflictMarginText
if ($null -eq $conflictMarginLogOdds) { throw 'CONFLICT_MARGIN_LOG_ODDS ausente no modelo ativo.' }
$dualThresholdFlagText = Get-SqlScalar "SELECT CONVERT(varchar(40),valor) FROM identidade.parametro_linkage WHERE modelo_id='$activeModelId' AND nome='SCORING_DUAL_THRESHOLD_CONFLICT_V1';"
$dualThresholdFlag = Parse-Decimal $dualThresholdFlagText
$dualThresholdGuardEnabled = ($null -ne $dualThresholdFlag -and $dualThresholdFlag -ge [decimal]1)

$counterfactualPositiveLine = Get-SqlScalar @"
WITH truth AS (
    SELECT r.*,vc.pessoa_uuid AS truth_uuid
    FROM identidade.linkage_resultado r
    JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id
    JOIN silver.pessoa_observacao tpo
      ON tpo.codigo_pessoa_origem=CONCAT(N'SCALE-SEHAB-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),TRY_CONVERT(int,RIGHT(po.codigo_pessoa_origem,6))),10))
    JOIN identidade.v_vinculo_corrente vc
      ON vc.pessoa_observacao_id=tpo.pessoa_observacao_id
     AND vc.status=N'RESOLVIDO'
     AND vc.pessoa_uuid IS NOT NULL
    WHERE r.linkage_run_id='$runId'
      AND po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-%'
), cf AS (
    SELECT *,
           CASE
             WHEN score_melhor < $thresholdText THEN N'NAO_RESOLVIDO'
             WHEN segundo_candidato_uuid IS NOT NULL AND (margem IS NULL OR margem < $conflictMarginText) THEN N'CONFLITO'
             ELSE N'RESOLVIDO'
           END AS cf_status
    FROM truth
)
SELECT CONCAT(
    COUNT_BIG(*),'|',
    SUM(CASE WHEN cf_status=N'RESOLVIDO' AND melhor_candidato_uuid=truth_uuid THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN cf_status=N'RESOLVIDO' AND (melhor_candidato_uuid IS NULL OR melhor_candidato_uuid<>truth_uuid) THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN cf_status=N'CONFLITO' THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN cf_status=N'NAO_RESOLVIDO' THEN 1 ELSE 0 END))
FROM cf;
"@
$cfPos = $counterfactualPositiveLine.Split('|')

$counterfactualNegativeLine = Get-SqlScalar @"
WITH n AS (
    SELECT r.*
    FROM identidade.linkage_resultado r
    JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id
    WHERE r.linkage_run_id='$runId'
      AND po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-%'
), cf AS (
    SELECT *,
           CASE
             WHEN score_melhor < $thresholdText THEN N'NAO_RESOLVIDO'
             WHEN segundo_candidato_uuid IS NOT NULL AND (margem IS NULL OR margem < $conflictMarginText) THEN N'CONFLITO'
             ELSE N'RESOLVIDO'
           END AS cf_status
    FROM n
)
SELECT CONCAT(
    COUNT_BIG(*),'|',
    SUM(CASE WHEN cf_status=N'RESOLVIDO' THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN cf_status=N'CONFLITO' THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN cf_status=N'NAO_RESOLVIDO' THEN 1 ELSE 0 END))
FROM cf;
"@
$cfNeg = $counterfactualNegativeLine.Split('|')

$counterfactualPositiveScenarioLines = @(Get-SqlLines @"
WITH truth AS (
    SELECT
        CASE
            WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-EXACT-%' THEN N'EXACT'
            WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-NAME_ABBREV-%' THEN N'NAME_ABBREV'
            WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-MOTHER_ABBREV-%' THEN N'MOTHER_ABBREV'
            WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-BIRTH_SHIFT-%' THEN N'BIRTH_SHIFT'
            WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-COMBINED-%' THEN N'COMBINED'
            ELSE N'UNKNOWN'
        END AS scenario,
        r.*,vc.pessoa_uuid AS truth_uuid
    FROM identidade.linkage_resultado r
    JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id
    JOIN silver.pessoa_observacao tpo
      ON tpo.codigo_pessoa_origem=CONCAT(N'SCALE-SEHAB-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),TRY_CONVERT(int,RIGHT(po.codigo_pessoa_origem,6))),10))
    JOIN identidade.v_vinculo_corrente vc
      ON vc.pessoa_observacao_id=tpo.pessoa_observacao_id
     AND vc.status=N'RESOLVIDO'
     AND vc.pessoa_uuid IS NOT NULL
    WHERE r.linkage_run_id='$runId'
      AND po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-%'
), cf AS (
    SELECT *,
           CASE
             WHEN score_melhor < $thresholdText THEN N'NAO_RESOLVIDO'
             WHEN segundo_candidato_uuid IS NOT NULL AND (margem IS NULL OR margem < $conflictMarginText) THEN N'CONFLITO'
             ELSE N'RESOLVIDO'
           END AS cf_status
    FROM truth
)
SELECT CONCAT(
    scenario,'|',COUNT_BIG(*),'|',
    SUM(CASE WHEN cf_status=N'RESOLVIDO' AND melhor_candidato_uuid=truth_uuid THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN cf_status=N'RESOLVIDO' AND (melhor_candidato_uuid IS NULL OR melhor_candidato_uuid<>truth_uuid) THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN cf_status=N'CONFLITO' THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN cf_status=N'NAO_RESOLVIDO' THEN 1 ELSE 0 END))
FROM cf
GROUP BY scenario
ORDER BY CASE scenario
    WHEN N'EXACT' THEN 1
    WHEN N'NAME_ABBREV' THEN 2
    WHEN N'MOTHER_ABBREV' THEN 3
    WHEN N'BIRTH_SHIFT' THEN 4
    WHEN N'COMBINED' THEN 5
    ELSE 6 END;
"@)
$counterfactualPositiveScenarios = @(
    foreach ($line in $counterfactualPositiveScenarioLines) {
        $parts = $line.Split('|')
        [ordered]@{
            scenario = $parts[0]
            total = [int]$parts[1]
            resolvedCorrect = [int]$parts[2]
            resolvedWrong = [int]$parts[3]
            conflicts = [int]$parts[4]
            unresolved = [int]$parts[5]
        }
    }
)

$counterfactualNegativeScenarioLines = @(Get-SqlLines @"
WITH n AS (
    SELECT
        CASE
            WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-EASY-%' THEN N'EASY'
            WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-NAME_COLLISION-%' THEN N'NAME_COLLISION'
            WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-MOTHER_COLLISION-%' THEN N'MOTHER_COLLISION'
            WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-HARD_HOMONYM-%' THEN N'HARD_HOMONYM'
            ELSE N'UNKNOWN'
        END AS scenario,
        r.*
    FROM identidade.linkage_resultado r
    JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id
    WHERE r.linkage_run_id='$runId'
      AND po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-%'
), cf AS (
    SELECT *,
           CASE
             WHEN score_melhor < $thresholdText THEN N'NAO_RESOLVIDO'
             WHEN segundo_candidato_uuid IS NOT NULL AND (margem IS NULL OR margem < $conflictMarginText) THEN N'CONFLITO'
             ELSE N'RESOLVIDO'
           END AS cf_status
    FROM n
)
SELECT CONCAT(
    scenario,'|',COUNT_BIG(*),'|',
    SUM(CASE WHEN cf_status=N'RESOLVIDO' THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN cf_status=N'CONFLITO' THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN cf_status=N'NAO_RESOLVIDO' THEN 1 ELSE 0 END))
FROM cf
GROUP BY scenario
ORDER BY CASE scenario
    WHEN N'EASY' THEN 1
    WHEN N'NAME_COLLISION' THEN 2
    WHEN N'MOTHER_COLLISION' THEN 3
    WHEN N'HARD_HOMONYM' THEN 4
    ELSE 5 END;
"@)
$counterfactualNegativeScenarios = @(
    foreach ($line in $counterfactualNegativeScenarioLines) {
        $parts = $line.Split('|')
        [ordered]@{
            scenario = $parts[0]
            total = [int]$parts[1]
            resolvedFalseMatches = [int]$parts[2]
            conflicts = [int]$parts[3]
            unresolved = [int]$parts[4]
        }
    }
)


$priorProbability = Parse-Decimal (Get-SqlScalar "SELECT CONVERT(varchar(40),valor) FROM identidade.parametro_linkage WHERE modelo_id='$activeModelId' AND nome='PRIOR_MATCH_PROBABILITY';")
if ($null -eq $priorProbability) { throw 'PRIOR_MATCH_PROBABILITY ausente no modelo ativo.' }
$priorLogOddsDouble = [Math]::Log([double]$priorProbability / (1.0 - [double]$priorProbability))

$scenarioDefinitions = @(
    [ordered]@{ scenario='EASY'; nameState='LOW'; motherState='LOW'; birthState='EXACT' },
    [ordered]@{ scenario='NAME_COLLISION'; nameState='EXACT'; motherState='LOW'; birthState='EXACT' },
    [ordered]@{ scenario='MOTHER_COLLISION'; nameState='LOW'; motherState='EXACT'; birthState='EXACT' },
    [ordered]@{ scenario='HARD_HOMONYM'; nameState='EXACT'; motherState='EXACT'; birthState='EXACT' }
)

$negativeScenarioEvidence = @(
    foreach ($definition in $scenarioDefinitions) {
        $nameM = Parse-Decimal (Get-SqlScalar "SELECT CONVERT(varchar(40),valor) FROM identidade.parametro_linkage WHERE modelo_id='$activeModelId' AND nome='M_NOME_$($definition.nameState)';")
        $nameU = Parse-Decimal (Get-SqlScalar "SELECT CONVERT(varchar(40),valor) FROM identidade.parametro_linkage WHERE modelo_id='$activeModelId' AND nome='U_NOME_$($definition.nameState)';")
        $motherM = Parse-Decimal (Get-SqlScalar "SELECT CONVERT(varchar(40),valor) FROM identidade.parametro_linkage WHERE modelo_id='$activeModelId' AND nome='M_NOME_MAE_$($definition.motherState)';")
        $motherU = Parse-Decimal (Get-SqlScalar "SELECT CONVERT(varchar(40),valor) FROM identidade.parametro_linkage WHERE modelo_id='$activeModelId' AND nome='U_NOME_MAE_$($definition.motherState)';")
        $birthM = Parse-Decimal (Get-SqlScalar "SELECT CONVERT(varchar(40),valor) FROM identidade.parametro_linkage WHERE modelo_id='$activeModelId' AND nome='M_NASCIMENTO_SEMANTICO_$($definition.birthState)';")
        $birthU = Parse-Decimal (Get-SqlScalar "SELECT CONVERT(varchar(40),valor) FROM identidade.parametro_linkage WHERE modelo_id='$activeModelId' AND nome='U_NASCIMENTO_SEMANTICO_$($definition.birthState)';")

        if ($null -in @($nameM,$nameU,$motherM,$motherU,$birthM,$birthU)) {
            throw "Parâmetro probabilístico ausente ao montar perfil do cenário $($definition.scenario)."
        }

        $nameLlrDouble = [Math]::Log([double]$nameM / [double]$nameU)
        $motherLlrDouble = [Math]::Log([double]$motherM / [double]$motherU)
        $birthLlrDouble = [Math]::Log([double]$birthM / [double]$birthU)
        $logOddsDouble = $priorLogOddsDouble + $nameLlrDouble + $motherLlrDouble + $birthLlrDouble
        $posteriorDouble = 1.0 / (1.0 + [Math]::Exp(-[Math]::Max(-40.0,[Math]::Min(40.0,$logOddsDouble))))

        [ordered]@{
            scenario = $definition.scenario
            nameState = $definition.nameState
            motherState = $definition.motherState
            birthState = $definition.birthState
            priorLogOdds = [decimal]::Round([decimal]$priorLogOddsDouble,8)
            nameLlr = [decimal]::Round([decimal]$nameLlrDouble,8)
            motherLlr = [decimal]::Round([decimal]$motherLlrDouble,8)
            birthLlr = [decimal]::Round([decimal]$birthLlrDouble,8)
            theoreticalPosterior = [decimal]::Round([decimal]$posteriorDouble,8)
        }
    }
)

$conflictMetricLine = Get-SqlScalar @"
SELECT CONCAT(
    COUNT_BIG(*),'|',
    SUM(CASE WHEN r.status='CONFLITO' THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN r.margem=0 THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN r.score_melhor>=$thresholdText THEN 1 ELSE 0 END),'|',
    SUM(CASE WHEN r.status='RESOLVIDO' THEN 1 ELSE 0 END),'|',
    CONVERT(varchar(40),MIN(r.score_melhor)),'|',
    CONVERT(varchar(40),MAX(r.score_melhor)))
FROM identidade.linkage_resultado r
JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id
WHERE r.linkage_run_id='$runId'
  AND po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-CONFLICT-%';
"@
$conf = $conflictMetricLine.Split('|')

$frontierLine = Get-SqlScalar @"
WITH v AS (
    SELECT r.score_melhor
    FROM identidade.linkage_resultado r
    JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id
    WHERE r.linkage_run_id='$runId'
      AND (po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-POS-%'
        OR po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-NEG-%'
        OR po.codigo_pessoa_origem LIKE N'SCALE-VAL-$modelShort-CONFLICT-%')
)
SELECT CONCAT(
    SUM(CASE WHEN ABS(score_melhor-$thresholdText)<=0.02 THEN 1 ELSE 0 END),'|',
    COALESCE(CONVERT(varchar(40),MAX(CASE WHEN score_melhor<$thresholdText THEN score_melhor END)),'NULL'),'|',
    COALESCE(CONVERT(varchar(40),MIN(CASE WHEN score_melhor>=$thresholdText THEN score_melhor END)),'NULL'))
FROM v;
"@
$frontier = $frontierLine.Split('|')

$theoretical = @(Get-SqlLines @"
WITH prior AS (
    SELECT CAST(valor AS float) AS p
    FROM identidade.parametro_linkage
    WHERE modelo_id='$activeModelId' AND nome='PRIOR_MATCH_PROBABILITY'
), names AS (
    SELECT REPLACE(m.nome,'M_NOME_','') AS estado,
           LOG(CAST(m.valor AS float)/CAST(u.valor AS float)) AS llr
    FROM identidade.parametro_linkage m
    JOIN identidade.parametro_linkage u ON u.modelo_id=m.modelo_id AND u.nome=REPLACE(m.nome,'M_','U_')
    WHERE m.modelo_id='$activeModelId'
      AND LEFT(m.nome,7)='M_NOME_'
      AND LEFT(m.nome,11)<>'M_NOME_MAE_'
), mothers AS (
    SELECT REPLACE(m.nome,'M_NOME_MAE_','') AS estado,
           LOG(CAST(m.valor AS float)/CAST(u.valor AS float)) AS llr
    FROM identidade.parametro_linkage m
    JOIN identidade.parametro_linkage u ON u.modelo_id=m.modelo_id AND u.nome=REPLACE(m.nome,'M_','U_')
    WHERE m.modelo_id='$activeModelId' AND LEFT(m.nome,11)='M_NOME_MAE_'
), births AS (
    SELECT REPLACE(m.nome,'M_NASCIMENTO_SEMANTICO_','') AS estado,
           LOG(CAST(m.valor AS float)/CAST(u.valor AS float)) AS llr
    FROM identidade.parametro_linkage m
    JOIN identidade.parametro_linkage u ON u.modelo_id=m.modelo_id AND u.nome=REPLACE(m.nome,'M_','U_')
    WHERE m.modelo_id='$activeModelId' AND LEFT(m.nome,23)='M_NASCIMENTO_SEMANTICO_'
), lattice AS (
    SELECT n.estado AS nome_estado,ma.estado AS mae_estado,b.estado AS nascimento_estado,
           1.0/(1.0+EXP(-(LOG(prior.p/(1.0-prior.p))+n.llr+ma.llr+b.llr))) AS posterior
    FROM prior
    CROSS JOIN names n
    CROSS JOIN mothers ma
    CROSS JOIN births b
)
SELECT TOP(12) CONCAT(nome_estado,'/',mae_estado,'/',nascimento_estado,'|',CONVERT(varchar(40),CAST(posterior AS decimal(18,8))))
FROM lattice
ORDER BY ABS(posterior-$thresholdText),nome_estado,mae_estado,nascimento_estado;
"@)

$orderedMleValue = Parse-Decimal (Get-SqlScalar "SELECT CONVERT(varchar(40),valor) FROM identidade.parametro_linkage WHERE modelo_id='$activeModelId' AND nome='ORDER_RESTRICTED_NAME_LLR_MLE_V1';")
$orderedMleEnabled = ($null -ne $orderedMleValue -and $orderedMleValue -ge [decimal]1)

$orderRestrictionLines = @(Get-SqlLines @"
WITH ord AS (
    SELECT *
    FROM (VALUES
        (1,N'NOME',N'EXACT'),
        (2,N'NOME',N'HIGH'),
        (3,N'NOME',N'MEDIUM'),
        (4,N'NOME',N'LOW'),
        (5,N'NOME_MAE',N'EXACT'),
        (6,N'NOME_MAE',N'HIGH'),
        (7,N'NOME_MAE',N'MEDIUM'),
        (8,N'NOME_MAE',N'LOW')
    ) v(ordem,campo,estado)
)
SELECT CONCAT(
    o.campo,'|',o.estado,'|',
    CONVERT(varchar(40),um.valor),'|',
    CONVERT(varchar(40),m.valor),'|',
    CONVERT(varchar(40),u.valor),'|',
    CONVERT(varchar(40),support_m.valor),'|',
    CONVERT(varchar(40),blk.valor),'|',
    CONVERT(varchar(40),delta.valor),'|',
    CONVERT(varchar(40),CAST(LOG(CAST(um.valor AS float)/CAST(u.valor AS float)) AS decimal(30,12))),'|',
    CONVERT(varchar(40),CAST(LOG(CAST(m.valor AS float)/CAST(u.valor AS float)) AS decimal(30,12))),'|',
    CONVERT(varchar(40),adj.valor),'|',
    CONVERT(varchar(40),mx.valor))
FROM ord o
JOIN identidade.parametro_linkage um
  ON um.modelo_id='$activeModelId'
 AND um.nome=CONCAT(N'UNRESTRICTED_M_',o.campo,N'_',o.estado)
JOIN identidade.parametro_linkage m
  ON m.modelo_id=um.modelo_id
 AND m.nome=CONCAT(N'M_',o.campo,N'_',o.estado)
JOIN identidade.parametro_linkage u
  ON u.modelo_id=um.modelo_id
 AND u.nome=CONCAT(N'U_',o.campo,N'_',o.estado)
JOIN identidade.parametro_linkage support_m
  ON support_m.modelo_id=um.modelo_id
 AND support_m.nome=CONCAT(N'SUPPORT_M_',o.campo,N'_',o.estado)
JOIN identidade.parametro_linkage blk
  ON blk.modelo_id=um.modelo_id
 AND blk.nome=CONCAT(N'ORDER_RESTRICTED_BLOCK_',o.campo,N'_',o.estado)
JOIN identidade.parametro_linkage delta
  ON delta.modelo_id=um.modelo_id
 AND delta.nome=CONCAT(N'ORDER_RESTRICTED_DELTA_LLR_',o.campo,N'_',o.estado)
JOIN identidade.parametro_linkage adj
  ON adj.modelo_id=um.modelo_id
 AND adj.nome=CONCAT(N'ORDER_RESTRICTED_ADJUSTED_STATES_',o.campo)
JOIN identidade.parametro_linkage mx
  ON mx.modelo_id=um.modelo_id
 AND mx.nome=CONCAT(N'ORDER_RESTRICTED_MAX_ABS_DELTA_LLR_',o.campo)
ORDER BY o.ordem;
"@)

$orderRestrictionStates = @(
    foreach ($line in $orderRestrictionLines) {
        $parts = $line.Split('|')
        [ordered]@{
            field = $parts[0]
            state = $parts[1]
            unrestrictedM = (Parse-Decimal $parts[2])
            restrictedM = (Parse-Decimal $parts[3])
            u = (Parse-Decimal $parts[4])
            matchedSupport = [int](Parse-Decimal $parts[5])
            block = [int](Parse-Decimal $parts[6])
            deltaLlr = (Parse-Decimal $parts[7])
            unrestrictedLlr = (Parse-Decimal $parts[8])
            restrictedLlr = (Parse-Decimal $parts[9])
            persistedAdjustedStates = [int](Parse-Decimal $parts[10])
            persistedMaxAbsDeltaLlr = (Parse-Decimal $parts[11])
        }
    }
)

$orderRestrictionFields = @(
    foreach ($field in @('NOME','NOME_MAE')) {
        $items = @($orderRestrictionStates | Where-Object { $_.field -eq $field })
        if ($items.Count -eq 0) { continue }
        $computedAdjusted = @($items | Where-Object { [Math]::Abs([double]$_.deltaLlr) -gt 1e-12 }).Count
        $computedMax = [decimal](($items | ForEach-Object { [Math]::Abs([double]$_.deltaLlr) } | Measure-Object -Maximum).Maximum)
        [ordered]@{
            field = $field
            adjustedStates = $items[0].persistedAdjustedStates
            maxAbsDeltaLlr = $items[0].persistedMaxAbsDeltaLlr
            computedAdjustedStates = $computedAdjusted
            computedMaxAbsDeltaLlr = $computedMax
        }
    }
)

if ($orderedMleEnabled) {
    if ($orderRestrictionStates.Count -ne 8) {
        throw "Auditoria MLE ordenada incompleta: estados=$($orderRestrictionStates.Count); esperado=8."
    }
    foreach ($fieldSummary in $orderRestrictionFields) {
        if ($fieldSummary.adjustedStates -ne $fieldSummary.computedAdjustedStates) {
            throw "Auditoria MLE ordenada inconsistente em $($fieldSummary.field): adjusted persistido=$($fieldSummary.adjustedStates), calculado=$($fieldSummary.computedAdjustedStates)."
        }
        if ([Math]::Abs([double]($fieldSummary.maxAbsDeltaLlr - $fieldSummary.computedMaxAbsDeltaLlr)) -gt 1e-10) {
            throw "Auditoria MLE ordenada inconsistente em $($fieldSummary.field): max |delta LLR| persistido=$($fieldSummary.maxAbsDeltaLlr), calculado=$($fieldSummary.computedMaxAbsDeltaLlr)."
        }
    }
}

$positiveTotal = [int]$pos[0]
$positiveCorrect = [int]$pos[1]
$positiveWrong = [int]$pos[2]
$positiveUnresolved = [int]$pos[3]
$positiveTruthTop1 = [int]$pos[4]
$positiveTruthTop2 = [int]$pos[5]
$negativeTotal = [int]$neg[0]
$negativeResolved = [int]$neg[1]
$negativeRejected = [int]$neg[2]
$negativeCandidateExposure = [int]$neg[3]
$negativeConflicts = [int]$neg[4]
$negativeNoCandidate = [int]$neg[5]
$conflictTotal = [int]$conf[0]
$conflictStatus = [int]$conf[1]
$conflictMarginZero = [int]$conf[2]
$conflictAboveThreshold = [int]$conf[3]
$conflictResolved = [int]$conf[4]

$positiveSensitivity = if ($positiveTotal -eq 0) { [decimal]0 } else { [decimal]$positiveCorrect / [decimal]$positiveTotal }
$negativeSpecificity = if ($negativeTotal -eq 0) { [decimal]0 } else { [decimal]$negativeRejected / [decimal]$negativeTotal }
$negativeFalseMatchRate = if ($negativeTotal -eq 0) { [decimal]0 } else { [decimal]$negativeResolved / [decimal]$negativeTotal }
$resolvedDecisionTotal = $positiveCorrect + $positiveWrong + $negativeResolved
$syntheticResolvedPpv = if ($resolvedDecisionTotal -eq 0) { [decimal]0 } else { [decimal]$positiveCorrect / [decimal]$resolvedDecisionTotal }

$cfPositiveTotal = [int]$cfPos[0]
$cfPositiveCorrect = [int]$cfPos[1]
$cfPositiveWrong = [int]$cfPos[2]
$cfPositiveConflicts = [int]$cfPos[3]
$cfPositiveUnresolved = [int]$cfPos[4]
$cfNegativeTotal = [int]$cfNeg[0]
$cfNegativeResolved = [int]$cfNeg[1]
$cfNegativeConflicts = [int]$cfNeg[2]
$cfNegativeUnresolved = [int]$cfNeg[3]
$cfResolvedDecisionTotal = $cfPositiveCorrect + $cfPositiveWrong + $cfNegativeResolved
$cfSyntheticResolvedPpv = if ($cfResolvedDecisionTotal -eq 0) { [decimal]0 } else { [decimal]$cfPositiveCorrect / [decimal]$cfResolvedDecisionTotal }

$report = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    purpose = 'DEV_SYNTHETIC_INDEPENDENT_VALIDATION_NO_HML_CLAIM'
    safeguards = @(
        'validation rows are injected only after an active calibrated model exists',
        'fixture prefix is bound to the active model id fragment',
        'quality metrics gate only this DEV validation script; they do not promote models or alter thresholds',
        'blocking recall and conflict-rule coverage are measured separately from decision quality',
        'no regression comparison with prior models is required in this pre-homologation phase')
    model = [ordered]@{
        modelId = $activeModelId
        modelVersion = $modelVersion
        algorithmVersion = $algorithmVersion
        threshold = $threshold
    }
    runId = $runId
    nominalOrderRestriction = [ordered]@{
        enabled = $orderedMleEnabled
        fields = $orderRestrictionFields
        states = $orderRestrictionStates
    }
    blocking = $blockingAudit.summary
    positive = [ordered]@{
        total = $positiveTotal
        resolvedCorrect = $positiveCorrect
        resolvedWrong = $positiveWrong
        unresolvedOrConflict = $positiveUnresolved
        truthTop1 = $positiveTruthTop1
        truthTop2 = $positiveTruthTop2
        syntheticSensitivity = [decimal]::Round($positiveSensitivity,6)
        scenarios = $positiveScenarioBreakdown
    }
    negative = [ordered]@{
        total = $negativeTotal
        resolvedFalseMatches = $negativeResolved
        rejectedOrConflict = $negativeRejected
        candidateExposure = $negativeCandidateExposure
        conflicts = $negativeConflicts
        noCandidate = $negativeNoCandidate
        syntheticSpecificity = [decimal]::Round($negativeSpecificity,6)
        syntheticFalseMatchRate = [decimal]::Round($negativeFalseMatchRate,6)
        scenarios = $negativeScenarioBreakdown
        scenarioEvidenceProfiles = $negativeScenarioEvidence
        falseMatchDetails = $negativeFalseMatchDetails
    }
    combinedDecisionQuality = [ordered]@{
        resolvedDecisions = $resolvedDecisionTotal
        correctResolved = $positiveCorrect
        falseResolved = ($positiveWrong + $negativeResolved)
        syntheticResolvedPpv = [decimal]::Round($syntheticResolvedPpv,6)
    }
    conflictProbe = [ordered]@{
        total = $conflictTotal
        conflictStatus = $conflictStatus
        marginZero = $conflictMarginZero
        aboveThreshold = $conflictAboveThreshold
        resolvedUnexpectedly = $conflictResolved
        minBestScore = (Parse-Decimal $conf[5])
        maxBestScore = (Parse-Decimal $conf[6])
    }
    counterfactualNoDualThresholdGuard = [ordered]@{
        purpose = 'READ_ONLY_POLICY_COUNTERFACTUAL'
        changesPolicy = $false
        dualThresholdGuardEnabledInModel = $dualThresholdGuardEnabled
        retainedRules = @('T_LINKAGE','CONFLICT_MARGIN_LOG_ODDS')
        removedRuleOnly = 'SCORING_DUAL_THRESHOLD_CONFLICT_V1'
        threshold = $threshold
        conflictMarginLogOdds = $conflictMarginLogOdds
        positive = [ordered]@{
            total = $cfPositiveTotal
            resolvedCorrect = $cfPositiveCorrect
            resolvedWrong = $cfPositiveWrong
            conflicts = $cfPositiveConflicts
            unresolved = $cfPositiveUnresolved
            scenarios = $counterfactualPositiveScenarios
        }
        negative = [ordered]@{
            total = $cfNegativeTotal
            resolvedFalseMatches = $cfNegativeResolved
            conflicts = $cfNegativeConflicts
            unresolved = $cfNegativeUnresolved
            scenarios = $counterfactualNegativeScenarios
        }
        combined = [ordered]@{
            resolvedDecisions = $cfResolvedDecisionTotal
            correctResolved = $cfPositiveCorrect
            falseResolved = ($cfPositiveWrong + $cfNegativeResolved)
            syntheticResolvedPpv = [decimal]::Round($cfSyntheticResolvedPpv,6)
        }
        deltaVsCurrent = [ordered]@{
            correctResolved = ($cfPositiveCorrect - $positiveCorrect)
            falseResolved = (($cfPositiveWrong + $cfNegativeResolved) - ($positiveWrong + $negativeResolved))
            positiveConflicts = ($cfPositiveConflicts - [int](@($positiveScenarioBreakdown | Measure-Object -Property conflicts -Sum).Sum))
            negativeConflicts = ($cfNegativeConflicts - $negativeConflicts)
        }
        interpretation = 'Contrafactual read-only: reaplica as decisões sobre ranking/score já persistidos, removendo somente o guard de dois candidatos acima de T. Não recalibra o modelo, não publica vínculos e não recomenda alterar a política.'
    }
    thresholdFrontier = [ordered]@{
        actualWithinPlusMinus002 = [int]$frontier[0]
        actualMaxBelow = (Parse-Decimal $frontier[1])
        actualMinAtOrAbove = (Parse-Decimal $frontier[2])
        theoreticalClosestStates = $theoretical
    }
    interpretation = [ordered]@{
        scope = 'Evidência sintética DEV; não é estimativa de acurácia municipal nem homologação.'
        negatives = 'Impostores incluem colisões simples e HARD_HOMONYM. Nesta fase DEV, qualquer falso vínculo resolvido reprova o quality gate do harness.'
        frontier = 'A malha teórica mostra se os estados discretos do modelo conseguem sequer ocupar a vizinhança do threshold atual.'
        orderRestriction = 'A auditoria mostra quanto a MLE ordenada alterou m/LLR; pooling grande é diagnóstico de tensão entre estimativas, não evidência adicional de identidade.'
    }
}

[IO.File]::WriteAllText($ReportPath, ($report | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '=== VALIDAÇÃO INDEPENDENTE DO LINKAGE (DEV SINTÉTICO) ==='
Write-Host "Modelo: v$modelVersion / $activeModelId / $algorithmVersion"
Write-Host "Run: $runId"
if ($orderedMleEnabled) {
    Write-Host 'MLE nominal ordenada (auditoria do pooling):'
    foreach ($fieldSummary in $orderRestrictionFields) {
        Write-Host ("  {0}: estados_ajustados={1}/4 max_abs_delta_llr={2}" -f $fieldSummary.field,$fieldSummary.adjustedStates,$fieldSummary.maxAbsDeltaLlr)
        foreach ($state in @($orderRestrictionStates | Where-Object { $_.field -eq $fieldSummary.field })) {
            Write-Host ("    {0}: suporte_m={1} bloco={2} m_irrestrito={3} m_final={4} u={5} llr_irrestrito={6} llr_final={7} delta_llr={8}" -f $state.state,$state.matchedSupport,$state.block,$state.unrestrictedM,$state.restrictedM,$state.u,$state.unrestrictedLlr,$state.restrictedLlr,$state.deltaLlr)
        }
    }
}
Write-Host "Blocking positivo: truthInsideUnion=$($blockingAudit.summary.truthInsideUnion)/$($blockingAudit.summary.sampleSize) recall=$($blockingAudit.summary.unionRecallPct)%"
Write-Host "Positivos: corretos=$positiveCorrect/$positiveTotal errados=$positiveWrong não_resolvidos_ou_conflitos=$positiveUnresolved sensibilidade_sintética=$([decimal]::Round(($positiveSensitivity * [decimal]100),2))%"
Write-Host 'Positivos por cenário:'
foreach ($scenario in $positiveScenarioBreakdown) {
    Write-Host ("  {0}: total={1} corretos={2} errados={3} conflitos={4} não_resolvidos={5} truth_top1={6} truth_top2={7} best=[{8},{9}] second=[{10},{11}]" -f $scenario.scenario,$scenario.total,$scenario.resolvedCorrect,$scenario.resolvedWrong,$scenario.conflicts,$scenario.unresolved,$scenario.truthTop1,$scenario.truthTop2,$scenario.minBestScore,$scenario.maxBestScore,$scenario.minSecondScore,$scenario.maxSecondScore)
}
Write-Host "Negativos: falsos_vínculos=$negativeResolved/$negativeTotal rejeitados_ou_conflitos=$negativeRejected candidatos_expostos=$negativeCandidateExposure especificidade_sintética=$([decimal]::Round(($negativeSpecificity * [decimal]100),2))%"
Write-Host 'Negativos por cenário:'
foreach ($scenario in $negativeScenarioBreakdown) {
    Write-Host ("  {0}: total={1} falsos_vínculos={2} conflitos={3} não_resolvidos={4} expostos={5} score=[{6},{7}]" -f $scenario.scenario,$scenario.total,$scenario.resolvedFalseMatches,$scenario.conflicts,$scenario.unresolved,$scenario.candidateExposure,$scenario.minBestScore,$scenario.maxBestScore)
}
Write-Host 'Perfil teórico de evidência dos negativos:'
foreach ($profile in $negativeScenarioEvidence) {
    Write-Host ("  {0}: {1}/{2}/{3} prior={4} nome_llr={5} mãe_llr={6} nasc_llr={7} posterior={8}" -f $profile.scenario,$profile.nameState,$profile.motherState,$profile.birthState,$profile.priorLogOdds,$profile.nameLlr,$profile.motherLlr,$profile.birthLlr,$profile.theoreticalPosterior)
}
if ($negativeFalseMatchDetails.Count -gt 0) {
    Write-Host 'Falsos vínculos resolvidos:'
    foreach ($item in $negativeFalseMatchDetails) {
        Write-Host ("  {0} | {1} | score={2} segundo={3} margem={4} best={5}" -f $item.scenario,$item.sourceCode,$item.bestScore,$item.secondScore,$item.margin,$item.bestCandidateUuid)
    }
}
Write-Host "Decisões resolvidas combinadas: corretas=$positiveCorrect falsas=$($positiveWrong+$negativeResolved) PPV_sintético=$([decimal]::Round(($syntheticResolvedPpv * [decimal]100),2))%"
Write-Host "Conflito forçado: conflito=$conflictStatus/$conflictTotal margem_zero=$conflictMarginZero acima_threshold=$conflictAboveThreshold resolvidos_indevidos=$conflictResolved"
Write-Host ("Contrafactual sem dual-threshold guard (mantém T={0} e margem_log_odds={1}): positivos_corretos={2}/{3} positivos_errados={4} positivos_conflitos={5} positivos_nao_resolvidos={6} negativos_falsos_vinculos={7}/{8} negativos_conflitos={9} negativos_nao_resolvidos={10} PPV_sintetico={11}%" -f $threshold,$conflictMarginLogOdds,$cfPositiveCorrect,$cfPositiveTotal,$cfPositiveWrong,$cfPositiveConflicts,$cfPositiveUnresolved,$cfNegativeResolved,$cfNegativeTotal,$cfNegativeConflicts,$cfNegativeUnresolved,[decimal]::Round(($cfSyntheticResolvedPpv * [decimal]100),2))
Write-Host "Fronteira T=$threshold`: casos reais ±0,02=$($frontier[0]); max_abaixo=$($frontier[1]); min_acima=$($frontier[2])"
Write-Host 'Estados teóricos mais próximos do threshold:'
$theoretical | ForEach-Object { Write-Host "  $_" }
Write-Host "Relatório: $ReportPath"
Write-Host "Auditoria de blocking: $BlockingAuditPath"
Write-Host ''

if ([decimal]$blockingAudit.summary.unionRecallPct -ne [decimal]100) {
    throw "Fixture positivo não ficou integralmente dentro do blocking: recall=$($blockingAudit.summary.unionRecallPct)%."
}
if ($negativeCandidateExposure -lt 30) {
    throw "Corpus negativo não exerceu candidatos suficientes: exposição=$negativeCandidateExposure/40; esperado >=30."
}
if ($conflictTotal -ne 10 -or $conflictStatus -ne 10 -or $conflictMarginZero -ne 10 -or $conflictAboveThreshold -ne 10 -or $conflictResolved -ne 0) {
    throw "Regra de conflito não foi integralmente exercitada pelo probe: total=$conflictTotal conflito=$conflictStatus margem0=$conflictMarginZero acimaT=$conflictAboveThreshold resolvidos=$conflictResolved."
}

Write-Host 'LINKAGE INDEPENDENT VALIDATION STRUCTURAL GATES: OK' -ForegroundColor Green

if ($positiveWrong -ne 0) {
    throw "DEV QUALITY GATE reprovado: houve $positiveWrong resolução(ões) positiva(s) para UUID incorreto."
}
if ($negativeResolved -ne 0) {
    throw "DEV QUALITY GATE reprovado: houve $negativeResolved falso(s) vínculo(s) resolvido(s) em $negativeTotal negativos independentes."
}

Write-Host 'LINKAGE INDEPENDENT VALIDATION DEV QUALITY GATES: OK' -ForegroundColor Green
