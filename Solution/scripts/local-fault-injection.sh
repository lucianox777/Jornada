#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
"$ROOT/scripts/local-db.sh" up
# shellcheck disable=SC1091
set -a; source "$ROOT/.env"; set +a
export JORNADA_TEST_SQL_CONNECTION="Server=localhost,${JORNADA_SQL_PORT:-14333};Database=${JORNADA_SQL_DATABASE:-JornadaLocal};User Id=sa;Password=${JORNADA_SQL_SA_PASSWORD};TrustServerCertificate=true;Encrypt=false"
mkdir -p "$ROOT/.local/test-results"
cd "$ROOT"
if [[ "${JORNADA_LOCKED_RESTORE:-false}" == "true" ]]; then
  dotnet restore Jornada.sln --locked-mode
else
  dotnet restore Jornada.sln
fi
dotnet build Jornada.sln --configuration Release --no-restore -warnaserror
dotnet test tests/Jornada.Integration.Tests/Jornada.Integration.Tests.csproj --configuration Release --no-build \
  --filter "TestCategory=FaultInjection" \
  --logger "trx;LogFileName=$ROOT/.local/test-results/fault-injection.trx"
python3 scripts/test-evidence-gate.py .local/test-results/fault-injection.trx \
  --forbid-skipped --minimum-tests 2 --summary .local/test-results/fault-injection-summary.json
echo "Fault injection concluído sem skips. Evidências: .local/test-results/fault-injection.trx e fault-injection-summary.json"
