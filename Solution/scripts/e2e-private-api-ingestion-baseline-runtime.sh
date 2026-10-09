#!/usr/bin/env bash
# C3.2f2b0: real PRIVATE API -> Processor -> SQL baseline and idempotency.
# Does not inject a fault or claim recovery; C3.2f2b1 will cover that.
set -euo pipefail
die() { printf 'C3.2f2b baseline: %s\n' "$*" >&2; exit 2; }
[[ $# == 0 && "${GITHUB_ACTIONS:-}" == true && "${CI:-}" == true &&
   "${GITHUB_REPOSITORY:-}" == 'lucianox777/Jornada' &&
   "${JORNADA_WORKERS_E2E_RUNTIME_TEST:-}" == true ]] ||
  die 'explicit disposable CI only'
[[ "${GITHUB_RUN_ID:-}" =~ ^[0-9]{6,16}$ &&
   "${GITHUB_RUN_ATTEMPT:-}" =~ ^[0-9]{1,3}$ ]] ||
  die 'unique GitHub run identity required'
ID="ci${GITHUB_RUN_ID}${GITHUB_RUN_ATTEMPT}"
[[ "${JORNADA_WORKERS_E2E_ID:-}" == "$ID" &&
   -n "${JORNADA_WORKERS_E2E_SQL_PASSWORD:-}" &&
   "${JORNADA_WORKERS_E2E_IMAGE_TAG:-test}" == test ]] ||
  die 'isolated Compose credentials or project mismatch'
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
OUT="$ROOT/.local/e2e/c3-2f2b-api-sql-baseline"
mkdir -p "$OUT/packages"
PROJECT="jornada-workers-e2e-$ID"
COMPOSE="$ROOT/install/console-dev-e2e/docker-compose.workers.yml"
compose() { docker compose --env-file /dev/null --profile continuous -p "$PROJECT" -f "$COMPOSE" "$@"; }
label() {
  docker inspect -f '{{ index .Config.Labels "com.docker.compose.project" }}:{{ index .Config.Labels "com.docker.compose.service" }}' "$1"
}
for service in sqlserver api processor operations-maintenance bronze-maintenance resultado-api; do
  cid="$(compose ps -q "$service")"
  [[ -n "$cid" && "$(label "$cid")" == "$PROJECT:$service" &&
     "$(docker inspect -f '{{.State.Running}}' "$cid")" == true ]] ||
    die "required sandbox service is not independently running: $service"
done
api_cid="$(compose ps -q api)"
processor_cid="$(compose ps -q processor)"
sql_cid="$(compose ps -q sqlserver)"
export SQLCMDPASSWORD="$JORNADA_WORKERS_E2E_SQL_PASSWORD"
sql() {
  docker exec -e SQLCMDPASSWORD "$sql_cid" /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -C -b -I -l 10 -d JornadaE2E -W -h -1 "$@"
}
scalar() {
  local out
  out="$(sql -Q "SET NOCOUNT ON; $1" | tr -d '[:space:]\r')"
  [[ "$out" =~ ^[0-9]+$ ]] || die 'non-numeric SQL count'
  printf '%s' "$out"
}
docker exec "$api_cid" curl -fsS http://127.0.0.1:5080/health/ready \
  > "$OUT/api-readiness-before.json" || die 'API is not SQL/schema-ready'
worker_before="$(sql -Q "SET NOCOUNT ON; SELECT COUNT_BIG(*) FROM controle.runtime_componente
 WHERE node_id=N'NODE2' AND componente=N'Processor' AND status=N'RUNNING'
 AND heartbeat_em >= DATEADD(SECOND,-45,SYSUTCDATETIME());" | tr -d '[:space:]\r')"
[[ "$worker_before" == 1 ]] || die 'Processor heartbeat not fresh'

# Use the SAME synthetic fixture as local E2E, but ONLY private Compose API/SQL.
package="$(python3 scripts/build-ingestion-fixture.py \
  --fixture tests/fixtures/ingestao/AA01_v2 \
  --gestor SEHAB --output-dir "$OUT/packages")"
[[ -f "$package" && "$package" == "$OUT/packages/"* ]] ||
  die 'fixture path escaped disposable evidence directory'
filename="$(basename "$package")"
[[ "$filename" =~ ^ENTREGA_SEHAB_SEHAB_v2_[a-f0-9]{64}\.zip$ ]] ||
  die 'unexpected ZIP name'
sha="$(sha256sum "$package" | cut -d' ' -f1)"
[[ "$filename" == *"_$sha.zip" ]] || die 'ZIP digest mismatch'
key="ci-e2e-$GITHUB_RUN_ID-$GITHUB_RUN_ATTEMPT"
access_key="$(python3 - "$ROOT/config/security/test-access-keys.json" <<'PY'
import json,sys
creds=json.load(open(sys.argv[1],encoding='utf-8'))['credentials']
matches=[c['accessKey'] for c in creds if c.get('type')=='GESTOR'
 and c.get('gestorCodigo')=='SEHAB' and 'jornada.ingestao.write' in c.get('scopes',[])]
assert len(matches)==1
print(matches[0])
PY
)"
[[ -n "$access_key" ]] || die 'missing synthetic SEHAB test key'
post_zip() {
  local output="$1" code
  code="$(docker exec -i "$api_cid" curl --max-time 40 --fail-with-body -sS \
    -o /tmp/c3-2f2b-ingestion-receipt.json -w '%{http_code}' \
    -X POST 'http://127.0.0.1:5080/api/v1/ingestao/entregas' \
    -H 'X-Jornada-Gestor: SEHAB' -H "X-Jornada-Access-Key: $access_key" \
    -H "Idempotency-Key: $key" -H 'Content-Type: application/zip' \
    -H "Content-Disposition: attachment; filename=$filename" \
    --data-binary @- < "$package")" ||
    die 'synthetic ZIP was rejected by private API'
  [[ "$code" == 202 ]] || die "private API returned HTTP $code"
  docker exec "$api_cid" cat /tmp/c3-2f2b-ingestion-receipt.json > "$output" ||
    die 'private API receipt was not readable'
}
post_zip "$OUT/first-receipt.json"
entrega="$(python3 - "$OUT/first-receipt.json" <<'PY'
import json,sys,uuid
obj=json.load(open(sys.argv[1],encoding='utf-8'))
print(uuid.UUID(obj['entregaId']))
PY
)"
[[ "$entrega" =~ ^[0-9a-f-]{36}$ ]] || die 'invalid delivery UUID'

# Poll SQL on PRIVATE sqlserver only; never allow host SQL connections.
state=''
for _ in $(seq 1 90); do
  state="$(sql -Q "SET NOCOUNT ON;
    SELECT TOP(1) e.status FROM ingestao.entrega e
    WHERE e.entrega_id='$entrega' AND e.idempotency_key='$key';" |
    tr -d '[:space:]\r')"
  case "$state" in
    PROCESSADA) break ;;
    REJEITADA|QUARENTENA) die "synthetic ingestion ended in $state" ;;
    *) sleep 2 ;;
  esac
done
[[ "$state" == PROCESSADA ]] || die "synthetic delivery never reached PROCESSADA ($state)"
lote="$(sql -Q "SET NOCOUNT ON; SELECT CONVERT(VARCHAR(36),l.lote_id)
 FROM ingestao.lote l WHERE l.entrega_id='$entrega';" | tr -d '[:space:]\r')"
[[ "$lote" =~ ^[0-9a-fA-F-]{36}$ ]] || die 'one synthetic batch not found'

# Every count is tied to THIS delivery/lot; no global counts or false positives.
counts_sql() {
  sql -Q "SET NOCOUNT ON; SELECT CONCAT(
    (SELECT COUNT_BIG(*) FROM ingestao.entrega WHERE gestor_id=
      (SELECT gestor_id FROM ref.gestor WHERE codigo=N'SEHAB') AND idempotency_key='$key'), '|',
    (SELECT COUNT_BIG(*) FROM ingestao.lote WHERE entrega_id='$entrega'), '|',
    (SELECT COUNT_BIG(*) FROM silver.pessoa_observacao WHERE lote_id='$lote'), '|',
    (SELECT COUNT_BIG(*) FROM silver.registro_observacao WHERE lote_id='$lote'), '|',
    (SELECT COUNT_BIG(*) FROM ingestao.item_processado WHERE lote_id='$lote'), '|',
    (SELECT COUNT_BIG(*) FROM serving.registro_integrado WHERE entrega_id='$entrega'));"
}
counts_before="$(counts_sql | tr -d '[:space:]\r')"
IFS='|' read -r deliveries lots persons records items serving <<< "$counts_before"
[[ "$deliveries" == 1 && "$lots" == 1 && "$persons" == 1 &&
   "$records" == 1 && "$items" == 2 && "$serving" =~ ^[0-9]+$ &&
   "$serving" -ge 1 ]] ||
  die "private SQL cardinality invalid: $counts_before"

post_zip "$OUT/replay-receipt.json"
replay="$(python3 - "$OUT/replay-receipt.json" <<'PY'
import json,sys,uuid
print(uuid.UUID(json.load(open(sys.argv[1],encoding='utf-8'))['entregaId']))
PY
)"
[[ "$replay" == "$entrega" ]] || die 'replay created a second delivery'
counts_after="$(counts_sql | tr -d '[:space:]\r')"
[[ "$counts_before" == "$counts_after" ]] ||
  die "replay modified Silver/processed items/serving: $counts_before -> $counts_after"
docker exec "$api_cid" curl -fsS http://127.0.0.1:5080/health/ready \
  > "$OUT/api-readiness-after.json" || die 'API lost SQL/schema readiness'
[[ "$(docker inspect -f '{{.State.Running}}' "$processor_cid")" == true ]] ||
  die 'Processor stopped after private ZIP ingestion'
python3 - "$OUT/summary.json" "$PROJECT" "$entrega" "$lote" "$sha" "$counts_before" <<'PY'
import json,sys,pathlib
output,project,entrega,lote,sha,counts=sys.argv[1:]
keys=['entregas','lotes','silver_pessoas','silver_registros','itens_processados','serving_registros']
record={
 'status':'PASS','scenario':'C3.2f2b private API/Processor/SQL ingestion baseline',
 'database':'JornadaE2E','compose_project':project,
 'entrega_id':entrega,'lote_id':lote,'zip_sha256':sha,
 'counts_by_delivery':dict(zip(keys,map(int,counts.split('|')))),
 'same_delivery_on_http_idempotency_replay':True,
 'identical_counts_after_http_replay':True,
 'fault_injected':False,'transaction_rollback_observed':False,
 'processing_recovery_verified':False}
pathlib.Path(output).write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8')
PY
echo 'C3.2f2b: PASS real isolated API/Processor/SQL ZIP, idempotency; NO recovery claim'
