#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$ROOT/.env"
[[ -f "$ENV_FILE" ]] || cp "$ROOT/.env.example" "$ENV_FILE"
# shellcheck disable=SC1090
set -a; source "$ENV_FILE"; set +a
: "${JORNADA_SQL_SA_PASSWORD:?JORNADA_SQL_SA_PASSWORD não definido}"
DB="${JORNADA_SQL_DATABASE:-JornadaLocal}"
[[ "$DB" =~ ^[A-Za-z0-9_]+$ ]] || { echo "ERRO: nome de banco inválido." >&2; exit 2; }
compose(){ (cd "$ROOT" && docker compose --env-file "$ENV_FILE" "$@"); }
sqlcmd(){ compose exec -T -e "SQLCMDPASSWORD=$JORNADA_SQL_SA_PASSWORD" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b "$@"; }
# O banco local já deve estar disponível; local-test chama local-db.sh antes deste gate.
sqlcmd -d "$DB" -i /workspace/database/Jornada_Fase1.sql
sqlcmd -d "$DB" -i /workspace/database/Jornada_Seed_Dev.sql
sqlcmd -d "$DB" -i /workspace/database/Jornada_Runtime_Smoke.sql
