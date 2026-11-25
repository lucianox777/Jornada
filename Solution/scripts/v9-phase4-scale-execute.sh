#!/usr/bin/env bash
set -euo pipefail
umask 077
root="$JORNADA_V9_E2E_PRIVATE_ROOT"
run_cli() {
  flag=--v9-dev-full-10k
  [[ "$JORNADA_V9_PHASE4_SCALE" != audit1k ]] || flag=--v9-dev-full-1k
  dotnet run --no-build -c Release --no-launch-profile --project src/Jornada.Linkage.Runner -- "$flag" "$1" >"$root/runner-$1.log" 2>&1 || {
    grep -E '^V9_DEV_E2E_FAILURE stage=[a-z-]+ type=[A-Za-z0-9_-]+ code=[A-Z0-9_]+$' "$root/runner-$1.log" || true
    echo "V9_E2E_RUNNER_STAGE_FAILED_$1"; exit 1;
  }
}
run_cli catalog
echo 'V9_E2E_CATALOG_EXECUTED'
contracts="$PWD/config/contracts/gestores/SEHAB"
support="$PWD/tests/fixtures/external-contracts/gestores/SEHAB"
if [[ -e "$contracts" ]]; then diff -qr "$support" "$contracts" >/dev/null; else cp -R "$support" "$contracts"; fi
export ASPNETCORE_URLS=http://127.0.0.1:5088
export BronzeStorage__Provider=FileSystem
export BronzeStorage__RootPath="$root/bronze"
export IngestionStaging__RootPath="$root/staging"
export Processor__PollingMilliseconds=100
export SemiblindIdentitySearch__Enabled=false
dotnet run --no-build -c Release --no-launch-profile --project src/Jornada.Api >"$root/api.log" 2>&1 & api=$!
dotnet run --no-build -c Release --no-launch-profile --project src/Jornada.Processor.Worker >"$root/processor.log" 2>&1 & worker=$!
ASPNETCORE_URLS=http://127.0.0.1:5089 JornadaApiBaseUrl=http://127.0.0.1:5088 dotnet run --no-build -c Release --no-launch-profile --project src/Jornada.Resultado.Api >"$root/resultado.log" 2>&1 & resultado=$!
trap 'kill "$worker" "$api" "$resultado" 2>/dev/null || true; wait "$worker" "$api" "$resultado" 2>/dev/null || true' EXIT
ready=0
for _ in $(seq 1 120); do
  if curl --silent --fail --output "$root/ready.json" http://127.0.0.1:5088/health/ready; then ready=1; break; fi
  sleep 1
done
[[ "$ready" == 1 ]] || { echo 'V9_E2E_API_READINESS_FAILED'; exit 1; }
ready=0
for _ in $(seq 1 60); do
  if curl --silent --fail --output "$root/resultado-ready.json" http://127.0.0.1:5089/health; then ready=1; break; fi
  sleep 1
done
[[ "$ready" == 1 ]] || { echo 'V9_E2E_RESULTADO_READINESS_FAILED'; exit 1; }
python3 scripts/v9-phase4-scale.py ingest --root "$root/fixture"
cp "$root/fixture/http-lineage.json" "$root/http-lineage.json"
export JORNADA_V9_PHASE4_MATRIX=true
run_cli full
python3 scripts/v9-e2e-1k-membership-oracle.py --root "$root"
echo 'V9_E2E_FULL_PUBLICATION_PASS_NOT_INTEGRAL'
if [[ "$JORNADA_V9_PHASE4_SCALE" == diagnostic* || "$JORNADA_V9_PHASE4_SCALE" == optimized1k ]]; then
  python3 scripts/v9-phase4-scale.py diagnostic --root "$root"
  out="$RUNNER_TEMP/v9-scale-aggregate"
  mkdir -m 700 "$out"
  cp "$root/diagnostic-receipt.json" "$root/membership-receipt.json" "$root/scalar-receipt.json" "$root/actual-graph-receipt.json" "$out/"
  printf '%s\n' "$JORNADA_E2E_EXACT_SHA" > "$out/commit.txt"
  echo 'V9_FULL_SCALE_DIAGNOSTIC_MEASURED_NOT_MATRIX'
  exit 0
fi
python3 scripts/v9-phase4-scale.py plan --root "$root"
run_cli matrix
python3 scripts/v9-phase4-scale.py measure --root "$root"
out="$RUNNER_TEMP/v9-scale-aggregate"
mkdir -m 700 "$out"
cp "$root/selection/winner-pin-receipt.json" "$root/http-lineage.json" "$root/runner-receipt.json" "$root/identity-receipt.json" "$root/membership-receipt.json" "$root/scalar-receipt.json" "$root/actual-graph-receipt.json" "$root/s4b-receipt.json" "$root/scale-receipt.json" "$root/case-audit.jsonl" "$out/"
printf '%s\n' "$JORNADA_E2E_EXACT_SHA" > "$out/commit.txt"
echo 'V9_SCALE_MEASURED_NONPUBLISHING'
