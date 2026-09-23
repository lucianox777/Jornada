#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="${JORNADA_LOCAL_ENV_FILE:-$ROOT/.env}"
# shellcheck disable=SC1090
set -a; source "$ENV_FILE"; set +a
: "${JORNADA_SQL_SA_PASSWORD:?Senha SQL nao definida}"
DB="${1:-${JORNADA_SQL_DATABASE:-JornadaLocal}}"
[[ "$DB" =~ ^[A-Za-z0-9_]+$ ]] || { echo 'Nome de banco invalido.' >&2; exit 1; }
echo "Lendo diagnostico agregado do banco $DB (somente SELECT, sem limpeza)."
(
  cd "$ROOT"
  docker compose --env-file "$ENV_FILE" exec -T -w /workspace \
    -e "SQLCMDPASSWORD=$JORNADA_SQL_SA_PASSWORD" sqlserver \
    /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d "$DB" -w 900 \
    -i database/Jornada_Dev_SyntheticCalibration_Diagnostics.sql
)
