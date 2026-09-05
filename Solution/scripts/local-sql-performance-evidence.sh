#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${JORNADA_SQL_PERFORMANCE_OUT:-$ROOT/.local/sql-performance}"
WINDOW="${JORNADA_SQL_PERFORMANCE_WINDOW_SECONDS:-60}"
mkdir -p "$OUT"
command -v sqlcmd >/dev/null 2>&1 || { echo "ERRO: sqlcmd não encontrado." >&2; exit 2; }
: "${SQLCMD_SERVER:?defina SQLCMD_SERVER}"
: "${SQLCMD_USER:?defina SQLCMD_USER}"
: "${SQLCMDPASSWORD:?defina SQLCMDPASSWORD via secret store/ambiente}"
DB="${SQLCMD_DATABASE:-JornadaHml}"
collect() {
  sqlcmd -S "$SQLCMD_SERVER" -U "$SQLCMD_USER" -C -d "$DB" -h -1 -W \
    -i "$ROOT/database/Jornada_HML_SQL_Performance_Evidence.sql" | sed '/^[[:space:]]*$/d'
}
collect > "$OUT/sample-start.json"
sleep "$WINDOW"
collect > "$OUT/sample-end.json"
python3 - "$OUT/sample-start.json" "$OUT/sample-end.json" "$OUT/report.json" "$WINDOW" <<'PY2'
import json,sys
from pathlib import Path
a=json.loads(Path(sys.argv[1]).read_text()); b=json.loads(Path(sys.argv[2]).read_text()); w=int(sys.argv[4])
for k in ('cumulativeLockWaitTasks','cumulativeLockWaitMilliseconds','cumulativeDeadlocksRaw'):
    if b[k] < a[k]: raise SystemExit(f'contador reiniciou durante janela: {k}')
b['windowSeconds']=w
b['deltaLockWaitTasks']=b['cumulativeLockWaitTasks']-a['cumulativeLockWaitTasks']
b['deltaLockWaitMilliseconds']=b['cumulativeLockWaitMilliseconds']-a['cumulativeLockWaitMilliseconds']
b['deltaDeadlocks']=b['cumulativeDeadlocksRaw']-a['cumulativeDeadlocksRaw']
Path(sys.argv[3]).write_text(json.dumps(b,indent=2,ensure_ascii=False)+'\n')
PY2
python3 "$ROOT/scripts/sql-performance-evidence-gate.py" "$OUT/report.json" \
  --policy "$ROOT/config/hml/sql-performance-policy.json" --summary "$OUT/summary.json"
echo "SQL performance evidence: $OUT/report.json"
