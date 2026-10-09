#!/usr/bin/env bash
# C3.2f1 — prove per-worker restart in the already-validated private CI E2E sandbox.
# This is an ADDITIVE test helper, inert until a separate PR wires it to E2E.
# It does not prove resumed processing; that needs a distinct synthetic-lot gate.
set -euo pipefail
die() { printf 'C3.2f1: %s\n' "$*" >&2; exit 2; }

[[ $# -eq 0 ]] || die 'no arguments allowed'
[[ "${GITHUB_ACTIONS:-}" == true && "${CI:-}" == true &&
   "${GITHUB_REPOSITORY:-}" == 'lucianox777/Jornada' &&
   "${JORNADA_WORKERS_E2E_RUNTIME_TEST:-}" == true ]] ||
  die 'GitHub-hosted opt-in CI is mandatory'
[[ "${GITHUB_RUN_ID:-}" =~ ^[0-9]{6,16}$ &&
   "${GITHUB_RUN_ATTEMPT:-}" =~ ^[0-9]{1,3}$ ]] ||
  die 'validated unique run identity required'
EXPECTED_ID="ci${GITHUB_RUN_ID}${GITHUB_RUN_ATTEMPT}"
[[ "${JORNADA_WORKERS_E2E_ID:-}" == "$EXPECTED_ID" ]] ||
  die 'cannot select an arbitrary E2E project'
[[ -n "${JORNADA_WORKERS_E2E_SQL_PASSWORD:-}" ]] ||
  die 'dedicated disposable SQL secret required'
[[ "${JORNADA_WORKERS_E2E_IMAGE_TAG:-test}" == test ]] ||
  die 'image tag override is not permitted in CI acceptance'
for bin in docker python3; do
  command -v "$bin" >/dev/null || die "missing $bin"
done

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE="$ROOT/install/console-dev-e2e/docker-compose.workers.yml"
PROJECT="jornada-workers-e2e-$EXPECTED_ID"
OUT="$ROOT/.local/e2e/c3-2f1-worker-restart"
mkdir -p "$OUT"
cd "$ROOT"
compose() {
  docker compose --env-file /dev/null --profile continuous \
    -p "$PROJECT" -f "$COMPOSE" "$@"
}
label() {
  docker inspect -f '{{ index .Config.Labels "com.docker.compose.project" }}:{{ index .Config.Labels "com.docker.compose.service" }}' "$1"
}
is_running() {
  [[ "$(docker inspect -f '{{.State.Running}}' "$1")" == true ]]
}
get_pid() {
  docker inspect -f '{{.State.Pid}}' "$1"
}
get_restart_count() {
  docker inspect -f '{{.RestartCount}}' "$1"
}
# Restrict ALL mutable operations to known Compose service IDs and project labels.
# Bootstrap was already verified by the parent E2E gate; its deliberate
# replay-denial one-off container may coexist with the successful one-shot.
for service in sqlserver api resultado-api; do
  cid="$(compose ps -aq "$service")"
  [[ -n "$cid" && "$(label "$cid")" == "$PROJECT:$service" ]] ||
    die "missing verified isolated $service"
done
sql_cid="$(compose ps -q sqlserver)"
api_cid="$(compose ps -q api)"
resultado_cid="$(compose ps -q resultado-api)"
for cid in "$sql_cid" "$api_cid" "$resultado_cid"; do
  is_running "$cid" || die 'SQL and both APIs must be running before the test'
done
docker exec "$api_cid" curl -fsS http://127.0.0.1:5080/health/ready \
  > "$OUT/api-ready-before.json" || die 'private API not SQL-ready'
docker exec "$resultado_cid" curl -fsS http://127.0.0.1:5081/health \
  > "$OUT/resultado-live-before.json" || die 'private ResultadoApi not live'
for service in processor operations-maintenance bronze-maintenance; do
  [[ -z "$(docker ps -aq --filter "label=com.docker.compose.project=$PROJECT" \
    --filter "label=com.docker.compose.service=$service")" ]] ||
    die "refusing pre-existing resident $service; OFF prerequisite violated"
done

export SQLCMDPASSWORD="$JORNADA_WORKERS_E2E_SQL_PASSWORD"
heartbeat() {
  local component="$1"
  # component argument is an INTERNAL allowlisted value, never user input.
  docker exec -e SQLCMDPASSWORD "$sql_cid" /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -C -b -I -l 10 -d JornadaE2E -W -h -1 \
    -Q "SET NOCOUNT ON; SELECT ISNULL((SELECT TOP (1) CONVERT(VARCHAR(36), instance_id)
        FROM controle.runtime_componente
        WHERE node_id=N'NODE2' AND componente=N'$component'
        AND status=N'RUNNING'
        AND heartbeat_em >= DATEADD(SECOND, -45, SYSUTCDATETIME())), N'-');" \
    | tr -d '[:space:]\r'
}
valid_uuid() {
  [[ "$1" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]
}
services=(processor operations-maintenance bronze-maintenance)
components=(Processor OperationsMaintenance BronzeMaintenance)
declare -A ids pids counts instances
# Build was already completed and SQL/APIs verified by C3.2e in this job.
compose up -d --no-build --no-deps "${services[@]}" > "$OUT/start.log" 2>&1 ||
  die 'private workers failed to start'
for i in "${!services[@]}"; do
  service="${services[$i]}"
  cid="$(compose ps -q "$service")"
  [[ -n "$cid" && "$(label "$cid")" == "$PROJECT:$service" &&
     "$(docker inspect -f '{{.HostConfig.RestartPolicy.Name}}' "$cid")" == unless-stopped ]] ||
    die "worker not independently supervised: $service"
  ids["$service"]="$cid"
done

wait_heartbeat() {
  local service="$1" component="$2" previous="$3"
  local current=''
  for _ in $(seq 1 50); do
    if is_running "${ids[$service]}"; then
      current="$(heartbeat "$component")" || die "SQL heartbeat query failed: $service"
      if valid_uuid "$current" && [[ "$current" != "$previous" ]]; then
        printf '%s' "$current"
        return 0
      fi
    fi
    sleep 2
  done
  return 1
}
for i in "${!services[@]}"; do
  service="${services[$i]}"
  instance="$(wait_heartbeat "$service" "${components[$i]}" '-') " ||
    die "worker never emitted a fresh SQL heartbeat: $service"
  instance="${instance//[[:space:]]/}"
  valid_uuid "$instance" || die "invalid SQL instance id: $service"
  instances["$service"]="$instance"
  pids["$service"]="$(get_pid "${ids[$service]}")"
  counts["$service"]="$(get_restart_count "${ids[$service]}")"
  [[ "${pids[$service]}" =~ ^[0-9]+$ && "${pids[$service]}" -gt 1 ]] ||
    die "invalid host PID: $service"
done
declare -A stable_ids stable_pids stable_counts
for service in sqlserver api resultado-api; do
  cid="$(compose ps -q "$service")"
  stable_ids["$service"]="$cid"
  stable_pids["$service"]="$(get_pid "$cid")"
  stable_counts["$service"]="$(get_restart_count "$cid")"
done

printf "service\tbefore_host_pid\tafter_host_pid\tbefore_restart_count\tafter_restart_count\tbefore_sql_instance\tafter_sql_instance\n" > "$OUT/restart-evidence.tsv"

for i in "${!services[@]}"; do
  target="${services[$i]}"
  target_cid="${ids[$target]}"
  before_pid="${pids[$target]}"
  before_count="${counts[$target]}"
  before_instance="${instances[$target]}"
  # A PID namespace's init (PID 1) cannot be reliably SIGKILLed by a
  # process inside that namespace. Signal the verified container init HOST PID
  # from this ephemeral GitHub runner; never use docker stop/kill, which can
  # suppress the external restart policy. Recheck identity and PID immediately.
  [[ "$(label "$target_cid")" == "$PROJECT:$target" &&
     "$(get_pid "$target_cid")" == "$before_pid" &&
     "$(docker inspect -f '{{.State.Running}}' "$target_cid")" == true &&
     "$before_pid" =~ ^[0-9]+$ && "$before_pid" -gt 1 ]] ||
    die 'worker identity/PID changed before fault injection'
  signal_exit=0
  sudo kill -KILL -- "$before_pid" \
    > "$OUT/$target-fault-signal.log" 2>&1 || signal_exit=$?
  [[ "$signal_exit" -eq 0 ]] || die "SIGKILL failed for verified sandbox worker: $target"
  printf 'target=%s;inject_exit=%s;before_pid=%s;before_restart=%s\n' \
    "$target" "$signal_exit" "$before_pid" "$before_count" \
    >> "$OUT/fault-injection-evidence.log"

  restarted=''
  for _ in $(seq 1 50); do
    if is_running "$target_cid"; then
      now_pid="$(get_pid "$target_cid")"
      now_count="$(get_restart_count "$target_cid")"
      if [[ "$now_pid" =~ ^[0-9]+$ && "$now_pid" -gt 1 &&
           "$now_pid" != "$before_pid" && "$now_count" =~ ^[0-9]+$ &&
           "$now_count" -gt "$before_count" ]]; then
        restarted=true; break
      fi
    fi
    sleep 2
  done
  if [[ "$restarted" != true ]]; then
    docker inspect -f 'status={{.State.Status}};running={{.State.Running}};exit={{.State.ExitCode}};pid={{.State.Pid}};restart={{.RestartCount}};policy={{.HostConfig.RestartPolicy.Name}}' "$target_cid" \
      >> "$OUT/fault-injection-evidence.log" 2>&1 || true
    docker logs --tail 100 "$target_cid" > "$OUT/$target-runtime-diagnostic.log" 2>&1 || true
    die "no automatic restart after SIGKILL: $target (diagnostics recorded)"
  fi
  next_instance="$(wait_heartbeat "$target" "${components[$i]}" "$before_instance")" ||
    die "new process emitted no distinct SQL heartbeat: $target"
  valid_uuid "$next_instance" || die "new SQL instance_id invalid: $target"
  pids["$target"]="$now_pid"
  counts["$target"]="$now_count"
  instances["$target"]="$next_instance"

  for other in "${services[@]}"; do
    [[ "$(label "${ids[$other]}")" == "$PROJECT:$other" ]] ||
      die "worker identity changed: $other"
    is_running "${ids[$other]}" || die "sibling worker stopped: $other"
    [[ "$(get_pid "${ids[$other]}")" == "${pids[$other]}" &&
       "$(get_restart_count "${ids[$other]}")" == "${counts[$other]}" ]] ||
      die "unrelated worker restarted: $other"
  done
  for service in sqlserver api resultado-api; do
    cid="${stable_ids[$service]}"
    [[ "$(label "$cid")" == "$PROJECT:$service" ]] ||
      die "SQL/API container identity changed: $service"
    is_running "$cid" || die "SQL/API stopped during $target fault"
    [[ "$(get_pid "$cid")" == "${stable_pids[$service]}" &&
       "$(get_restart_count "$cid")" == "${stable_counts[$service]}" ]] ||
      die "SQL/API unexpectedly restarted during $target fault"
  done
  printf "%s\t%s\t%s\t%s\t%s\t%s\t%s\n" \
    "$target" "$before_pid" "$now_pid" "$before_count" "$now_count" \
    "$before_instance" "$next_instance" >> "$OUT/restart-evidence.tsv"
  echo "C3.2f1: $target automatic restart isolated; new host PID and SQL instance_id confirmed"
done

docker exec "$api_cid" curl -fsS http://127.0.0.1:5080/health/ready \
  > "$OUT/api-ready-after.json" || die 'API SQL readiness lost'
docker exec "$resultado_cid" curl -fsS http://api:5080/health/ready \
  > "$OUT/resultado-upstream-after.json" || die 'ResultadoApi private upstream lost'
python3 - "$OUT/summary.json" <<'PY'
from pathlib import Path
import json, sys
Path(sys.argv[1]).write_text(json.dumps({
    "status": "PASS",
    "scope": "C3.2f1 restart and sibling/API isolation only",
    "workers_tested": ["processor", "operations-maintenance", "bronze-maintenance"],
    "fault": "SIGKILL of verified sandbox container host PID, never Docker stop",
    "automatic_restart_with_new_host_pid": True,
    "new_sql_heartbeat_instance_id": True,
    "sql_and_apis_and_siblings_not_restarted": True,
    "processing_recovery_verified": False
}, indent=2) + "\n", encoding="utf-8")
PY
echo 'C3.2f1: PASS three worker restart/isolation cases; processing recovery NOT tested'
