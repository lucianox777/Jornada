#!/usr/bin/env bash
set -euo pipefail

ACTION="${1:-up}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$ROOT/.env"
EXAMPLE="$ROOT/.env.example"

need() { command -v "$1" >/dev/null 2>&1 || { echo "ERRO: comando '$1' não encontrado." >&2; exit 2; }; }
need docker

if [[ ! -f "$ENV_FILE" ]]; then
  cp "$EXAMPLE" "$ENV_FILE"
  echo "Criado .env local a partir de .env.example. Revise a senha antes de uso compartilhado." >&2
fi

# shellcheck disable=SC1090
set -a; source "$ENV_FILE"; set +a
: "${JORNADA_SQL_SA_PASSWORD:?JORNADA_SQL_SA_PASSWORD não definido}"
JORNADA_SQL_PORT="${JORNADA_SQL_PORT:-14333}"
JORNADA_SQL_DATABASE="${JORNADA_SQL_DATABASE:-JornadaLocal}"
mkdir -p "$ROOT/.local/sql-backup"
chmod 0777 "$ROOT/.local/sql-backup"
[[ "$JORNADA_SQL_DATABASE" =~ ^[A-Za-z0-9_]+$ ]] || { echo "ERRO: JORNADA_SQL_DATABASE inválido." >&2; exit 2; }

compose() { (cd "$ROOT" && docker compose --env-file "$ENV_FILE" "$@"); }
sqlcmd() {
  compose exec -T -e "SQLCMDPASSWORD=$JORNADA_SQL_SA_PASSWORD" sqlserver \
    /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I "$@"
}
wait_healthy() {
  local i status
  for i in $(seq 1 60); do
    status="$(docker inspect -f '{{.State.Health.Status}}' jornada-sqlserver-local 2>/dev/null || true)"
    [[ "$status" == "healthy" ]] && return 0
    sleep 2
  done
  echo "ERRO: SQL Server não ficou healthy no tempo esperado." >&2
  compose logs sqlserver >&2 || true
  exit 3
}
apply_migrations() {
  compose exec -T \
    -e "SQLCMDPASSWORD=$JORNADA_SQL_SA_PASSWORD" \
    -e "JORNADA_SQL_DATABASE=$JORNADA_SQL_DATABASE" \
    -e "SQLCMD_BIN=/opt/mssql-tools18/bin/sqlcmd" \
    sqlserver bash /workspace/scripts/apply-migrations.sh
}
bootstrap() {
  sqlcmd -Q "IF DB_ID(N'$JORNADA_SQL_DATABASE') IS NULL CREATE DATABASE [$JORNADA_SQL_DATABASE];"
  sqlcmd -d "$JORNADA_SQL_DATABASE" -i /workspace/database/Jornada_Fase1.sql
  sqlcmd -d "$JORNADA_SQL_DATABASE" -i /workspace/database/Jornada_Seed_Dev.sql
  # O baseline contém a base funcional; toda extensão operacional posterior é aplicada
  # pelo manifesto canônico, com histórico e checksum fail-closed.
  apply_migrations
}

case "$ACTION" in
  up)
    compose up -d sqlserver
    wait_healthy
    bootstrap
    echo "SQL Server Developer local pronto: localhost:$JORNADA_SQL_PORT / $JORNADA_SQL_DATABASE"
    ;;
  reset)
    compose up -d sqlserver
    wait_healthy
    sqlcmd -Q "IF DB_ID(N'$JORNADA_SQL_DATABASE') IS NOT NULL BEGIN ALTER DATABASE [$JORNADA_SQL_DATABASE] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$JORNADA_SQL_DATABASE]; END; CREATE DATABASE [$JORNADA_SQL_DATABASE];"
    bootstrap
    echo "Banco local recriado: $JORNADA_SQL_DATABASE"
    ;;
  down)
    compose down
    ;;
  clean)
    compose down -v
    ;;
  status)
    compose ps
    ;;
  *) echo "Uso: $0 {up|reset|down|clean|status}" >&2; exit 2 ;;
esac
