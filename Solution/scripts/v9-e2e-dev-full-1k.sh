#!/usr/bin/env bash
# Only source after the real HTTP/Processor/SQL delivery of IBGE 1k.
# No 10k/100k, TRAIN, scientific TEST, HML/PROD or activation.
set -euo pipefail
[[ "${JORNADA_E2E_V9_DEV_FULL:-false}" == true
   && "${JORNADA_E2E_HTTP_VOLUME_COUNT:-}" == 1000
   && "${JORNADA_E2E_IBGE_1K:-}" == true
   && "${JORNADA_RUNTIME_MODE:-}" == DEV
   && "$DB" == "JornadaV9Dev_${JORNADA_V9_DEV_ATTEMPT//-/}"
   && "$DB" =~ ^JornadaV9Dev_[0-9a-f]{32}$ ]] || {
    echo "RUNNER_V9_DEV_E2E_1K_ISOLATION_REQUIRED" >&2; exit 61;
  }
preflight="$OUT/ibge-1k-preflight.json"
private_truth="$OUT/ibge-1k/ingestion/bridge-truth.jsonl"
[[ -f "$preflight" && -f "$private_truth"
   && ! -L "$preflight" && ! -L "$private_truth" ]] || {
    echo "RUNNER_V9_DEV_E2E_TRUTH_ISOLATION_INVALID" >&2; exit 62;
  }
python3 - "$preflight" "$private_truth" <<'PY'
import hashlib, json, pathlib, sys
receipt=json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
data=pathlib.Path(sys.argv[2]).read_bytes()
digest=hashlib.sha256(data).hexdigest().upper()
if (receipt.get("schema") != "V9_E2E_IBGE_BRIDGE_PREFLIGHT_V1"
    or receipt.get("status") != "PASS"
    or receipt.get("baseIdentities") != 1000
    or receipt.get("seed") != 355
    or receipt.get("sourceObservations",0) < 1000
    or receipt.get("truthSha256") != digest):
    raise SystemExit("RUNNER_V9_DEV_E2E_SOURCE_SHA_NOT_AUTHENTICATED")
print("V9_DEV_E2E_PRIVATE_TRUTH_REAL_SHA: VERIFIED")
PY
export JORNADA_V9_DEV_1K_PREFLIGHT="$preflight"
export Linkage__SelectedModelVersion=9
runner_log="$OUT/ibge-1k/v9-dev-runner-private.log"
(cd "$ROOT" && dotnet run --no-build --configuration Release \
   --project src/Jornada.Linkage.Runner -- --v9-dev-full-1k \
   > "$runner_log" 2>&1) || {
  echo "RUNNER_V9_DEV_E2E_FULL_SQL_FAILED" >&2; exit 63;
}
run_id="$(python3 - "$runner_log" <<'PY'
from pathlib import Path
import re,sys
lines=Path(sys.argv[1]).read_text(encoding="utf-8").splitlines()
matches=[re.fullmatch(r"V9_DEV_E2E_FULL_1K_PASS run_id=([0-9a-f-]{36}) evaluated=(\d+) winner_sha256=([0-9a-f]{64})",x)
         for x in lines]
matches=[x for x in matches if x]
if len(matches)!=1 or int(matches[0].group(2))<=0:
    raise SystemExit("RUNNER_V9_DEV_E2E_FULL_RECEIPT_NOT_UNIQUE")
print(matches[0].group(1))
PY
)"
[[ "$run_id" =~ ^[0-9a-f-]{36}$ ]] || exit 64
observed="$OUT/ibge-1k/v9-dev-observed-private.txt"
compose_sql "SET NOCOUNT ON;
SELECT CONCAT(po.codigo_pessoa_origem,N'|',
              COALESCE(CONVERT(varchar(36),vc.pessoa_uuid),N'NULL'))
  FROM silver.pessoa_observacao po
  LEFT JOIN identidade.v_vinculo_corrente vc
    ON vc.pessoa_observacao_id=po.pessoa_observacao_id
   AND vc.status=N'RESOLVIDO'
 WHERE po.codigo_pessoa_origem LIKE N'SYNTH-%'
 ORDER BY po.codigo_pessoa_origem,po.pessoa_observacao_id;" > "$observed"
python3 "$ROOT/scripts/v9-e2e-dev-r1-independent-oracle.py" \
  --truth "$private_truth" --preflight "$preflight" \
  --observed "$observed" --run-id "$run_id" \
  --receipt "$OUT/v9-dev-full-1k-summary.json"
echo "V9_DEV_E2E_FULL_1K_AFTER_PROCESSOR: PASS (aggregate receipt only)"
