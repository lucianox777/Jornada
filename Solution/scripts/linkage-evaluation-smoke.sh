#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${JORNADA_EVALUATION_SMOKE_OUT:-$ROOT/.local/linkage-evaluation-smoke}"
DB="${JORNADA_EVALUATION_DATABASE:-JornadaHarness}"
SQL_PASSWORD="${JORNADA_EVALUATION_SQL_PASSWORD:-}"
SCALE_PEOPLE="${JORNADA_EVALUATION_SCALE_PEOPLE:-5000}"
SCALE_SEED="${JORNADA_EVALUATION_SCALE_SEED:-355}"
LABEL_COUNT="${JORNADA_EVALUATION_LABEL_COUNT:-100}"

need(){ command -v "$1" >/dev/null 2>&1 || { echo "ERRO: comando '$1' não encontrado." >&2; exit 2; }; }
for x in docker dotnet python3 sha256sum; do need "$x"; done
: "${ConnectionStrings__Jornada:?ConnectionStrings__Jornada não definido}"
: "${SQL_PASSWORD:?JORNADA_EVALUATION_SQL_PASSWORD não definido}"
[[ "$DB" =~ ^[A-Za-z0-9_]+$ ]] || { echo "ERRO: nome de banco inválido." >&2; exit 2; }
[[ "$SCALE_PEOPLE" =~ ^[1-9][0-9]*$ ]] || { echo "ERRO: JORNADA_EVALUATION_SCALE_PEOPLE inválido." >&2; exit 2; }
[[ "$SCALE_SEED" =~ ^[0-9]+$ ]] || { echo "ERRO: JORNADA_EVALUATION_SCALE_SEED inválido." >&2; exit 2; }
[[ "$LABEL_COUNT" =~ ^[1-9][0-9]*$ ]] || { echo "ERRO: JORNADA_EVALUATION_LABEL_COUNT inválido." >&2; exit 2; }

CID="${JORNADA_EVALUATION_SQL_CONTAINER_ID:-}"
if [[ -z "$CID" ]]; then
  CID="$(docker ps -q --filter publish=1433 | head -1)"
fi
[[ -n "$CID" ]] || { echo "ERRO: container SQL Server não encontrado." >&2; exit 3; }

mkdir -p "$OUT"
rm -f "$OUT/labels.csv" "$OUT/report.json" "$OUT/before.txt" "$OUT/after.txt"

sqlcmd(){
  docker exec -i -e "SQLCMDPASSWORD=$SQL_PASSWORD" "$CID" \
    /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d "$DB" -W -h -1 "$@"
}
scalar(){ sqlcmd -Q "SET NOCOUNT ON; $1" | tr -d '\r' | sed '/^[[:space:]]*$/d' | tail -1 | xargs; }

snapshot(){
  local dest="$1"
  {
    printf 'identidade.linkage_run='
    scalar "SELECT CONCAT(COUNT_BIG(*),'|',COALESCE(CONVERT(varchar(30),CHECKSUM_AGG(BINARY_CHECKSUM(*))),'NULL')) FROM identidade.linkage_run;"
    printf 'identidade.vinculo_fonte='
    scalar "SELECT CONCAT(COUNT_BIG(*),'|',COALESCE(CONVERT(varchar(30),CHECKSUM_AGG(BINARY_CHECKSUM(*))),'NULL')) FROM identidade.vinculo_fonte;"
    printf 'gold.pessoa='
    scalar "SELECT CONCAT(COUNT_BIG(*),'|',COALESCE(CONVERT(varchar(30),CHECKSUM_AGG(BINARY_CHECKSUM(*))),'NULL')) FROM gold.pessoa;"
  } > "$dest"
}

printf 'pessoa_observacao_id,pessoa_uuid_verdade\n' > "$OUT/labels.csv"
sqlcmd -Q "
SET NOCOUNT ON;
WITH pend AS (
    SELECT TOP ($LABEL_COUNT)
           po.pessoa_observacao_id,
           TRY_CONVERT(bigint,RIGHT(po.codigo_pessoa_origem,10)) AS n
    FROM silver.pessoa_observacao po
    WHERE po.cpf IS NULL
      AND po.codigo_pessoa_origem LIKE 'SCALE-PEND-%'
    ORDER BY po.pessoa_observacao_id
)
SELECT CONCAT(
    pessoa_observacao_id,',',
    CONVERT(varchar(36),CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('JORNADA-V355-',$SCALE_SEED,'-P-',((n-1)%$SCALE_PEOPLE)+1))))
)
FROM pend
WHERE n IS NOT NULL
ORDER BY pessoa_observacao_id;" | tr -d '\r' | sed '/^[[:space:]]*$/d' >> "$OUT/labels.csv"

actual_labels="$(( $(wc -l < "$OUT/labels.csv") - 1 ))"
[[ "$actual_labels" -eq "$LABEL_COUNT" ]] || {
  echo "ERRO: esperados $LABEL_COUNT rótulos sintéticos, obtidos $actual_labels." >&2
  exit 4
}

snapshot "$OUT/before.txt"

(
  cd "$ROOT"
  dotnet run --project src/Jornada.Linkage.Evaluation --configuration Release --no-build -- \
    --labels "$OUT/labels.csv" \
    --output "$OUT/report.json" \
    --birth-window-days 7 \
    --max-cpf-anchored-pairs 500 \
    --sample-pool-size 5000 \
    --smoothing-alpha 0.5 \
    --command-timeout-seconds 300
)

snapshot "$OUT/after.txt"
cmp -s "$OUT/before.txt" "$OUT/after.txt" || {
  echo "ERRO: Evaluation alterou fingerprint de tabelas operacionais monitoradas." >&2
  diff -u "$OUT/before.txt" "$OUT/after.txt" >&2 || true
  exit 5
}

python3 - "$OUT/report.json" "$LABEL_COUNT" <<'PY'
import json,sys
path=sys.argv[1]
expected=int(sys.argv[2])
with open(path,encoding='utf-8') as f:
    r=json.load(f)
if r.get('purpose')!='DEV_HML_ONLY_NO_PUBLICATION':
    raise SystemExit('purpose inválido')
required={
    'read-only against Jornada operational tables',
    'does not create linkage_run',
    'does not write IDENTITY_MAP/vinculo_fonte',
    'does not update Gold',
    'V2 is experimental evidence only',
}
if not required.issubset(set(r.get('safeguards',[]))):
    raise SystemExit('salvaguardas incompletas')
inp=r.get('input',{})
if inp.get('labeledNoCpfPairs')!=expected:
    raise SystemExit(f"labeledNoCpfPairs inesperado: {inp.get('labeledNoCpfPairs')}")
if int(inp.get('cpfAnchoredIndependentPairs',0)) < 100:
    raise SystemExit('amostra CPF-ancorada insuficiente para smoke')
blocking=r.get('blocking',{})
for key in ('v1','v2Candidate','deltaRecall'):
    if key not in blocking:
        raise SystemExit(f'blocking sem {key}')
for key in ('v1','v2Candidate'):
    b=blocking[key]
    for metric in ('sampleSize','trueUuidInsideBlock','recall','meanCandidates','medianCandidates','p95Candidates','maxCandidates'):
        if metric not in b:
            raise SystemExit(f'{key} sem {metric}')
transport=r.get('mTransportability',{})
for key in ('cpfAnchored','noCpfLabeled','distance'):
    if key not in transport:
        raise SystemExit(f'mTransportability sem {key}')
for metric in ('nomeTotalVariation','nomeMaeTotalVariation','dataNascimentoExactAbsoluteDelta'):
    if metric not in transport['distance']:
        raise SystemExit(f'distance sem {metric}')
print('Relatório Evaluation: contrato e métricas OK')
PY
python3 "$ROOT/scripts/linkage-evaluation-evidence-gate.py" "$OUT/report.json" \
  --policy "$ROOT/config/hml/linkage-evaluation-policy.json" \
  --summary "$OUT/evidence-gate-summary.json"
python3 "$ROOT/scripts/linkage-statistical-readiness-gate.py" \
  --root "$ROOT" --self-test

{
  echo "status=OK"
  echo "labels=$actual_labels"
  echo "report_sha256=$(sha256sum "$OUT/report.json" | awk '{print $1}')"
  echo "operational_fingerprint_unchanged=true"
} > "$OUT/result.txt"
cat "$OUT/result.txt"
echo "LINKAGE EVALUATION SMOKE: OK"
