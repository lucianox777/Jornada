#!/usr/bin/env bash
# Shared by preflight and execute_30k. Never runs calibration or TEST.
set -euo pipefail
if [[ "$#" != 1 ]]; then
  echo "Usage: v9-tf-prepare-pin.sh <new-evidence-dir>" >&2
  exit 64
fi
root="$1"
pin_name="${2:-pinned-ibge.json}"
[[ "$pin_name" == "pinned-ibge.json" || "$pin_name" == "reconstructed-ibge.json" ]] || { echo "Invalid pin output name" >&2; exit 1; }
test -n "${ConnectionStrings__Jornada:-}" || { echo "Missing disposable SQL connection" >&2; exit 1; }
[[ "$ConnectionStrings__Jornada" == *"Database=JornadaHarness;"* ]] || {
  echo "Only the disposable JornadaHarness database is permitted" >&2; exit 1;
}
test ! -e "$root/people-30k" && test ! -e "$root/bootstrap.json" && test ! -e "$root/$pin_name" || {
  echo "Evidence directory already contains generation/pin data" >&2; exit 1;
}
mkdir -p "$root"
CID=$(docker ps -q --filter publish=1433 | head -1)
test -n "$CID" || { echo "SQL Server Docker service not found" >&2; exit 1; }
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
DOTNET_ENVIRONMENT=Development \
LinkageParameters__Operation=LOAD_NAME_FREQUENCY_SNAPSHOT \
NameFrequencySnapshot__ManifestPath="$PWD/data/reference/ibge-nomes-2022/manifest.json" \
  dotnet run --no-build -c Release --project src/Jornada.Linkage.Parameters.Worker
active=$(sqlcmd_docker -d JornadaHarness -h -1 -W -Q \
  "SET NOCOUNT ON; SELECT TOP(1) codigo FROM ref.frequencia_nome_versao WHERE status=N'ATIVA';" | tr -d '\r' | xargs)
test "$active" = 'CENSO2022_NOMES_BRASIL_V1'

dotnet run --no-build -c Release --project src/Jornada.Linkage.SyntheticCorpus -- generate \
  --reference-root "$PWD/data/reference/ibge-nomes-2022" \
  --population-profile demographic-primary \
  --birth-daily-source "$PWD/data/reference/synthetic-birth-sp/birth_daily_sp_projection2024_2026.json" \
  --error-profile clean --people 30000 --seed 42 \
  --out "$root/people-30k" > "$root/generation.log"
python3 scripts/v9-tf-pin-source.py bootstrap \
  --root "$root/people-30k" --output "$root/bootstrap.json"
dotnet run --no-build -c Release --project src/Jornada.Linkage.Parameters.Worker -- \
  --v9-tf-export-ibge-pin "$root/bootstrap.json" "$root/$pin_name"
echo "PASS: disposable SQL and 30000-base corpus prepared; pinned reference exported; NO calibration."
