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

"$ROOT/scripts/local-db.sh" reset
# shellcheck disable=SC1091
set -a; source "$ROOT/.env"; set +a
PORT="${JORNADA_SQL_PORT:-14333}"; DB="${JORNADA_SQL_DATABASE:-JornadaLocal}"
CONN="Server=localhost,$PORT;Database=$DB;User Id=sa;Password=${JORNADA_SQL_SA_PASSWORD};TrustServerCertificate=true;Encrypt=false"
compose() { (cd "$ROOT" && docker compose --env-file "$ROOT/.env" "$@"); }
sqlcmd() {
  compose exec -T -e "SQLCMDPASSWORD=$JORNADA_SQL_SA_PASSWORD" sqlserver \
    /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b "$@"
}
scalar() { sqlcmd -d "$DB" -h -1 -y 0 -w 65535 -Q "SET NOCOUNT ON; $1" | tr -d '\r' | sed '/^[[:space:]]*$/d' | tail -1 | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//'; }
now_ms() { date +%s%3N; }
probe_lock() {
  local resource="$1"
  local delay_ms="$2"
  local delay_seconds
  delay_seconds="$(python3 - "$delay_ms" <<'PY'
import sys
ms = int(sys.argv[1])
seconds, rem = divmod(ms, 1000)
print(f"00:00:{seconds:02d}.{rem:03d}")
PY
)"
  sqlcmd -d "$DB" -Q "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'$resource',@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; IF @r<0 THROW 51990,'probe holder lock failed',1; WAITFOR DELAY '$delay_seconds'; DECLARE @release int; EXEC @release=sys.sp_releaseapplock @Resource=N'$resource',@LockOwner='Session';" >/dev/null &
  local holder_pid=$!
  sleep 0.25
  local t0 t1 result
  t0=$(now_ms)
  result="$(scalar "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'$resource',@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=10000; DECLARE @release int; IF @r>=0 EXEC @release=sys.sp_releaseapplock @Resource=N'$resource',@LockOwner='Session'; SELECT @r;")"
  t1=$(now_ms)
  wait "$holder_pid"
  [[ "$result" =~ ^-?[0-9]+$ ]] || { echo "ERRO: resultado de applock inválido para $resource: $result" >&2; exit 6; }
  printf '%s|%s\n' "$result" "$((t1-t0))"
}

sqlcmd -d "$DB" -v SCALE_PEOPLE="$PEOPLE" SCALE_PAIRED="$PAIRED" SCALE_PENDING="$PENDING" SCALE_SEED="$SEED" SCALE_COLLISION_MODULO="$COLLISION_MODULO" SCALE_BIRTH_SHIFT_MODULO="$BIRTH_SHIFT_MODULO" -i /workspace/database/Jornada_Dev_SyntheticScale.sql

cd "$ROOT"
if [[ "${JORNADA_LOCKED_RESTORE:-false}" == "true" ]]; then
  dotnet restore Jornada.sln --locked-mode
else
  dotnet restore Jornada.sln
fi
dotnet build Jornada.sln --configuration Release --no-restore -warnaserror
export ConnectionStrings__Jornada="$CONN"
export PipelineCoordination__HeartbeatSeconds=2
export PipelineCoordination__ExclusiveIntentTimeoutSeconds=5
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

CORRELATION="$(cat /proc/sys/kernel/random/uuid 2>/dev/null || python3 - <<'PY'
import uuid; print(uuid.uuid4())
PY
)"
R0=$(now_ms)
dotnet run --project src/Jornada.Linkage.Runner --configuration Release --no-build -- \
  --mode MODEL_VALIDATION --model-version "$MODEL_VERSION" --max-records "$PENDING" \
  --batch-size "$BATCH" --max-parallelism "$PARALLEL" --publish false \
  --requested-by V373_SCALE_HARNESS --reason "$PROFILE" --correlation-id "$CORRELATION"
R1=$(now_ms)
RUNNER_MS=$((R1-R0))

IFS='|' read -r RUN_STATUS ELIGIBLE EVALUATED RESOLVED UNRESOLVED CONFLICTS NO_CANDIDATE <<< "$(scalar "SELECT CONCAT(status,'|',registros_elegiveis,'|',avaliados,'|',resolvidos,'|',nao_resolvidos,'|',conflitos,'|',sem_candidato_no_bloco) FROM identidade.linkage_run WHERE correlation_id='$CORRELATION';")"
RUN_SCOPE_JSON="$(scalar "SELECT escopo_json FROM identidade.linkage_run WHERE correlation_id='$CORRELATION';")"
[[ -n "$RUN_SCOPE_JSON" ]] || { echo "ERRO: escopo_json do linkage_run ausente." >&2; exit 5; }

BLOCKING_PRESSURE_JSON="$(scalar "DECLARE @ruleset uniqueidentifier=(SELECT ruleset_id FROM identidade.linkage_ruleset WHERE modelo_id='$MODEL_ID'); SELECT (SELECT (SELECT COUNT(*) FROM identidade.linkage_ruleset_passe WHERE ruleset_id=@ruleset) AS ruleSetPassCount, (SELECT COUNT_BIG(*) FROM identidade.blocking_chave WHERE vigencia_fim IS NULL) AS blockingRows, (SELECT COUNT_BIG(*) FROM (SELECT atributo,valor_normalizado FROM identidade.blocking_chave WHERE vigencia_fim IS NULL GROUP BY atributo,valor_normalizado) d) AS distinctKeys, (SELECT ISNULL(MAX(people_per_key),0) FROM (SELECT COUNT_BIG(DISTINCT pessoa_uuid) people_per_key FROM identidade.blocking_chave WHERE vigencia_fim IS NULL GROUP BY atributo,valor_normalizado) q) AS maxPeoplePerKey, JSON_QUERY((SELECT a.atributo AS attribute, COUNT_BIG(*) AS rows, COUNT_BIG(DISTINCT a.valor_normalizado) AS distinctValues, (SELECT ISNULL(MAX(people_per_value),0) FROM (SELECT COUNT_BIG(DISTINCT b.pessoa_uuid) people_per_value FROM identidade.blocking_chave b WHERE b.vigencia_fim IS NULL AND b.atributo=a.atributo GROUP BY b.valor_normalizado) z) AS maxPeoplePerValue FROM identidade.blocking_chave a WHERE a.vigencia_fim IS NULL GROUP BY a.atributo FOR JSON PATH)) AS attributes FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);")"
[[ -n "$BLOCKING_PRESSURE_JSON" ]] || { echo "ERRO: métricas de pressão de blocking ausentes." >&2; exit 5; }

IFS='|' read -r EXCLUSIVE_RESULT EXCLUSIVE_WAIT_MS <<< "$(probe_lock 'Jornada.Pipeline.ExclusiveRequest' "$LOCK_HOLDER_DELAY_MS")"
IFS='|' read -r CORPUS_RESULT CORPUS_WAIT_MS <<< "$(probe_lock 'Jornada.Pipeline.Corpus' "$LOCK_HOLDER_DELAY_MS")"

GIT_COMMIT_SHA="$(git -C "$ROOT" rev-parse HEAD | tr '[:upper:]' '[:lower:]' | tr -d '\r\n')"
[[ "$GIT_COMMIT_SHA" =~ ^[0-9a-f]{40}$ ]] || { echo "ERRO: SHA Git inválido para evidência de escala." >&2; exit 5; }

OUTDIR="$ROOT/.local/performance"; mkdir -p "$OUTDIR"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"; OUT="$OUTDIR/scale-${PROFILE}-${STAMP}.json"
cat > "$OUT" <<JSON
{
  "reportVersion": "LINKAGE_SCALE_EVIDENCE_V1",
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
  "coordinationProbe": {
    "holderDelayMilliseconds": $LOCK_HOLDER_DELAY_MS,
    "exclusiveRequest": {
      "resource": "Jornada.Pipeline.ExclusiveRequest",
      "lockResult": $EXCLUSIVE_RESULT,
      "waitMilliseconds": $EXCLUSIVE_WAIT_MS
    },
    "corpus": {
      "resource": "Jornada.Pipeline.Corpus",
      "lockResult": $CORPUS_RESULT,
      "waitMilliseconds": $CORPUS_WAIT_MS
    }
  },
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
