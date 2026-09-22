#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$ROOT/.env"
EXAMPLE="$ROOT/.env.example"
API_BASE="${JORNADA_SYNTH_API_URL:-http://127.0.0.1:5098}"

: "${JORNADA_SYNTH_PSEUDONYMIZATION_KEY:?Defina JORNADA_SYNTH_PSEUDONYMIZATION_KEY com ao menos 16 bytes}"

[[ -f "$ENV_FILE" ]] || cp "$EXAMPLE" "$ENV_FILE"

"$ROOT/scripts/local-db.sh" up --no-synthetic-corpus

# shellcheck disable=SC1090
set -a
source "$ENV_FILE"
set +a

: "${JORNADA_SQL_SA_PASSWORD:?JORNADA_SQL_SA_PASSWORD não definido}"
PORT="${JORNADA_SQL_PORT:-14333}"
DB="${JORNADA_SQL_DATABASE:-JornadaLocal}"

(
  cd "$ROOT"
  docker compose --env-file "$ENV_FILE" exec -T -w /workspace \
    -e "SQLCMDPASSWORD=$JORNADA_SQL_SA_PASSWORD" sqlserver \
    /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I \
    -d "$DB" -i scripts/local-synthetic-calibration-clean.sql
)

export ConnectionStrings__Jornada="Server=localhost,$PORT;Database=$DB;User Id=sa;Password=$JORNADA_SQL_SA_PASSWORD;TrustServerCertificate=true;Encrypt=false"
export Database__Provider=SqlServer
export Ensaio__Mode=SYNTHETIC_CALIBRATION_DEV
export Ensaio__Endpoints__IngestaoEntregas="$API_BASE/api/v1/ingestao/entregas"

if [[ -n "${JORNADA_SYNTH_PEOPLE:-}" ]]; then
  export Ensaio__SyntheticCalibration__People="$JORNADA_SYNTH_PEOPLE"
fi
if [[ -n "${JORNADA_SYNTH_SEED:-}" ]]; then
  export Ensaio__SyntheticCalibration__Seed="$JORNADA_SYNTH_SEED"
fi
export Ensaio__SyntheticCalibration__ExpectedSeeds="${JORNADA_SYNTH_EXPECTED_SEEDS:-${JORNADA_SYNTH_SEED:-42}}"
if [[ -n "${JORNADA_SYNTH_RUN_GROUP_ID:-}" ]]; then
  export Ensaio__SyntheticCalibration__RunGroupId="$JORNADA_SYNTH_RUN_GROUP_ID"
else
  unset Ensaio__SyntheticCalibration__RunGroupId || true
fi
if [[ -n "${JORNADA_SYNTH_ERROR_PROFILE:-}" ]]; then
  export Ensaio__SyntheticCalibration__ErrorProfile="$JORNADA_SYNTH_ERROR_PROFILE"
fi
if [[ -n "${JORNADA_SYNTH_DATA_REFERENCIA:-}" ]]; then
  export Ensaio__SyntheticCalibration__DataReferencia="$JORNADA_SYNTH_DATA_REFERENCIA"
fi

cd "$ROOT"
dotnet run --project src/Jornada.Ensaio --configuration Release
