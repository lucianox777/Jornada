#!/usr/bin/env bash
set -euo pipefail
PROFILE="${1:-smoke}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
case "$PROFILE" in
  smoke)   PEOPLE=10000;   PAIRED=6000;  PENDING=5000;   SAMPLE=5000;  POOL=10000 ;;
  medium)  PEOPLE=100000;  PAIRED=20000; PENDING=25000;  SAMPLE=10000; POOL=100000 ;;
  million) PEOPLE=1000000; PAIRED=50000; PENDING=100000; SAMPLE=25000; POOL=500000 ;;
  custom)
    PEOPLE="${JORNADA_SCALE_PEOPLE:?defina JORNADA_SCALE_PEOPLE}"
    PAIRED="${JORNADA_SCALE_PAIRED:?defina JORNADA_SCALE_PAIRED}"
    PENDING="${JORNADA_SCALE_PENDING:?defina JORNADA_SCALE_PENDING}"
    SAMPLE="${JORNADA_SCALE_SAMPLE:-5000}"
    POOL="${JORNADA_SCALE_POOL:-$PEOPLE}"
    ;;
  *) echo "Uso: $0 {smoke|medium|million|custom}" >&2; exit 2 ;;
esac
SEED="${JORNADA_SCALE_SEED:-355}"
COLLISION_MODULO="${JORNADA_SCALE_COLLISION_MODULO:-37}"
BIRTH_SHIFT_MODULO="${JORNADA_SCALE_BIRTH_SHIFT_MODULO:-29}"
PARALLEL="${JORNADA_SCALE_PARALLELISM:-4}"
BATCH="${JORNADA_SCALE_BATCH_SIZE:-10000}"
LOCK_HOLDER_DELAY_MS="${JORNADA_SCALE_LOCK_HOLDER_DELAY_MS:-3000}"
PENDING_SINCE="${JORNADA_SCALE_PENDING_SINCE:-2026-08-31T01:00:00Z}"
BLOCKING_AUDIT_LABEL_COUNT="${JORNADA_SCALE_BLOCKING_AUDIT_LABEL_COUNT:-1000}"
[[ "$BLOCKING_AUDIT_LABEL_COUNT" =~ ^[1-9][0-9]*$ ]] || { echo "ERRO: JORNADA_SCALE_BLOCKING_AUDIT_LABEL_COUNT deve ser inteiro > 0." >&2; exit 2; }
if (( BLOCKING_AUDIT_LABEL_COUNT > PENDING )); then BLOCKING_AUDIT_LABEL_COUNT="$PENDING"; fi
OUTDIR="$ROOT/.local/performance"; mkdir -p "$OUTDIR"
BLOCKING_AUDIT_LABELS="$OUTDIR/scale-${PROFILE}-blocking-labels.csv"
BLOCKING_AUDIT="$OUTDIR/scale-${PROFILE}-blocking-pass-audit.json"

# O harness de escala controla a própria massa. O reset prepara schema+seed sem
# inserir o corpus SCALE canônico de 5k, evitando a colisão determinística 51553.
"$ROOT/scripts/local-db.sh" reset --no-synthetic-corpus
# shellcheck disable=SC1091
set -a; source "$ROOT/.env"; set +a
PORT="${JORNADA_SQL_PORT:-14333}"; DB="${JORNADA_SQL_DATABASE:-JornadaLocal}"
CONN="Server=localhost,$PORT;Database=$DB;User Id=sa;Password=${JORNADA_SQL_SA_PASSWORD};TrustServerCertificate=true;Encrypt=false"
compose() { (cd "$ROOT" && docker compose --env-file "$ROOT/.env" "$@"); }
sqlcmd() {
  compose exec -T -e "SQLCMDPASSWORD=$JORNADA_SQL_SA_PASSWORD" sqlserver \
    /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b "$@"
}
scalar() { sqlcmd -d "$DB" -y 0 -w 65535 -Q "SET NOCOUNT ON; $1" | tr -d '\r' | sed '/^[[:space:]]*$/d' | tail -1 | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//'; }
now_ms() { date +%s%3N; }

cd "$ROOT"
if [[ "${JORNADA_LOCKED_RESTORE:-false}" == "true" ]]; then
  dotnet restore Jornada.sln --locked-mode
else
  dotnet restore Jornada.sln
fi
dotnet build Jornada.sln --configuration Release --no-restore -warnaserror
# Este é um harness local/DEV. Loader, Calibrador, Runner e rebuild devem observar o
# mesmo ambiente do cluster local para que a evidência não dependa do default Production.
export DOTNET_ENVIRONMENT=Development
export ConnectionStrings__Jornada="$CONN"
export PipelineCoordination__HeartbeatSeconds=2
export PipelineCoordination__ExclusiveIntentTimeoutSeconds=5

# O schema 3.70 corrente já contém o contrato de frequências. O snapshot versionado é
# carregado pelo mesmo loader operacional usado na calibração, preservando manifestos,
# SHA-256 e rowCount sem duplicar parsing de NDJSON no harness SQL.
export LinkageParameters__Operation=LOAD_NAME_FREQUENCY_SNAPSHOT
export NameFrequencySnapshot__ManifestPath="$ROOT/data/reference/ibge-nomes-2022/manifest.json"
echo "Carregando referência IBGE canônica para geração da massa SCALE..."
dotnet run --project src/Jornada.Linkage.Parameters.Worker --configuration Release --no-build
ACTIVE_NAME_REFERENCE="$(scalar "SELECT TOP(1) codigo FROM ref.frequencia_nome_versao WHERE status=N'ATIVA';")"
[[ "$ACTIVE_NAME_REFERENCE" == "CENSO2022_NOMES_BRASIL_V1" ]] || { echo "ERRO: referência IBGE ATIVA inesperada após carga: $ACTIVE_NAME_REFERENCE" >&2; exit 4; }
echo "Referência de frequências ativa: $ACTIVE_NAME_REFERENCE"

sqlcmd -d "$DB" -v SCALE_PEOPLE="$PEOPLE" SCALE_PAIRED="$PAIRED" SCALE_PENDING="$PENDING" SCALE_SEED="$SEED" SCALE_COLLISION_MODULO="$COLLISION_MODULO" SCALE_BIRTH_SHIFT_MODULO="$BIRTH_SHIFT_MODULO" -i /workspace/database/Jornada_Dev_SyntheticScale.sql
sqlcmd -d "$DB" -v SCALE_PEOPLE="$PEOPLE" SCALE_SEED="$SEED" SCALE_COLLISION_MODULO="$COLLISION_MODULO" -i /workspace/database/Jornada_Dev_SyntheticScale_Diversify.sql
"$ROOT/scripts/local-db.sh" backfill

echo "Materializando projeção canônica de blocking da massa SCALE antes da calibração..."
Processor__Operation=REBUILD_LOCAL_BLOCKING \
  dotnet run --project src/Jornada.Processor.Worker --configuration Release --no-build
PROJECTED_SCALE_PEOPLE="$(scalar "SELECT COUNT_BIG(*) FROM (SELECT DISTINCT vc.pessoa_uuid FROM silver.pessoa_observacao po JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id WHERE po.codigo_pessoa_origem LIKE N'SCALE-%' AND vc.status='RESOLVIDO' AND vc.pessoa_uuid IS NOT NULL AND EXISTS (SELECT 1 FROM identidade.blocking_chave bc WHERE bc.pessoa_uuid=vc.pessoa_uuid AND bc.vigencia_fim IS NULL)) projected;")"
[[ "$PROJECTED_SCALE_PEOPLE" =~ ^[0-9]+$ ]] || { echo "ERRO: contagem inválida de Pessoas SCALE com blocking: $PROJECTED_SCALE_PEOPLE" >&2; exit 4; }
[[ "$PROJECTED_SCALE_PEOPLE" -eq "$PEOPLE" ]] || { echo "ERRO: projeção de blocking não materializada para toda a massa SCALE: projetadas=$PROJECTED_SCALE_PEOPLE; esperadas=$PEOPLE." >&2; exit 4; }
echo "Projeção de blocking validada: $PROJECTED_SCALE_PEOPLE Pessoas SCALE com chaves correntes."

export LinkageParameters__Operation=GENERATE_DRAFT
export LinkageParameters__RunOnce=true
export LinkageParameters__TrainingSampleSize="$SAMPLE"
export LinkageParameters__TrainingSamplePoolSize="$POOL"
export LinkageParameters__MinimumIndependentMatchedPairs="${JORNADA_SCALE_MIN_MATCHED_PAIRS:-1000}"
export LinkageParameters__ReadCommandTimeoutSeconds="${JORNADA_SCALE_COMMAND_TIMEOUT_SECONDS:-1800}"

P0=$(now_ms)
dotnet run --project src/Jornada.Linkage.Parameters.Worker --configuration Release --no-build
P1=$(now_ms)
PARAM_MS=$((P1-P0))
MODEL_VERSION="$(scalar "SELECT TOP(1) versao FROM identidade.modelo_linkage WHERE status='RASCUNHO' ORDER BY versao DESC;")"
[[ "$MODEL_VERSION" =~ ^[0-9]+$ ]] || { echo "ERRO: versão RASCUNHO não encontrada." >&2; exit 4; }
MODEL_ID="$(scalar "SELECT CONVERT(nvarchar(36),modelo_id) FROM identidade.modelo_linkage WHERE versao=$MODEL_VERSION;")"
[[ "$MODEL_ID" =~ ^[0-9a-fA-F-]{36}$ ]] || { echo "ERRO: modelo_id inválido para evidência de escala." >&2; exit 4; }

export LinkageParameters__Operation=VALIDATE
export LinkageParameters__TargetVersion="$MODEL_VERSION"
dotnet run --project src/Jornada.Linkage.Parameters.Worker --configuration Release --no-build
export LinkageParameters__Operation=ACTIVATE
dotnet run --project src/Jornada.Linkage.Parameters.Worker --configuration Release --no-build

echo "Auditando fan-out efetivo do ruleset calibrado em $BLOCKING_AUDIT_LABEL_COUNT observações SCALE..."
printf 'pessoa_observacao_id,pessoa_uuid_verdade\n' > "$BLOCKING_AUDIT_LABELS"
sqlcmd -d "$DB" -W -h -1 -y 0 -w 65535 -Q "
SET NOCOUNT ON;
WITH pend AS (
    SELECT TOP ($BLOCKING_AUDIT_LABEL_COUNT)
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
 ORDER BY p.n,p.codigo_pessoa_origem;" \
  | tr -d '\r' | sed '/^[[:space:]]*$/d' >> "$BLOCKING_AUDIT_LABELS"
ACTUAL_BLOCKING_LABELS="$(( $(wc -l < "$BLOCKING_AUDIT_LABELS") - 1 ))"
[[ "$ACTUAL_BLOCKING_LABELS" -eq "$BLOCKING_AUDIT_LABEL_COUNT" ]] || { echo "ERRO: rótulos SCALE insuficientes para auditoria de blocking: obtidos=$ACTUAL_BLOCKING_LABELS esperados=$BLOCKING_AUDIT_LABEL_COUNT" >&2; exit 5; }
dotnet run --project src/Jornada.Linkage.Runner --configuration Release --no-build -- \
  --blocking-pass-audit-labels "$BLOCKING_AUDIT_LABELS" \
  --blocking-pass-audit-output "$BLOCKING_AUDIT" \
  --ProbabilisticLinkage:CommandTimeoutSeconds 300
python3 "$ROOT/scripts/blocking-pass-fanout-gate.py" "$BLOCKING_AUDIT" --population "$PEOPLE"
BLOCKING_PASS_PRESSURE_JSON="$(cat "$BLOCKING_AUDIT")"

CORRELATION="$(cat /proc/sys/kernel/random/uuid 2>/dev/null || python3 - <<'PY'
import uuid; print(uuid.uuid4())
PY
)"
R0=$(now_ms)
dotnet run --project src/Jornada.Linkage.Runner --configuration Release --no-build -- \
  --mode MODEL_VALIDATION --model-version "$MODEL_VERSION" --since "$PENDING_SINCE" --max-records "$PENDING" \
  --batch-size "$BATCH" --max-parallelism "$PARALLEL" --publish false \
  --requested-by V373_SCALE_HARNESS --reason "$PROFILE" --correlation-id "$CORRELATION"
R1=$(now_ms)
RUNNER_MS=$((R1-R0))

IFS='|' read -r RUN_STATUS ELIGIBLE EVALUATED RESOLVED UNRESOLVED CONFLICTS NO_CANDIDATE <<< "$(scalar "SELECT CONCAT(status,'|',registros_elegiveis,'|',avaliados,'|',resolvidos,'|',nao_resolvidos,'|',conflitos,'|',sem_candidato_no_bloco) FROM identidade.linkage_run WHERE correlation_id='$CORRELATION';")"
RUN_SCOPE_JSON="$(scalar "SELECT escopo_json FROM identidade.linkage_run WHERE correlation_id='$CORRELATION';")"
[[ -n "$RUN_SCOPE_JSON" ]] || { echo "ERRO: escopo_json do linkage_run ausente." >&2; exit 5; }
DECISION_QUALITY_JSON="$(scalar "DECLARE @run uniqueidentifier=(SELECT linkage_run_id FROM identidade.linkage_run WHERE correlation_id='$CORRELATION'); WITH truth AS (SELECT r.*,po.codigo_pessoa_origem,tv.pessoa_uuid AS truth_uuid FROM identidade.linkage_resultado r JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id JOIN silver.pessoa_observacao tpo ON tpo.codigo_pessoa_origem=REPLACE(po.codigo_pessoa_origem,'SCALE-PEND-','SCALE-SEHAB-') JOIN ref.gestor tg ON tg.gestor_id=tpo.gestor_id AND tg.codigo='SEHAB' JOIN identidade.v_vinculo_corrente tv ON tv.pessoa_observacao_id=tpo.pessoa_observacao_id AND tv.status='RESOLVIDO' WHERE r.linkage_run_id=@run AND po.codigo_pessoa_origem LIKE 'SCALE-PEND-%') SELECT (SELECT COUNT_BIG(*) AS totalScale,SUM(CASE WHEN status='RESOLVIDO' THEN 1 ELSE 0 END) AS resolved,SUM(CASE WHEN status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END) AS resolvedCorrect,SUM(CASE WHEN status='RESOLVIDO' AND (pessoa_uuid_resolvido IS NULL OR pessoa_uuid_resolvido<>truth_uuid) THEN 1 ELSE 0 END) AS falsePositives,SUM(CASE WHEN status='CONFLITO' THEN 1 ELSE 0 END) AS conflicts,SUM(CASE WHEN status='CONFLITO' AND (melhor_candidato_uuid=truth_uuid OR segundo_candidato_uuid=truth_uuid) THEN 1 ELSE 0 END) AS conflictsTruthTop2,SUM(CASE WHEN status='NAO_RESOLVIDO' THEN 1 ELSE 0 END) AS unresolved,SUM(CASE WHEN status='NAO_RESOLVIDO' AND NOT(segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001) AND melhor_candidato_uuid=truth_uuid THEN 1 ELSE 0 END) AS unresolvedTruthFirstWithoutTie,SUM(CASE WHEN status='NAO_RESOLVIDO' AND segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001 AND (melhor_candidato_uuid=truth_uuid OR segundo_candidato_uuid=truth_uuid) THEN 1 ELSE 0 END) AS unresolvedTruthInTop2Tie,SUM(CASE WHEN status='NAO_RESOLVIDO' AND NOT(segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001) AND segundo_candidato_uuid=truth_uuid AND (melhor_candidato_uuid IS NULL OR melhor_candidato_uuid<>truth_uuid) THEN 1 ELSE 0 END) AS unresolvedTruthSecondWithoutTie,SUM(CASE WHEN status='NAO_RESOLVIDO' AND ISNULL(melhor_candidato_uuid,'00000000-0000-0000-0000-000000000000')<>truth_uuid AND ISNULL(segundo_candidato_uuid,'00000000-0000-0000-0000-000000000000')<>truth_uuid THEN 1 ELSE 0 END) AS unresolvedTruthOutsideTop2,SUM(CASE WHEN status='NAO_RESOLVIDO' AND segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001 THEN 1 ELSE 0 END) AS unresolvedTieTop2,CAST(100.0*SUM(CASE WHEN status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END)/NULLIF(SUM(CASE WHEN status='RESOLVIDO' THEN 1 ELSE 0 END),0) AS decimal(9,4)) AS ppvPct,CAST(100.0*SUM(CASE WHEN status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END)/NULLIF(COUNT_BIG(*),0) AS decimal(9,4)) AS sensitivityPct FROM truth FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);")"
[[ -n "$DECISION_QUALITY_JSON" ]] || { echo "ERRO: qualidade contra ground truth SCALE ausente." >&2; exit 5; }

BLOCKING_PRESSURE_JSON="$(scalar "DECLARE @ruleset uniqueidentifier=(SELECT ruleset_id FROM identidade.linkage_ruleset WHERE modelo_id='$MODEL_ID'); SELECT (SELECT (SELECT COUNT(*) FROM identidade.linkage_ruleset_passe WHERE ruleset_id=@ruleset) AS ruleSetPassCount, (SELECT COUNT_BIG(*) FROM identidade.blocking_chave WHERE vigencia_fim IS NULL) AS blockingRows, (SELECT COUNT_BIG(*) FROM (SELECT atributo,valor_normalizado FROM identidade.blocking_chave WHERE vigencia_fim IS NULL GROUP BY atributo,valor_normalizado) d) AS distinctKeys, (SELECT ISNULL(MAX(people_per_key),0) FROM (SELECT COUNT_BIG(DISTINCT pessoa_uuid) people_per_key FROM identidade.blocking_chave WHERE vigencia_fim IS NULL GROUP BY atributo,valor_normalizado) q) AS maxPeoplePerKey, JSON_QUERY((SELECT a.atributo AS attribute, COUNT_BIG(*) AS rows, COUNT_BIG(DISTINCT a.valor_normalizado) AS distinctValues, (SELECT ISNULL(MAX(people_per_value),0) FROM (SELECT COUNT_BIG(DISTINCT b.pessoa_uuid) people_per_value FROM identidade.blocking_chave b WHERE b.vigencia_fim IS NULL AND b.atributo=a.atributo GROUP BY b.valor_normalizado) z) AS maxPeoplePerValue FROM identidade.blocking_chave a WHERE a.vigencia_fim IS NULL GROUP BY a.atributo FOR JSON PATH)) AS attributes FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);")"
[[ -n "$BLOCKING_PRESSURE_JSON" ]] || { echo "ERRO: métricas de pressão de blocking ausentes." >&2; exit 5; }

COORDINATION_PROBE_JSON="$(python3 "$ROOT/scripts/coordination-lock-probe.py" --root "$ROOT" --database "$DB" --delay-ms "$LOCK_HOLDER_DELAY_MS")"
[[ -n "$COORDINATION_PROBE_JSON" ]] || { echo "ERRO: probe sincronizado de coordenação ausente." >&2; exit 6; }

GIT_COMMIT_SHA="$(git -C "$ROOT" rev-parse HEAD | tr '[:upper:]' '[:lower:]' | tr -d '\r\n')"
[[ "$GIT_COMMIT_SHA" =~ ^[0-9a-f]{40}$ ]] || { echo "ERRO: SHA Git inválido para evidência de escala." >&2; exit 5; }

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"; OUT="$OUTDIR/scale-${PROFILE}-${STAMP}.json"
cat > "$OUT" <<JSON
{
  "reportVersion": "LINKAGE_SCALE_EVIDENCE_V2",
  "gitCommitSha": "$GIT_COMMIT_SHA",
  "profile": "$PROFILE",
  "seed": $SEED,
  "collisionModulo": $COLLISION_MODULO,
  "birthShiftModulo": $BIRTH_SHIFT_MODULO,
  "goldPeople": $PEOPLE,
  "pairedPeople": $PAIRED,
  "pendingWithoutCpf": $PENDING,
  "trainingSampleSize": $SAMPLE,
  "trainingPoolSize": $POOL,
  "modelVersion": $MODEL_VERSION,
  "runtimeScope": $RUN_SCOPE_JSON,
  "parametersGenerateMilliseconds": $PARAM_MS,
  "runnerMilliseconds": $RUNNER_MS,
  "blockingPressure": $BLOCKING_PRESSURE_JSON,
  "blockingPassPressure": $BLOCKING_PASS_PRESSURE_JSON,
  "blockingAuditSampleSize": $BLOCKING_AUDIT_LABEL_COUNT,
  "decisionQuality": $DECISION_QUALITY_JSON,
  "coordinationProbe": $COORDINATION_PROBE_JSON,
  "runner": {
    "status": "$RUN_STATUS",
    "eligible": ${ELIGIBLE:-0},
    "evaluated": ${EVALUATED:-0},
    "resolved": ${RESOLVED:-0},
    "unresolved": ${UNRESOLVED:-0},
    "conflicts": ${CONFLICTS:-0},
    "noCandidateInBirthDateBlock": ${NO_CANDIDATE:-0}
  },
  "correlationId": "$CORRELATION",
  "generatedAtUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
}
JSON
LATEST="$OUTDIR/scale-${PROFILE}-latest.json"
cp "$OUT" "$LATEST"
python3 "$ROOT/scripts/performance-evidence-gate.py" "$OUT" --minimum-eligible 1 \
  --baseline "$ROOT/config/hml/performance-baseline.json" \
  --summary "$OUTDIR/scale-${PROFILE}-validation.json"
python3 "$ROOT/scripts/scale-observability-evidence-gate.py" "$OUT"
echo "Scale harness concluído: $OUT"
cat "$OUT"
