#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
: "${JORNADA_HML_PERFORMANCE_REPORT:?defina JORNADA_HML_PERFORMANCE_REPORT com evidência real do harness de escala em HML}"
: "${JORNADA_HML_LINKAGE_REPORT:?defina JORNADA_HML_LINKAGE_REPORT com evidência real do Jornada.Linkage.Evaluation}"
: "${JORNADA_HML_SQL_PERFORMANCE_REPORT:?defina JORNADA_HML_SQL_PERFORMANCE_REPORT com evidência de Query Store/waits/deadlocks}"
: "${JORNADA_HML_API_PROJECTION_REPORT:?defina JORNADA_HML_API_PROJECTION_REPORT com evidência dos lotes 1/10/100/1000}"
OUT="${JORNADA_HML_READINESS_OUT:-$ROOT/.local/hml-readiness}"
mkdir -p "$OUT"
python3 "$ROOT/scripts/environment-preflight-gate.py" --root "$ROOT" --profile hml-runtime --strict --summary "$OUT/environment-summary.json"
python3 "$ROOT/scripts/governance-readiness-gate.py" --root "$ROOT" --require-approved --summary "$OUT/governance-summary.json"
python3 "$ROOT/scripts/scheduler-contract-gate.py" --root "$ROOT" --require-scheduled --summary "$OUT/scheduler-summary.json"
python3 "$ROOT/scripts/hml-config-gate.py" --root "$ROOT" --require-approved --summary "$OUT/config-summary.json"
python3 "$ROOT/scripts/hml-staleness-gate.py" --strict | tee "$OUT/staleness.txt"
python3 "$ROOT/scripts/performance-evidence-gate.py" "$JORNADA_HML_PERFORMANCE_REPORT" \
  --baseline "$ROOT/config/hml/performance-baseline.json" --require-baseline-approved \
  --summary "$OUT/performance-summary.json"
python3 "$ROOT/scripts/linkage-evaluation-evidence-gate.py" "$JORNADA_HML_LINKAGE_REPORT" \
  --policy "$ROOT/config/hml/linkage-evaluation-policy.json" --require-policy-approved \
  --summary "$OUT/linkage-summary.json"
python3 "$ROOT/scripts/linkage-statistical-readiness-gate.py" \
  --root "$ROOT" --require-approved \
  --summary "$OUT/linkage-statistical-summary.json"
python3 "$ROOT/scripts/sql-performance-evidence-gate.py" "$JORNADA_HML_SQL_PERFORMANCE_REPORT" \
  --policy "$ROOT/config/hml/sql-performance-policy.json" --require-policy-approved \
  --summary "$OUT/sql-performance-summary.json"
python3 "$ROOT/scripts/api-projection-evidence-gate.py" "$JORNADA_HML_API_PROJECTION_REPORT" \
  --policy "$ROOT/config/hml/api-projection-load-policy.json" --require-policy-approved \
  --summary "$OUT/api-projection-summary.json"
printf 'status=OK\nenvironment=READY\ngovernance=APPROVED\nscheduler=APPROVED\nparameters=APPROVED_AND_CURRENT\nperformance=APPROVED_AND_WITHIN_BASELINE\nlinkage=APPROVED_AND_WITHIN_POLICY\nlinkageStatistical=APPROVED_EVIDENCE_ATTESTED\nsqlPerformance=APPROVED_AND_WITHIN_POLICY\napiProjection=APPROVED_AND_WITHIN_POLICY\ncalibrationFreshness=PASS\n' > "$OUT/result.txt"
echo "HML READINESS GATE: OK"
