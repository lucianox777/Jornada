#!/usr/bin/env bash
set -euo pipefail
ROOT="${1:-$(cd "$(dirname "$0")/.." && pwd)}"
OUT="${2:-$ROOT/.local/deterministic-build}"
mkdir -p "$OUT"
manifest(){
  local target="$1"
  (cd "$ROOT" && find src -type f \( -path '*/bin/Release/net8.0/*.dll' -o -path '*/bin/Release/net8.0/*.pdb' -o -path '*/bin/Release/net8.0/*.deps.json' -o -path '*/bin/Release/net8.0/*.runtimeconfig.json' \) -print0 | sort -z | xargs -0 sha256sum) > "$target"
  test -s "$target" || { echo 'DETERMINISTIC BUILD GATE: FAIL: nenhum artefato Release encontrado' >&2; exit 1; }
}
cd "$ROOT"
dotnet clean Jornada.sln --configuration Release >/dev/null
dotnet build Jornada.sln --configuration Release --no-restore >/dev/null
manifest "$OUT/build1.sha256"
dotnet clean Jornada.sln --configuration Release >/dev/null
dotnet build Jornada.sln --configuration Release --no-restore >/dev/null
manifest "$OUT/build2.sha256"
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
