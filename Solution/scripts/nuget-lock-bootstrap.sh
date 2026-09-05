#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${JORNADA_NUGET_LOCK_OUT:-$ROOT/.local/nuget-lock}"
command -v dotnet >/dev/null 2>&1 || { echo "ERRO: dotnet não encontrado." >&2; exit 2; }
command -v python3 >/dev/null 2>&1 || { echo "ERRO: python3 não encontrado." >&2; exit 2; }
mkdir -p "$OUT"
cd "$ROOT"
dotnet --version | grep -Fx '8.0.424' >/dev/null || {
  echo "ERRO: SDK ativo deve ser exatamente 8.0.424 (global.json)." >&2
  exit 3
}
dotnet restore Jornada.sln --use-lock-file --force-evaluate
if ! git diff --quiet -- '**/packages.lock.json'; then
  echo "ERRO: force-evaluate alterou packages.lock.json versionado; atualize locks e proveniência." >&2
  git diff --stat -- '**/packages.lock.json' >&2
  exit 4
fi
python3 scripts/nuget-lock-gate.py --root . --summary "$OUT/summary.json"
python3 scripts/nuget-lock-provenance-gate.py --root . --summary "$OUT/provenance-summary.json"
dotnet restore Jornada.sln --locked-mode
find . -name packages.lock.json -not -path './.local/*' -print0 | sort -z | tar --null -T - -czf "$OUT/packages-locks.tar.gz"
echo "NuGet lock bootstrap OK: $OUT/packages-locks.tar.gz"
