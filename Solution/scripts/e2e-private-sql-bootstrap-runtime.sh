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
if compose run --no-deps sql-bootstrap > "$OUT/replay-denial.log" 2>&1; then
  die 'second bootstrap unexpectedly succeeded'
fi
grep -F 'banco JornadaE2E já existe' "$OUT/replay-denial.log" >/dev/null ||
  die 'second bootstrap refused for unexpected reason'
after="$(sql -W -h -1 -Q 'SET NOCOUNT ON; SELECT COUNT_BIG(*) FROM ref.gestor;' | tr -d '[:space:]\r')"
[[ "$before" == "$after" ]] || die 'second bootstrap modified seed'

python3 - "$OUT/summary.json" "$PROJECT" "$before" <<'PY'
import json, pathlib, sys
output, project, seed_count = sys.argv[1:]
pathlib.Path(output).write_text(json.dumps({
  'status':'PASS', 'scenario':'C3.2d private SQL bootstrap only',
  'project':project, 'database':'JornadaE2E',
  'seed_gestor_count':int(seed_count),
  'create_only_second_run':'REJECTED_EXISTING_DB',
  'api_and_workers_started':False
}, indent=2) + '\n', encoding='utf-8')
PY
echo 'C3.2d SQL-only operational gate: PASS'
