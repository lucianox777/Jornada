#!/usr/bin/env bash
# C3.2d: real SQL-only acceptance in a unique GitHub-hosted ephemeral runner.
# Do not use or touch the ordinary JornadaLocal Compose, volumes or database.
set -euo pipefail
die() { echo "C3.2d SQL acceptance: $*" >&2; exit 2; }
[[ $# -eq 0 ]] || die 'unexpected arguments'
[[ "${GITHUB_ACTIONS:-}" == true && "${CI:-}" == true &&
   "${GITHUB_REPOSITORY:-}" == 'lucianox777/Jornada' &&
   "${JORNADA_WORKERS_E2E_RUNTIME_TEST:-}" == true ]] || die 'CI-only opt-in required'
[[ "${GITHUB_RUN_ID:-}" =~ ^[0-9]{6,16}$ &&
   "${GITHUB_RUN_ATTEMPT:-}" =~ ^[0-9]{1,3}$ ]] || die 'missing unique CI identity'
for tool in docker openssl python3; do command -v "$tool" >/dev/null || die "missing $tool"; done
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
python3 scripts/e2e-private-sql-runtime-preflight.py
export JORNADA_WORKERS_E2E_ID="ci${GITHUB_RUN_ID}${GITHUB_RUN_ATTEMPT}"
export JORNADA_WORKERS_E2E_SQL_PASSWORD="Aa9!$(openssl rand -hex 24)"
PROJECT="jornada-workers-e2e-${JORNADA_WORKERS_E2E_ID}"
[[ "$JORNADA_WORKERS_E2E_ID" =~ ^[a-z0-9]{8,32}$ ]] || die 'invalid sandbox name'
COMPOSE="$ROOT/install/console-dev-e2e/docker-compose.workers.yml"
OUT="$ROOT/.local/e2e/c3-2d-sql-bootstrap"
mkdir -p "$OUT"
compose() { docker compose --env-file /dev/null -p "$PROJECT" -f "$COMPOSE" "$@"; }

# A pre-existing resource with this Compose label means a collision. Fail closed.
for kind in container volume network; do
  case "$kind" in
    container) found="$(docker ps -aq --filter "label=com.docker.compose.project=$PROJECT")" ;;
    volume) found="$(docker volume ls -q --filter "label=com.docker.compose.project=$PROJECT")" ;;
    network) found="$(docker network ls -q --filter "label=com.docker.compose.project=$PROJECT")" ;;
  esac
  [[ -z "$found" ]] || die "refusing existing project resources: $kind"
done

# ONLY SQL and its one-shot bootstrap; no app-image build or resident workers.
# No volume cleanup is attempted: the GitHub-hosted runner is ephemeral.
compose up -d --build sql-bootstrap > "$OUT/start.log" 2>&1 || {
  compose logs --no-color --tail 80 sqlserver sql-bootstrap > "$OUT/diagnostic.log" 2>&1 || true
  die 'private bootstrap did not start'
}
boot_cid="$(compose ps -aq sql-bootstrap)"
sql_cid="$(compose ps -q sqlserver)"
[[ -n "$boot_cid" && -n "$sql_cid" ]] || die 'private containers missing'
for pair in "$boot_cid:sql-bootstrap" "$sql_cid:sqlserver"; do
  cid="${pair%%:*}"; service="${pair##*:}"
  [[ "$(docker inspect -f '{{ index .Config.Labels "com.docker.compose.project" }}' "$cid")" == "$PROJECT" &&
     "$(docker inspect -f '{{ index .Config.Labels "com.docker.compose.service" }}' "$cid")" == "$service" ]] ||
    die 'container identity mismatch'
done

export SQLCMDPASSWORD="$JORNADA_WORKERS_E2E_SQL_PASSWORD"
sql() {
  docker exec -e SQLCMDPASSWORD "$sql_cid" /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -C -b -I -l 10 -d JornadaE2E "$@"
}
state=''
for _ in $(seq 1 120); do
  state="$(docker inspect -f '{{.State.Status}}:{{.State.ExitCode}}' "$boot_cid")"
  case "$state" in
    exited:0) break ;;
    exited:*) compose logs --no-color --tail 80 sql-bootstrap > "$OUT/diagnostic.log" 2>&1 || true
              die "bootstrap process failed: $state" ;;
    created:*|running:*|restarting:*) sleep 5 ;;
    *) die "unexpected bootstrap state $state" ;;
  esac
done
[[ "$state" == exited:0 ]] || die 'bootstrap timed out'

sql -Q "SET NOCOUNT ON;
IF DB_NAME()<>N'JornadaE2E' THROW 51601, 'Wrong database', 1;
IF OBJECT_ID(N'ingestao.entrega', N'U') IS NULL THROW 51602,'No ingestao',1;
IF OBJECT_ID(N'silver.pessoa_observacao', N'U') IS NULL THROW 51603,'No Silver',1;
IF OBJECT_ID(N'ingestao.lote_heartbeat', N'U') IS NULL THROW 51604,'No heartbeat',1;
IF NOT EXISTS (SELECT 1 FROM ref.gestor WHERE codigo=N'SMS') THROW 51605,'No DEV seed',1;
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.EnvironmentProfile' AND CONVERT(NVARCHAR(100),value)=N'Development') THROW 51606,'No DEV marker',1;" > "$OUT/schema-validation.log"
before="$(sql -W -h -1 -Q 'SET NOCOUNT ON; SELECT COUNT_BIG(*) FROM ref.gestor;' | tr -d '[:space:]\r')"
[[ "$before" =~ ^[0-9]+$ && "$before" -gt 0 ]] || die 'bad seed count'
# The intentionally failed replay creates a one-off Compose container.
# Always --rm it even on non-zero exit: otherwise the new supervisor
# correctly refuses global ON, detecting a leftover oneoff=True container.
if compose run --rm --no-deps sql-bootstrap > "$OUT/replay-denial.log" 2>&1; then
  die 'second bootstrap unexpectedly succeeded'
fi
grep -F 'banco JornadaE2E já existe' "$OUT/replay-denial.log" >/dev/null ||
  die 'second bootstrap refused for unexpected reason'
after="$(sql -W -h -1 -Q 'SET NOCOUNT ON; SELECT COUNT_BIG(*) FROM ref.gestor;' | tr -d '[:space:]\r')"
[[ "$before" == "$after" ]] || die 'second bootstrap modified seed'

# C3.2e: build only the app image from the canonical Dockerfile and start
# the TWO APIs in the SAME private E2E project. The three workers remain OFF.
# --no-deps is permitted because this script already checked SQL bootstrap.
compose build api > "$OUT/api-image-build.log" 2>&1 || die 'isolated API image build failed'
compose up -d --no-build --no-deps api resultado-api > "$OUT/api-start.log" 2>&1 ||
  die 'independent API services failed to start'

api_cid="$(compose ps -q api)"
resultado_cid="$(compose ps -q resultado-api)"
[[ -n "$api_cid" && -n "$resultado_cid" && "$api_cid" != "$resultado_cid" &&
   "$api_cid" != "$sql_cid" && "$resultado_cid" != "$sql_cid" ]] ||
  die 'API/ResultadoApi/SQL must be independent containers'
for pair in "$api_cid:api" "$resultado_cid:resultado-api"; do
  cid="${pair%%:*}"; service="${pair##*:}"
  [[ "$(docker inspect -f '{{ index .Config.Labels "com.docker.compose.project" }}' "$cid")" == "$PROJECT" &&
     "$(docker inspect -f '{{ index .Config.Labels "com.docker.compose.service" }}' "$cid")" == "$service" ]] ||
    die 'API container not part of the disposable sandbox'
done

# Liveness alone is insufficient: API readiness must prove SQL/schema access.
# Check both health endpoints INSIDE the private Docker network, no host ports.
ready=''
for _ in $(seq 1 90); do
  api_health="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$api_cid")"
  result_health="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$resultado_cid")"
  if [[ "$api_health" == healthy && "$result_health" == healthy ]] &&
     docker exec "$api_cid" curl --fail --silent --show-error        http://127.0.0.1:5080/health/ready > "$OUT/api-readiness.json" &&
     docker exec "$resultado_cid" curl --fail --silent --show-error        http://127.0.0.1:5081/health > "$OUT/resultado-health.json" &&
     docker exec "$resultado_cid" curl --fail --silent --show-error        http://api:5080/health/ready > "$OUT/resultado-to-api-readiness.json"; then
    ready=true
    break
  fi
  sleep 5
done
[[ "$ready" == true ]] || die 'independent API/ResultadoApi readiness or private DNS failed'
python3 - "$OUT/api-readiness.json" "$OUT/resultado-health.json"     "$OUT/resultado-to-api-readiness.json" <<'PY'
import json, sys
api = json.load(open(sys.argv[1], encoding='utf-8'))
resultado = json.load(open(sys.argv[2], encoding='utf-8'))
upstream = json.load(open(sys.argv[3], encoding='utf-8'))
assert api.get('status') == 'ready' and upstream.get('status') == 'ready', 'API SQL/schema not ready'
assert resultado.get('status') == 'ok', 'ResultadoApi not live'
PY

# Explicit OFF semantics: zero resident workers, independent APIs and SQL still alive.
for worker in processor operations-maintenance bronze-maintenance; do
  [[ -z "$(docker ps -aq --filter "label=com.docker.compose.project=$PROJECT" --filter "label=com.docker.compose.service=$worker")" ]] ||
    die "worker $worker exists in initial OFF mode"
done
for cid in "$sql_cid" "$api_cid" "$resultado_cid"; do
  [[ "$(docker inspect -f '{{.State.Running}}' "$cid")" == true ]] ||
    die 'SQL/API/Resultado unexpectedly stopped in OFF mode'
done
api_pid="$(docker inspect -f '{{.State.Pid}}' "$api_cid")"
resultado_pid="$(docker inspect -f '{{.State.Pid}}' "$resultado_cid")"
[[ "$api_pid" =~ ^[0-9]+$ && "$resultado_pid" =~ ^[0-9]+$ &&
   "$api_pid" != "$resultado_pid" && "$api_pid" -gt 1 && "$resultado_pid" -gt 1 ]] ||
  die 'independent resident API PID1 evidence missing'

# C3.3a: initial OFF must be visible through the real DevConsole HTTP GET
# reading private Docker labels/PIDs, SQL heartbeat and API readiness.
# Starts only a short-lived local Console observer; no Docker build or mutation.
python3 scripts/e2e-console-worker-supervisor-status.py OFF

# C3.2f1: after private SQL and two APIs are ready, exercise only
# the three independent resident workers; this is NOT the lot-recovery gate.
bash scripts/e2e-private-worker-restart-runtime.sh

# C3.2f2b: real API→Processor→SQL synthetic ZIP and idempotency baseline.
# No crash/transaction rollback exercised here: those require separate C3.2f2c.
bash scripts/e2e-private-api-ingestion-baseline-runtime.sh

# C3.2f2c: actual interrupted SQL Serializable transaction/lease recovery,
# confined to THIS already validated disposable Compose project. No new build.
python3 scripts/e2e-private-recovery-runtime.py

# The same read-only endpoint must report all three live workers after
# SIGKILL/restart/real rollback recovery without losing the APIs.
python3 scripts/e2e-console-worker-supervisor-status.py ON

# C3.3b1: ON→OFF→ON only through the loopback Console backend using the
# existing private Compose image/network. OFF DISARMS restart before stop;
# ON recreates only the three workers, SQL/2 APIs keep their PIDs.
python3 scripts/e2e-console-supervisor-global-toggle.py

# C3.3b2: with supervisor ON, verify all finite commands are rejected.
# Switch OFF, execute exactly one isolated job for each worker, confirm zero
# residents and independent SQL/APIs, then restore ON; no new build/container
# other than disposable oneoff worker jobs in this same private project.
python3 scripts/e2e-console-supervisor-three-runonce.py

# C3.3b3a: losing the browser TCP connection must NOT kill/untrack a
# live private oneoff. Observe actual oneoff=True then abort the HTTP
# client; ON must remain blocked until finite exit and --rm verified.
python3 scripts/e2e-console-runonce-disconnect-proof.py

python3 - "$OUT/summary.json" "$PROJECT" "$before" <<'PY'
import json, pathlib, sys
output, project, seed_count = sys.argv[1:]
pathlib.Path(output).write_text(json.dumps({
  'status':'PASS', 'scenario':'C3.2d/E2E SQL bootstrap plus APIs, independent workers and synthetic ingestion',
  'project':project, 'database':'JornadaE2E',
  'seed_gestor_count':int(seed_count),
  'create_only_second_run':'REJECTED_EXISTING_DB',
  'api_ready':True, 'resultado_api_ready':True,
  'resultado_to_api_private_dns':'PASS', 'initial_off_worker_residents':0,
  'subsequent_synthetic_ingestion_baseline':'PASS',
  'real_crash_recovery_and_fencing':'PASS',
  'processing_recovery_verified':True,
  'real_console_global_on_off_on':'PASS',
  'real_three_worker_runonce':'PASS',
  'http_abort_does_not_orphan_runonce':'PASS'
}, indent=2) + '\n', encoding='utf-8')
PY
echo 'C3.2d SQL-only operational gate: PASS'
