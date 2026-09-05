#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
"$ROOT/scripts/local-db.sh" up
# shellcheck disable=SC1091
set -a; source "$ROOT/.env"; set +a
export JORNADA_TEST_SQL_CONNECTION="Server=localhost,${JORNADA_SQL_PORT:-14333};Database=${JORNADA_SQL_DATABASE:-JornadaLocal};User Id=sa;Password=${JORNADA_SQL_SA_PASSWORD};TrustServerCertificate=true;Encrypt=false"
cd "$ROOT"
command -v python3 >/dev/null 2>&1 || { echo "ERRO: python3 é necessário para o gate OpenAPI." >&2; exit 2; }
python3 scripts/openapi-contract-gate.py
python3 scripts/technical-closure-gate.py
"$ROOT/scripts/local-sql-runtime-smoke.sh"
dotnet restore Jornada.sln
dotnet build Jornada.sln --configuration Release --no-restore -warnaserror
dotnet test tests/Jornada.Tests/Jornada.Tests.csproj --configuration Release --no-build
dotnet test tests/Jornada.Integration.Tests/Jornada.Integration.Tests.csproj --configuration Release --no-build
