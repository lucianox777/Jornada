#!/usr/bin/env bash
# Technical fixture only: no SQL, scorer, fit or scientific campaign.
set -euo pipefail
umask 077
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
private="$(mktemp -d)"
trap 'rm -rf -- "$private"' EXIT
historical="$ROOT/src/Jornada.Linkage.SyntheticCorpus/Jornada.Linkage.SyntheticCorpus.csproj"
canonical="$ROOT/src/Jornada.Linkage.SyntheticCorpus.Parametrized/Jornada.Linkage.SyntheticCorpus.Parametrized.csproj"
for project in "$historical" "$canonical"; do
  dotnet restore "$project" --locked-mode >"$private/build.log" 2>&1 || { echo V9_GENERATOR_PARITY_RESTORE_FAILED >&2; exit 1; }
  dotnet build "$project" -c Release --no-restore -warnaserror >"$private/build.log" 2>&1 || { echo V9_GENERATOR_PARITY_BUILD_FAILED >&2; exit 1; }
done
# Ephemeral key shared only between the two technical fixtures, not across attempts.
export JORNADA_SYNTH_PSEUDONYMIZATION_KEY="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"
common=(--reference-root "$ROOT/data/reference/ibge-nomes-2022"
  --population-profile demographic-primary
  --birth-daily-source "$ROOT/data/reference/synthetic-birth-sp/birth_daily_sp_projection2024_2026.json"
  --people 1000 --seed 355 --error-profile clean --gestores 4
  --data-referencia 2026-11-22T00:00:00+00:00)
waves=(--wave-count 2 --wave-delayed-arrival-rate 0 --wave-cpf-reveal-rate 0
  --wave-name-correction-rate 1 --wave-mother-correction-rate 1 --wave-birth-recovery-rate 1)
for mode in generate-ingestion generate-ingestion-waves; do
  extra=()
  if [[ "$mode" == generate-ingestion-waves ]]; then extra=("${waves[@]}"); fi
  dotnet run --no-build -c Release --project "$historical" -- "$mode" "${common[@]}" "${extra[@]}" --out "$private/$mode/historical" >"$private/generator.log" 2>&1 || { echo V9_HISTORICAL_FIXTURE_FAILED >&2; exit 1; }
  dotnet run --no-build -c Release --project "$canonical" -- "$mode" "${common[@]}" "${extra[@]}" --train-percent 60 --validation-percent 20 --test-percent 20 --out "$private/$mode/canonical" >"$private/generator.log" 2>&1 || { echo V9_CANONICAL_FIXTURE_FAILED >&2; exit 1; }
  python3 - "$private/$mode" <<'PY'
import hashlib
import json
from pathlib import Path
import sys

root = Path(sys.argv[1])
old, new = root / "historical", root / "canonical"
def file_set(directory):
    return {p.relative_to(directory) for p in directory.rglob("*") if p.is_file()}
def require(condition, error):
    if not condition:
        raise SystemExit(error)
paths = file_set(old)
require(paths == file_set(new), "V9_GENERATOR_PARITY_FILE_SET")
manifest = Path("corpus/generation-manifest.json")
require(manifest in paths and Path("corpus/observacoes.csv") in paths
        and Path("corpus/pessoas_verdade.csv") in paths and Path("corpus/gabarito.json") in paths,
        "V9_GENERATOR_PARITY_REQUIRED_CORPUS")
require(any(p.suffix == ".zip" for p in paths), "V9_GENERATOR_PARITY_REQUIRED_ZIPS")
for path in sorted(paths - {manifest}):
    require(hashlib.sha256((old / path).read_bytes()).digest()
            == hashlib.sha256((new / path).read_bytes()).digest(),
            "V9_GENERATOR_PARITY_BYTES_MISMATCH")
baseline = json.loads((old / manifest).read_text(encoding="utf-8"))
evolved = json.loads((new / manifest).read_text(encoding="utf-8"))
require(evolved.pop("synthetic_population", None) == 1000, "V9_GENERATOR_PARITY_POPULATION")
require(evolved.pop("partition_percentages", None) == {"train": 60, "validation": 20, "test": 20},
        "V9_GENERATOR_PARITY_PARTITIONS")
require(baseline == evolved, "V9_GENERATOR_PARITY_MANIFEST_MISMATCH")
print("V9_CANONICAL_GENERATOR_PARITY_PASS", root.name, len(paths))
PY
done
