#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$ROOT/.env"
EXAMPLE="$ROOT/.env.example"
API_URL="${JORNADA_E2E_API_URL:-http://127.0.0.1:5088}"
OUT="$ROOT/.local/e2e"

need(){ command -v "$1" >/dev/null 2>&1 || { echo "ERRO: comando '$1' não encontrado." >&2; exit 2; }; }
for x in docker dotnet curl python3; do need "$x"; done
[[ -f "$ENV_FILE" ]] || cp "$EXAMPLE" "$ENV_FILE"
# shellcheck disable=SC1090
set -a; source "$ENV_FILE"; set +a
: "${JORNADA_SQL_SA_PASSWORD:?JORNADA_SQL_SA_PASSWORD não definido}"
PORT="${JORNADA_SQL_PORT:-14333}"; DB="${JORNADA_SQL_DATABASE:-JornadaLocal}"
mkdir -p "$OUT"; rm -rf "$OUT/bronze" "$OUT/staging" "$OUT/packages"; mkdir -p "$OUT/bronze" "$OUT/staging" "$OUT/packages"

"$ROOT/scripts/local-db.sh" reset >/dev/null
CONN="Server=localhost,$PORT;Database=$DB;User Id=sa;Password=$JORNADA_SQL_SA_PASSWORD;TrustServerCertificate=true;Encrypt=false"
export ConnectionStrings__Jornada="$CONN"
export ASPNETCORE_ENVIRONMENT=Development
export DOTNET_ENVIRONMENT=Development
export ASPNETCORE_URLS="$API_URL"
export BronzeStorage__Provider=FileSystem
export BronzeStorage__RootPath="$OUT/bronze"
export IngestionStaging__RootPath="$OUT/staging"
export Processor__PollingMilliseconds=100

api_pid=''; worker_pid=''
cleanup(){
  [[ -z "$worker_pid" ]] || kill "$worker_pid" 2>/dev/null || true
  [[ -z "$api_pid" ]] || kill "$api_pid" 2>/dev/null || true
  wait "$worker_pid" 2>/dev/null || true; wait "$api_pid" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

# Compila uma vez para que API e Worker não disputem restore/build em paralelo.
# Em CI, os packages.lock.json vêm do job dependency-lock e o restore deve ser estritamente bloqueado.
if [[ "${JORNADA_E2E_LOCKED_RESTORE:-false}" == "true" ]]; then
  (cd "$ROOT" && dotnet restore Jornada.sln --locked-mode && dotnet build Jornada.sln --configuration Release --no-restore -warnaserror)
else
  (cd "$ROOT" && dotnet restore Jornada.sln --use-lock-file && python3 scripts/nuget-lock-gate.py --root . --summary .local/e2e/nuget-lock-summary.json && dotnet restore Jornada.sln --locked-mode && dotnet build Jornada.sln --configuration Release --no-restore -warnaserror)
fi
(cd "$ROOT" && dotnet run --no-build --configuration Release --no-launch-profile --project src/Jornada.Api >"$OUT/api.log" 2>&1) & api_pid=$!
(cd "$ROOT" && dotnet run --no-build --configuration Release --project src/Jornada.Processor.Worker >"$OUT/processor.log" 2>&1) & worker_pid=$!

for _ in $(seq 1 120); do
  code="$(curl -sS -o "$OUT/ready.json" -w '%{http_code}' "$API_URL/health/ready" || true)"
  [[ "$code" == 200 ]] && break
  sleep 1
done
[[ "${code:-}" == 200 ]] || { echo 'ERRO: API não ficou ready.' >&2; tail -100 "$OUT/api.log" >&2 || true; exit 3; }

package="$(python3 "$ROOT/scripts/build-ingestion-fixture.py" --fixture "$ROOT/tests/fixtures/ingestao/AA01_v2" --gestor SEHAB --output-dir "$OUT/packages")"
filename="$(basename "$package")"
access_key='KcUBZuLvRCu0lKN6xmXdjGKhPTgluG1Wu0sFB36lvTY'

post_delivery(){
  local idem="$1" body="$2" code_file="$3"
  curl -sS -o "$body" -w '%{http_code}' -X POST "$API_URL/api/v1/ingestao/entregas" \
    -H "X-Jornada-Gestor: SEHAB" -H "X-Jornada-Access-Key: $access_key" \
    -H "Idempotency-Key: $idem" -H 'Content-Type: application/zip' \
    -H "Content-Disposition: attachment; filename=$filename" --data-binary "@$package" > "$code_file"
}
json_get(){ python3 - "$1" "$2" <<'PY'
import json,sys
obj=json.load(open(sys.argv[1],encoding='utf-8'))
want=sys.argv[2].lower()
for k,v in obj.items():
    if k.lower()==want:
        print(v); break
else: raise SystemExit(f'campo {sys.argv[2]} ausente em {sys.argv[1]}')
PY
}
wait_processed(){
  local id="$1" out="$2" status=''
  for _ in $(seq 1 120); do
    curl -sS -o "$out" "$API_URL/api/v1/ingestao/entregas/$id" -H 'X-Jornada-Gestor: SEHAB' -H "X-Jornada-Access-Key: $access_key"
    status="$(json_get "$out" status)"
    [[ "$status" == PROCESSADA ]] && return 0
    [[ "$status" == REJEITADA || "$status" == QUARENTENA ]] && { echo "ERRO: Entrega $id terminou $status" >&2; return 1; }
    sleep 1
  done
  echo "ERRO: timeout aguardando Entrega $id; último status=$status" >&2; return 1
}
compose_sql(){
  (cd "$ROOT" && docker compose --env-file "$ENV_FILE" exec -T -e "SQLCMDPASSWORD=$JORNADA_SQL_SA_PASSWORD" sqlserver \
    /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d "$DB" -W -h -1 -Q "$1") | tr -d '\r' | sed '/^[[:space:]]*$/d'
}
scalar(){ compose_sql "SET NOCOUNT ON; $1" | tail -1 | tr -d '[:space:]'; }
actual_aa01_hash="$(sha256sum "$ROOT/config/contracts/registros/AA01/v1/registro.schema.json" | awk '{print $1}')"
expected_aa01_hash="$(scalar "SELECT LOWER(CONVERT(varchar(64),trv.schema_registro_sha256,2)) FROM ref.tipo_registro tr JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_id=tr.tipo_registro_id WHERE tr.codigo='AA01' AND trv.status='ATIVA';")"
echo "E2E CONTRACT DIGEST: AA01 source=$actual_aa01_hash catalog=$expected_aa01_hash"
[[ -n "$expected_aa01_hash" && "$actual_aa01_hash" == "$expected_aa01_hash" ]] || { echo 'ERRO: digest AA01 diverge entre arquivo e catálogo antes do Processor.' >&2; exit 10; }

post_delivery 'local-e2e-001' "$OUT/post1.json" "$OUT/post1.code"
[[ "$(cat "$OUT/post1.code")" == 202 ]] || { echo "ERRO: POST inicial não retornou 202" >&2; cat "$OUT/post1.json" >&2; exit 4; }
id1="$(json_get "$OUT/post1.json" entregaId)"
wait_processed "$id1" "$OUT/status1.json"

# Mesma Idempotency-Key + mesmos bytes deve devolver a mesma Entrega e não duplicar a borda.
post_delivery 'local-e2e-001' "$OUT/post-idempotent.json" "$OUT/post-idempotent.code"
[[ "$(cat "$OUT/post-idempotent.code")" == 202 ]] || exit 5
id_same="$(json_get "$OUT/post-idempotent.json" entregaId)"
[[ "$id_same" == "$id1" ]] || { echo 'ERRO: mesma Idempotency-Key criou outra Entrega.' >&2; exit 5; }
[[ "$(scalar "SELECT COUNT(*) FROM ingestao.entrega WHERE idempotency_key='local-e2e-001';")" == 1 ]] || { echo 'ERRO: borda idempotente duplicou Entrega.' >&2; exit 5; }

# Evidência das camadas após o caminho HTTP -> Bronze -> Processor -> Silver -> Gold -> Serving.
[[ "$(scalar "SELECT COUNT(*) FROM bronze.entrega_arquivo WHERE entrega_id='$id1';")" == 1 ]] || { echo 'ERRO: Bronze não materializada.' >&2; exit 6; }
[[ "$(scalar "SELECT COUNT(*) FROM silver.pessoa_observacao po JOIN ingestao.lote l ON l.lote_id=po.lote_id WHERE l.entrega_id='$id1';")" == 1 ]] || { echo 'ERRO: Silver não materializada.' >&2; exit 6; }
[[ "$(scalar "SELECT COUNT(*) FROM gold.pessoa WHERE cpf='11144477735';")" == 1 ]] || { echo 'ERRO: Gold Pessoa ausente.' >&2; exit 6; }
[[ "$(scalar "SELECT COUNT(*) FROM serving.v_beneficios_concedidos_pessoa WHERE codigo_registro_origem='AA-2026-004711';")" == 1 ]] || { echo 'ERRO: Serving factual ausente.' >&2; exit 6; }

curl -sS -o "$OUT/resolve.json" -w '%{http_code}' -X POST "$API_URL/api/v1/identidade/resolver" \
  -H 'Content-Type: application/json' -H 'X-Jornada-Gestor: SEHAB' -H "X-Jornada-Access-Key: $access_key" \
  --data '{"cpf":"11144477735"}' > "$OUT/resolve.code"
[[ "$(cat "$OUT/resolve.code")" == 200 ]] || { echo 'ERRO: resolver API falhou.' >&2; exit 7; }
pessoa_uuid="$(json_get "$OUT/resolve.json" pessoaUuid)"
[[ -n "$pessoa_uuid" && "$pessoa_uuid" != None ]] || { echo 'ERRO: resolver não retornou UUID.' >&2; exit 7; }
curl -sS -o "$OUT/person.json" -w '%{http_code}' "$API_URL/api/v1/pessoas/$pessoa_uuid" -H 'X-Jornada-Gestor: SEHAB' -H "X-Jornada-Access-Key: $access_key" > "$OUT/person.code"
[[ "$(cat "$OUT/person.code")" == 200 ]] || { echo 'ERRO: retorno da Pessoa pela API falhou.' >&2; exit 7; }
curl -sS -o "$OUT/records.json" -w '%{http_code}' "$API_URL/api/v1/pessoas/$pessoa_uuid/registros" -H 'X-Jornada-Gestor: SEHAB' -H "X-Jornada-Access-Key: $access_key" > "$OUT/records.code"
[[ "$(cat "$OUT/records.code")" == 200 ]] || { echo 'ERRO: retorno de Registros pela API falhou.' >&2; exit 7; }
grep -q 'AA-2026-004711' "$OUT/records.json" || { echo 'ERRO: registro esperado não voltou pela API.' >&2; exit 7; }

# Nova Entrega lógica com os mesmos bytes prova retransmissão de itens sem nova versão Gold.
post_delivery 'local-e2e-002' "$OUT/post2.json" "$OUT/post2.code"
[[ "$(cat "$OUT/post2.code")" == 202 ]] || exit 8
id2="$(json_get "$OUT/post2.json" entregaId)"; [[ "$id2" != "$id1" ]] || exit 8
wait_processed "$id2" "$OUT/status2.json"
retrans="$(scalar "SELECT COUNT(*) FROM ingestao.item_processado ip JOIN ingestao.lote l ON l.lote_id=ip.lote_id WHERE l.entrega_id='$id2' AND ip.resultado='RETRANSMITIDO';")"
[[ "$retrans" -ge 2 ]] || { echo "ERRO: retransmissão não foi reconhecida; itens=$retrans" >&2; exit 8; }
[[ "$(scalar "SELECT COUNT(*) FROM gold.beneficio_concedido WHERE codigo_registro_origem='AA-2026-004711' AND status_analitico='VIGENTE';")" == 1 ]] || { echo 'ERRO: retransmissão duplicou a versão Gold vigente.' >&2; exit 8; }

python3 - "$OUT/evidence.json" "$id1" "$id2" "$pessoa_uuid" "$retrans" <<'PY'
import json,sys,datetime
json.dump({
 'status':'OK','generatedAtUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
 'firstEntregaId':sys.argv[2],'retransmissionEntregaId':sys.argv[3],'pessoaUuid':sys.argv[4],
 'idempotencyKeyReplaySameEntrega':True,'retransmittedItems':int(sys.argv[5]),
 'layers':['HTTP','Bronze','Processor','Silver','Gold','Serving','HTTP-return']
},open(sys.argv[1],'w',encoding='utf-8'),ensure_ascii=False,indent=2)
PY
cat "$OUT/evidence.json"
echo 'LOCAL E2E: OK'
