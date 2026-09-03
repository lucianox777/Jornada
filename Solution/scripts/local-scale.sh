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
scalar() { sqlcmd -d "$DB" -h -1 -W -Q "SET NOCOUNT ON; $1" | tr -d '\r' | sed '/^[[:space:]]*$/d' | tail -1; }
now_ms() { date +%s%3N; }

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
OUTDIR="$ROOT/.local/performance"; mkdir -p "$OUTDIR"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"; OUT="$OUTDIR/scale-${PROFILE}-${STAMP}.json"
cat > "$OUT" <<JSON
{
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
  "parametersGenerateMilliseconds": $PARAM_MS,
  "runnerMilliseconds": $RUNNER_MS,
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
echo "Scale harness concluído: $OUT"
cat "$OUT"
