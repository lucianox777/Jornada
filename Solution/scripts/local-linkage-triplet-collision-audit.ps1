param()

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'
$OutDir = Join-Path $Root '.local\linkage-triplet-collision-audit'
$ReportPath = Join-Path $OutDir 'triplet-collision-audit.json'

if (-not (Test-Path -LiteralPath $EnvFile)) { throw "Arquivo .env ausente em $Root." }
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker não encontrado no PATH.' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Format-CommandArgument {
    param([Parameter(Mandatory=$true)][AllowEmptyString()][string]$Value)
    if ($Value -notmatch '[\s''"$&|<>]') { return $Value }
    return "'" + $Value.Replace("'", "''") + "'"
}
function Write-CommandLine {
    param([Parameter(Mandatory=$true)][string]$Executable,[string[]]$Arguments=@())
    $tokens=@((Format-CommandArgument $Executable))
    $tokens+=@($Arguments | ForEach-Object { Format-CommandArgument ([string]$_) })
    Write-Host ("# " + ($tokens -join ' ')) -ForegroundColor DarkGray
}
function Get-EnvValue([string]$Name) {
    foreach ($line in Get-Content -LiteralPath $EnvFile) {
        if ($line -match '^\s*#' -or [string]::IsNullOrWhiteSpace($line)) { continue }
        $parts=$line -split '=',2
        if ($parts.Count -eq 2 -and $parts[0].Trim() -eq $Name) { return $parts[1].Trim() }
    }
    return $null
}

$password=Get-EnvValue 'JORNADA_SQL_SA_PASSWORD'
if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD ausente do .env.' }
$db=Get-EnvValue 'JORNADA_SQL_DATABASE'
if ([string]::IsNullOrWhiteSpace($db)) { $db='JornadaLocal' }

function Get-SqlLines([string]$Query) {
    Push-Location $Root
    try {
        $safeArgs=@('compose','--env-file',$EnvFile,'exec','-T','-e','SQLCMDPASSWORD=<redacted>','sqlserver','/opt/mssql-tools18/bin/sqlcmd','-S','localhost','-U','sa','-C','-b','-d',$db,'-W','-h','-1','-s','|','-Q',"SET NOCOUNT ON; $Query")
        Write-CommandLine 'docker' $safeArgs
        $dockerArgs=@('compose','--env-file',$EnvFile,'exec','-T','-e',"SQLCMDPASSWORD=$password",'sqlserver','/opt/mssql-tools18/bin/sqlcmd','-S','localhost','-U','sa','-C','-b','-d',$db,'-W','-h','-1','-s','|','-Q',"SET NOCOUNT ON; $Query")
        $lines=@(& docker @dockerArgs)
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd -Q falhou ($LASTEXITCODE)." }
        return @($lines | ForEach-Object { $_.Trim() } | Where-Object { $_ -and $_ -notmatch '^\([0-9]+ rows? affected\)$' })
    }
    finally { Pop-Location }
}
function Get-SqlScalar([string]$Query) {
    $lines=@(Get-SqlLines $Query)
    if ($lines.Count -eq 0) { return '' }
    return [string]$lines[-1]
}

$activeModelId=Get-SqlScalar "SELECT TOP(1) CONVERT(varchar(36),modelo_id) FROM identidade.modelo_linkage WHERE status='ATIVO' AND ISNULL(amostra_metodo,'')<>'SEED_DEV_FIXO_NAO_TREINADO' ORDER BY versao DESC;"
if ([string]::IsNullOrWhiteSpace($activeModelId)) { throw 'Nenhum modelo calibrado ATIVO.' }
$modelVersion=[int](Get-SqlScalar "SELECT versao FROM identidade.modelo_linkage WHERE modelo_id='$activeModelId';")
$algorithmVersion=Get-SqlScalar "SELECT algoritmo_versao FROM identidade.modelo_linkage WHERE modelo_id='$activeModelId';"

$projectionLine=Get-SqlScalar @"
SELECT CONCAT(rs.projection_schema_version,'|',rs.projection_fingerprint_sha256,'|',m.normalizacao_versao)
FROM identidade.linkage_ruleset rs
JOIN identidade.modelo_linkage m ON m.modelo_id=rs.modelo_id
WHERE rs.modelo_id='$activeModelId';
"@
$projection=$projectionLine.Split('|')
if ($projection.Count -ne 3) { throw "Proveniência da projeção inválida: $projectionLine" }
$projectionSchema=$projection[0]
$projectionFingerprint=$projection[1]
$normalizationVersion=$projection[2]

$metricLine=Get-SqlScalar @"
WITH anchored AS (
    SELECT g.pessoa_uuid,g.data_nascimento,g.nome_completo,g.nome_mae
    FROM gold.pessoa g
    JOIN identidade.cpf_ancora a ON a.pessoa_uuid=g.pessoa_uuid
), eligible AS (
    SELECT *
    FROM anchored
    WHERE nome_completo IS NOT NULL
      AND nome_mae IS NOT NULL
      AND data_nascimento IS NOT NULL
), name_keys AS (
    SELECT pessoa_uuid,
           MIN(valor_normalizado) AS nome_norm,
           COUNT(DISTINCT valor_normalizado) AS key_count
    FROM identidade.blocking_chave
    WHERE normalizacao_versao=N'$normalizationVersion'
      AND projection_schema_version=N'$projectionSchema'
      AND projection_fingerprint_sha256=N'$projectionFingerprint'
      AND atributo=N'name_full'
      AND vigencia_fim IS NULL
    GROUP BY pessoa_uuid
), mother_keys AS (
    SELECT pessoa_uuid,
           MIN(valor_normalizado) AS mae_norm,
           COUNT(DISTINCT valor_normalizado) AS key_count
    FROM identidade.blocking_chave
    WHERE normalizacao_versao=N'$normalizationVersion'
      AND projection_schema_version=N'$projectionSchema'
      AND projection_fingerprint_sha256=N'$projectionFingerprint'
      AND atributo=N'mother_name_full'
      AND vigencia_fim IS NULL
    GROUP BY pessoa_uuid
), covered AS (
    SELECT e.pessoa_uuid,e.data_nascimento,n.nome_norm,m.mae_norm
    FROM eligible e
    JOIN name_keys n ON n.pessoa_uuid=e.pessoa_uuid AND n.key_count=1
    JOIN mother_keys m ON m.pessoa_uuid=e.pessoa_uuid AND m.key_count=1
), triplets AS (
    SELECT nome_norm,mae_norm,data_nascimento,COUNT_BIG(*) AS pessoas
    FROM covered
    GROUP BY nome_norm,mae_norm,data_nascimento
), totals AS (
    SELECT
      (SELECT COUNT_BIG(*) FROM anchored) AS anchored_total,
      (SELECT COUNT_BIG(*) FROM eligible) AS eligible_total,
      (SELECT COUNT_BIG(*) FROM covered) AS covered_total,
      (SELECT COUNT_BIG(*) FROM eligible e LEFT JOIN name_keys n ON n.pessoa_uuid=e.pessoa_uuid WHERE n.pessoa_uuid IS NULL) AS missing_name_key,
      (SELECT COUNT_BIG(*) FROM eligible e LEFT JOIN mother_keys m ON m.pessoa_uuid=e.pessoa_uuid WHERE m.pessoa_uuid IS NULL) AS missing_mother_key,
      (SELECT COUNT_BIG(*) FROM name_keys n JOIN eligible e ON e.pessoa_uuid=n.pessoa_uuid WHERE n.key_count<>1) AS ambiguous_name_key,
      (SELECT COUNT_BIG(*) FROM mother_keys m JOIN eligible e ON e.pessoa_uuid=m.pessoa_uuid WHERE m.key_count<>1) AS ambiguous_mother_key,
      (SELECT COUNT_BIG(*) FROM triplets) AS distinct_triplets,
      (SELECT COUNT_BIG(*) FROM triplets WHERE pessoas>1) AS colliding_triplets,
      COALESCE((SELECT SUM(pessoas) FROM triplets WHERE pessoas>1),0) AS persons_in_collisions,
      COALESCE((SELECT SUM(pessoas-1) FROM triplets WHERE pessoas>1),0) AS excess_persons,
      COALESCE((SELECT SUM((pessoas*(pessoas-1))/2) FROM triplets WHERE pessoas>1),0) AS colliding_pairs,
      COALESCE((SELECT MAX(pessoas) FROM triplets),0) AS max_people_per_triplet
)
SELECT CONCAT(
  anchored_total,'|',eligible_total,'|',covered_total,'|',
  missing_name_key,'|',missing_mother_key,'|',ambiguous_name_key,'|',ambiguous_mother_key,'|',
  distinct_triplets,'|',colliding_triplets,'|',persons_in_collisions,'|',
  excess_persons,'|',colliding_pairs,'|',max_people_per_triplet)
FROM totals;
"@

$p=$metricLine.Split('|')
if ($p.Count -ne 13) { throw "Resultado de colisão inválido: $metricLine" }
$anchored=[long]$p[0]
$eligible=[long]$p[1]
$covered=[long]$p[2]
$missingName=[long]$p[3]
$missingMother=[long]$p[4]
$ambiguousName=[long]$p[5]
$ambiguousMother=[long]$p[6]
$distinctTriplets=[long]$p[7]
$collidingTriplets=[long]$p[8]
$personsInCollisions=[long]$p[9]
$excessPersons=[long]$p[10]
$collidingPairs=[decimal]$p[11]
$maxPerTriplet=[long]$p[12]

$projectionCoverageComplete=($covered -eq $eligible -and $missingName -eq 0 -and $missingMother -eq 0 -and $ambiguousName -eq 0 -and $ambiguousMother -eq 0)
$personCollisionRate=if($covered -eq 0){$null}else{[decimal]$personsInCollisions/[decimal]$covered}
$tripletCollisionRate=if($distinctTriplets -eq 0){$null}else{[decimal]$collidingTriplets/[decimal]$distinctTriplets}
$allPairs=([decimal]$covered*([decimal]$covered-1m))/2m
$randomPairCollisionProbability=if($allPairs -le 0){$null}else{$collidingPairs/$allPairs}

$syntheticAnchored=[long](Get-SqlScalar @"
SELECT COUNT_BIG(DISTINCT a.pessoa_uuid)
FROM identidade.cpf_ancora a
JOIN silver.pessoa_observacao po ON po.cpf=a.cpf
WHERE po.codigo_pessoa_origem LIKE N'SCALE-%'
   OR po.codigo_pessoa_origem LIKE N'SEED-%';
"@)
$datasetHint=if($anchored -gt 0 -and $syntheticAnchored -eq $anchored){'SYNTHETIC_LOCAL'}elseif($syntheticAnchored -gt 0){'MIXED_WITH_SYNTHETIC'}else{'NO_SYNTHETIC_MARKER_DETECTED'}

$report=[ordered]@{
    generatedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
    purpose='READ_ONLY_GOLD_ANCHORED_TRIPLET_COLLISION_AUDIT'
    changesPolicy=$false
    changesScoring=$false
    exposesPii=$false
    triplet=[ordered]@{
        fields=@('NOME_NORMALIZADO','NOME_MAE_NORMALIZADO','DATA_NASCIMENTO')
        normalizationSource='CURRENT_BLOCKING_PROJECTION'
        nameFeature='name_full'
        motherNameFeature='mother_name_full'
    }
    modelProjection=[ordered]@{
        modelId=$activeModelId
        modelVersion=$modelVersion
        algorithmVersion=$algorithmVersion
        normalizationVersion=$normalizationVersion
        projectionSchemaVersion=$projectionSchema
        projectionFingerprintSha256=$projectionFingerprint
    }
    coverage=[ordered]@{
        anchoredGoldPersons=$anchored
        completeTripletPersons=$eligible
        coveredByCurrentProjection=$covered
        missingCurrentNameKey=$missingName
        missingCurrentMotherNameKey=$missingMother
        ambiguousCurrentNameKey=$ambiguousName
        ambiguousCurrentMotherNameKey=$ambiguousMother
        projectionCoverageComplete=$projectionCoverageComplete
    }
    collisions=[ordered]@{
        distinctTriplets=$distinctTriplets
        collidingTriplets=$collidingTriplets
        personsInCollidingTriplets=$personsInCollisions
        excessPersonsBeyondOnePerTriplet=$excessPersons
        collidingCpfPairs=$collidingPairs
        maxPersonsPerTriplet=$maxPerTriplet
        personCollisionRate=if($null -eq $personCollisionRate){$null}else{[decimal]::Round($personCollisionRate,12)}
        tripletCollisionRate=if($null -eq $tripletCollisionRate){$null}else{[decimal]::Round($tripletCollisionRate,12)}
        randomPairCollisionProbability=if($null -eq $randomPairCollisionProbability){$null}else{[decimal]::Round($randomPairCollisionProbability,16)}
    }
    dataset=[ordered]@{
        syntheticAnchoredPersons=$syntheticAnchored
        hint=$datasetHint
        municipalPrevalenceClaimAllowed=$false
    }
    interpretation=@(
        'A auditoria conta CPFs/UUIDs âncora distintos que compartilham exatamente nome normalizado, nome da mãe normalizado e data de nascimento.',
        'Nenhum nome, CPF, UUID ou data individual é gravado no relatório.',
        'A taxa só pode ser interpretada como prevalência municipal se a base executada for representativa e a cobertura da projeção for completa.',
        'Resultado em corpus DEV/sintético mede apenas o corpus sintético e não autoriza relaxar o dual-threshold guard.'
    )
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $ReportPath -Encoding UTF8

Write-Host ''
Write-Host '=== COLISÃO DA TRIPLA NA GOLD ANCORADA POR CPF (READ-ONLY) ==='
Write-Host "Modelo/projeção: v$modelVersion / $activeModelId / $projectionSchema"
Write-Host "Gold ancorada=$anchored; tripla completa=$eligible; coberta_projeção=$covered; cobertura_completa=$projectionCoverageComplete"
Write-Host "Triplas distintas=$distinctTriplets; triplas_colidentes=$collidingTriplets; pessoas_em_colisão=$personsInCollisions; pares_CPF_colidentes=$collidingPairs; máximo_por_tripla=$maxPerTriplet"
$personRateText=if($null -eq $personCollisionRate){'N/A'}else{"$([decimal]::Round($personCollisionRate*100m,8))%"}
$pairRateText=if($null -eq $randomPairCollisionProbability){'N/A'}else{"$([decimal]::Round($randomPairCollisionProbability*100m,12))%"}
Write-Host "Taxa de pessoas em tripla colidente=$personRateText; probabilidade de colisão entre dois CPFs aleatórios=$pairRateText"
Write-Host "Dataset hint=$datasetHint; isto não é automaticamente uma estimativa municipal."
Write-Host "Relatório agregado sem PII: $ReportPath"
Write-Host ''
Write-Host 'LINKAGE GOLD TRIPLET COLLISION AUDIT: OK'
