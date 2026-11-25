#!/usr/bin/env bash
# Prepare a private, synthetic fixture only. No HTTP, SQL, fit or Runner.
# Caller owns the private root and its eventual cleanup.
set -euo pipefail
umask 077
[[ "$#" == 1 && ! -e "$1" && ! -L "$1" ]] || {
  echo 'V9_E2E_1K_PRIVATE_FIXTURE_NEW_DIRECTORY_REQUIRED' >&2; exit 2;
}
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$1"
mkdir -m 700 -- "$OUT"
# Two HTTP delivery waves in the eventual E2E, followed by ONE FULL.
# Keep the existing seed/profile; do not reveal CPF in the second wave.
# Recover the already-generated truth's nominal attributes so some CPF
# origins acquire distinct factual versions without inventing a new person.
if ! JORNADA_SYNTH_PSEUDONYMIZATION_KEY="$(python3 -c 'import secrets; print(secrets.token_hex(32))')" \
  dotnet run --no-build --configuration Release \
    --project "$ROOT/src/Jornada.Linkage.SyntheticCorpus/Jornada.Linkage.SyntheticCorpus.csproj" -- \
    generate-ingestion-waves \
    --reference-root "$ROOT/data/reference/ibge-nomes-2022" \
    --population-profile demographic-primary \
    --birth-daily-source "$ROOT/data/reference/synthetic-birth-sp/birth_daily_sp_projection2024_2026.json" \
    --people 1000 --seed 355 --error-profile clean --gestores 4 \
    --wave-count 2 --wave-delayed-arrival-rate 0 --wave-cpf-reveal-rate 0 \
    --wave-name-correction-rate 1 --wave-mother-correction-rate 1 --wave-birth-recovery-rate 1 \
    --data-referencia 2026-11-22T00:00:00+00:00 \
    --out "$OUT" >"$OUT/generator-private.log" 2>&1; then
  echo 'V9_E2E_1K_PRIVATE_FIXTURE_GENERATION_FAILED' >&2; exit 1
fi
rm -f -- "$OUT/generator-private.log"
python3 "$ROOT/scripts/v9-e2e-1k-wave-preflight.py" \
  --root "$OUT" --receipt "$OUT/wave-preflight-receipt.json"
