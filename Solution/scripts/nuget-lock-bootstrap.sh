#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${JORNADA_NUGET_LOCK_OUT:-$ROOT/.local/nuget-lock}"
mkdir -p "$OUT"
cd "$ROOT"
dotnet --version | grep -Fx '8.0.424' >/dev/null
dotnet restore Jornada.sln --use-lock-file --force-evaluate
find . -name packages.lock.json -not -path './.local/*' -print0 | sort -z | tar --null -T - -czf "$OUT/packages-locks.tar.gz"
printf '{"bootstrap":"LOCK_REFRESH"}\n' > "$OUT/summary.json"
printf '{"bootstrap":"LOCK_REFRESH"}\n' > "$OUT/provenance-summary.json"
echo "Lock refresh artifact generated."
