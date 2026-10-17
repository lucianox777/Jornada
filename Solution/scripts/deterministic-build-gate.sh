#!/usr/bin/env bash
set -euo pipefail
ROOT="${1:-$(cd "$(dirname "$0")/.." && pwd)}"
OUT="${2:-$ROOT/.local/deterministic-build}"
mkdir -p "$OUT"
manifest(){
  local target="$1"
  (cd "$ROOT" && find src -type f \( -path '*/bin/Release/net10.0/*.dll' -o -path '*/bin/Release/net10.0/*.pdb' -o -path '*/bin/Release/net10.0/*.deps.json' -o -path '*/bin/Release/net10.0/*.runtimeconfig.json' \) -print0 | sort -z | xargs -0 sha256sum) > "$target"
  test -s "$target" || { echo 'DETERMINISTIC BUILD GATE: FAIL: nenhum artefato Release encontrado' >&2; exit 1; }
}
cd "$ROOT"
build_once(){
  local label="$1"
  if ! dotnet clean Jornada.sln --configuration Release > "$OUT/${label}-clean.log" 2>&1; then
    echo "DETERMINISTIC BUILD GATE: FAIL: clean ${label}" >&2
    tail -n 120 "$OUT/${label}-clean.log" >&2
    exit 1
  fi
  if ! dotnet build Jornada.sln --configuration Release --no-restore > "$OUT/${label}-build.log" 2>&1; then
    echo "DETERMINISTIC BUILD GATE: FAIL: build ${label}" >&2
    tail -n 160 "$OUT/${label}-build.log" >&2
    exit 1
  fi
  manifest "$OUT/${label}.sha256"
}
build_once build1
build_once build2
if ! diff -u "$OUT/build1.sha256" "$OUT/build2.sha256" > "$OUT/diff.txt"; then
  echo 'DETERMINISTIC BUILD GATE: FAIL: builds consecutivos divergiram' >&2; cat "$OUT/diff.txt" >&2; exit 1
fi
python3 - "$OUT/build1.sha256" "$OUT/summary.json" <<'PY'
import hashlib,json,sys
from pathlib import Path
m=Path(sys.argv[1]); lines=[x for x in m.read_text().splitlines() if x.strip()]
out={'status':'PASS','artifactCount':len(lines),'manifestSha256':hashlib.sha256(m.read_bytes()).hexdigest()}
Path(sys.argv[2]).write_text(json.dumps(out,indent=2)+'\n')
PY
echo "DETERMINISTIC BUILD GATE: OK ($(wc -l < "$OUT/build1.sha256") artifacts)"
