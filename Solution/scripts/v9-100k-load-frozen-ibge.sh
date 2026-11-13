#!/usr/bin/env bash
# 100k-only: rehydrate the already-frozen repository IBGE projection into
# one-use JornadaHarness SQL, using the historic 30k *metadata* from the plan.
# NEVER regenerate a 30k synthetic corpus or call the remote IBGE API here.
# Do not change shared v9-tf-prepare-pin.sh for legacy 30k studies.
set -euo pipefail
if [[ "$#" -ne 2 ]]; then
  echo "Usage: v9-100k-load-frozen-ibge.sh <pinned-100k-plan.json> <new-output-dir>" >&2
  exit 64
fi
plan="$1"
root="$2"

if [[ ! "${EXACT_SHA:-}" =~ ^[0-9a-f]{40}$ ||
      "${EXACT_SHA:-}" != "${GITHUB_SHA:-}" ||
      "${GITHUB_REF:-}" != "refs/heads/master" ||
      "${GITHUB_EVENT_NAME:-}" != "workflow_dispatch" ||
      "${GITHUB_ACTOR:-}" != "${GITHUB_REPOSITORY_OWNER:-}" ||
      "${JORNADA_RUNTIME_MODE:-}" != "DEV" ]]; then
  echo "V9_100K_FROZEN_IBGE: unauthorized or unpinned manual job context" >&2
  exit 2
fi
if [[ "${ConnectionStrings__Jornada:-}" != *"Database=JornadaHarness;"* ||
      -z "${JORNADA_SQL_SA_PASSWORD:-}" ||
      ! -f "$plan" || -e "$root" ]]; then
  echo "V9_100K_FROZEN_IBGE: SQL, plan or new directory admission failed" >&2
  exit 2
fi
command -v docker >/dev/null
mkdir -p "$root"

registry="data/reference/v9-synthetic-seed42-30000/manifest.json"
ibge="data/reference/ibge-nomes-2022/manifest.json"
# Fail before any SQL I/O if immutable metadata or pinned plan changed.
python3 scripts/v9-100k-frozen-ibge-admission.py bootstrap \
  --plan "$plan" --frozen-registry "$registry" --ibge-manifest "$ibge" \
  --bootstrap "$root/frozen-bootstrap.json"

sql_name="v9-probe-sql-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}"
CID="$(docker ps -q --filter "name=^/${sql_name}$" | head -1)"
test -n "$CID" || { echo "V9_100K_FROZEN_IBGE: one-use SQL container absent" >&2; exit 2; }
docker exec "$CID" mkdir -p /var/opt/mssql/data/jornada/database
docker cp database/. "$CID":/var/opt/mssql/data/jornada/database/
sqlcmd_docker() {
  SQLCMDPASSWORD="$JORNADA_SQL_SA_PASSWORD" docker exec \
    -w /var/opt/mssql/data/jornada -e SQLCMDPASSWORD "$CID" \
    /opt/mssql-tools18/bin/sqlcmd -I -S localhost -U sa -C -b "$@"
}
sqlcmd_docker -Q "IF DB_ID(N'JornadaHarness') IS NOT NULL THROW 51000, 'Preexisting database', 1; CREATE DATABASE JornadaHarness;"
sqlcmd_docker -d JornadaHarness -i database/Jornada_Fase1_v3.70.sql
sqlcmd_docker -d JornadaHarness -i database/Jornada_Seed_Dev.sql
sqlcmd_docker -d JornadaHarness -i database/migrations/20260907_Cpf_Ancora.sql
sqlcmd_docker -d JornadaHarness -i database/migrations/20260910_Schema_Consolidation_370.sql
sqlcmd_docker -d JornadaHarness -i database/migrations/20260930_Linkage_Z_Model_Config_Bundle_Fingerprint.sql

# Rehydrate the pinned local IBGE snapshot in newly empty SQL, not generating
# IBGE or downloading from API, and not rebuilding any synthetic 30k corpus.
DOTNET_ENVIRONMENT=Development \
LinkageParameters__Operation=LOAD_NAME_FREQUENCY_SNAPSHOT \
NameFrequencySnapshot__ManifestPath="$PWD/$ibge" \
  dotnet run --no-build -c Release --project src/Jornada.Linkage.Parameters.Worker

# Independently calculate the physical SHA with the production C# materializer
# and compare it to the frozen plan and historical reference registry.
dotnet run --no-build -c Release --project src/Jornada.Linkage.Parameters.Worker -- \
  --v9-tf-export-ibge-pin "$root/frozen-bootstrap.json" "$root/physical-ibge-pin.json"
python3 scripts/v9-100k-frozen-ibge-admission.py verify \
  --plan "$plan" --frozen-registry "$registry" --ibge-manifest "$ibge" \
  --actual-pin "$root/physical-ibge-pin.json"

# Re-register only frozen 30k provenance metadata in disposable SQL.
# No 30k CSV, gabarito or frequency table is re-created.
sqlcmd_docker -d JornadaHarness -i database/migrations/20261103_Ref_Corpus_Sintetico_Evidencia_V9.sql
echo "PASS: frozen IBGE physical SHA matched; 30k provenance reused; no 30k regeneration, no TEST."
