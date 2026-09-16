#!/usr/bin/env bash
set -euo pipefail

ACTION="${1:-up}"
SKIP_SYNTHETIC_SCALE="${JORNADA_LOCAL_SKIP_SYNTHETIC_SCALE:-false}"
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
  # O baseline v3.70 usa diretivas :r relativas ao diretório /workspace.
  # Fixar o working directory evita que sqlcmd resolva includes a partir de /.
  compose exec -T -w /workspace -e "SQLCMDPASSWORD=$JORNADA_SQL_SA_PASSWORD" sqlserver \
    /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I "$@"
}
sql_scalar() {
  local query="$1"
  sqlcmd -d "$JORNADA_SQL_DATABASE" -W -h -1 -Q "SET NOCOUNT ON; $query" \
    | awk 'NF{last=$0} END{gsub(/^[[:space:]]+|[[:space:]]+$/, "", last); print last}'
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
ensure_progressive_identity_backfill() {
  local has_silver remaining next
  has_silver="$(sql_scalar "SELECT CASE WHEN OBJECT_ID('silver.pessoa_origem','U') IS NULL THEN 0 ELSE 1 END;")"
  [[ "$has_silver" == "1" ]] || return 0

  # Em base local existente, instala/reaplica somente a persistência progressiva antes
  # do cutover fail-closed. A criação do initial_uuid continua na procedure canônica.
  sqlcmd -d "$JORNADA_SQL_DATABASE" -i database/Jornada_Identidade_Progressiva.sql
  remaining="$(sql_scalar "SELECT COUNT_BIG(*) FROM silver.pessoa_origem o LEFT JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=o.pessoa_origem_id WHERE p.pessoa_origem_id IS NULL;")"
  [[ "$remaining" =~ ^[0-9]+$ ]] || { echo "ERRO: contagem inválida no backfill progressivo: $remaining" >&2; return 4; }
  while (( remaining > 0 )); do
    echo "Backfill progressivo local: $remaining origens sem initial_uuid..."
    sqlcmd -d "$JORNADA_SQL_DATABASE" -v PAGE_SIZE=1000 -i scripts/local-progressive-identity-backfill.sql
    next="$(sql_scalar "SELECT COUNT_BIG(*) FROM silver.pessoa_origem o LEFT JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=o.pessoa_origem_id WHERE p.pessoa_origem_id IS NULL;")"
    [[ "$next" =~ ^[0-9]+$ ]] || { echo "ERRO: contagem inválida após página do backfill: $next" >&2; return 4; }
    (( next < remaining )) || { echo "ERRO: backfill progressivo local não avançou: restantes=$next." >&2; return 4; }
    remaining="$next"
  done
  echo "Backfill progressivo local concluído: todas as origens possuem initial_uuid."
}
ensure_synthetic_scale() {
  local count
  count="$(sql_scalar "SELECT COUNT_BIG(*) FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SCALE-%';")"
  if [[ "$count" == "0" ]]; then
    echo "Carregando corpus sintético local para calibração/linkage..."
    sqlcmd -d "$JORNADA_SQL_DATABASE" \
      -v SCALE_PEOPLE=5000 SCALE_PAIRED=5000 SCALE_PENDING=1000 SCALE_SEED=355 SCALE_COLLISION_MODULO=37 SCALE_BIRTH_SHIFT_MODULO=29 \
      -i database/Jornada_Dev_SyntheticScale.sql
    count="$(sql_scalar "SELECT COUNT_BIG(*) FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SCALE-%';")"
  fi
  [[ "$count" == "11000" ]] || {
    echo "ERRO: massa sintética local inconsistente: esperadas 11000 pessoas de origem SCALE; encontradas=$count. Execute local-db reset." >&2
    return 4
  }
  echo "Corpus sintético local pronto: 5000 pessoas Gold, 5000 pares corroborados e 1000 pendentes."
}
bootstrap() {
  sqlcmd -Q "IF DB_ID(N'$JORNADA_SQL_DATABASE') IS NULL CREATE DATABASE [$JORNADA_SQL_DATABASE];"

  # Upgrade local de volume existente: o baseline v3.70 contém um cutover fail-closed.
  # Concluímos antes o backfill paginado exigido pela migração, sem apagar a base.
  ensure_progressive_identity_backfill

  # Instalação nova possui um único ponto canônico. O arquivo v3.70 aplica baseline,
  # identidade progressiva, composição, blocking/ruleset e valida a completude antes
  # de promover Jornada.SolutionSchema=3.70.
  sqlcmd -d "$JORNADA_SQL_DATABASE" -i database/Jornada_Fase1_v3.70.sql
  sqlcmd -d "$JORNADA_SQL_DATABASE" -i database/Jornada_Seed_Dev.sql
  sqlcmd -d "$JORNADA_SQL_DATABASE" -i database/Jornada_Seed_Dev_UniquePayloads.sql
  # DEV possui seed; reaplicação idempotente reserva também CPFs históricos do seed.
  sqlcmd -d "$JORNADA_SQL_DATABASE" -i database/migrations/20260907_Cpf_Ancora.sql
  sqlcmd -d "$JORNADA_SQL_DATABASE" -i database/migrations/20260910_Schema_Consolidation_370.sql
  # O perfil local padrão exercita o calibrador com a massa sintética DEV. Harnesses
  # de escala podem omiti-la explicitamente para instalar o próprio corpus versionado.
  if [[ "$SKIP_SYNTHETIC_SCALE" == "true" ]]; then
    echo "Corpus sintético SCALE padrão omitido para bootstrap externo."
  else
    ensure_synthetic_scale
  fi

  # Seed e massa SCALE são inserções DEV diretas e não passam pelo Processor. Fechamos
  # a invariável ao fim do bootstrap para que o próximo 'up' também seja reentrante.
  ensure_progressive_identity_backfill
}

case "$ACTION" in
  up)
    compose up -d sqlserver
    wait_healthy
    bootstrap
    echo "SQL Server Developer local pronto: localhost:$JORNADA_SQL_PORT / $JORNADA_SQL_DATABASE (schema 3.70)"
    ;;
  reset)
    compose up -d sqlserver
    wait_healthy
    sqlcmd -Q "IF DB_ID(N'$JORNADA_SQL_DATABASE') IS NOT NULL BEGIN ALTER DATABASE [$JORNADA_SQL_DATABASE] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$JORNADA_SQL_DATABASE]; END; CREATE DATABASE [$JORNADA_SQL_DATABASE];"
    bootstrap
    echo "Banco local recriado: $JORNADA_SQL_DATABASE (schema 3.70)"
    ;;
  identity-backfill)
    compose up -d sqlserver
    wait_healthy
    ensure_progressive_identity_backfill
    echo "Backfill progressivo de identidade local concluído."
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
  *) echo "Uso: $0 {up|reset|identity-backfill|down|clean|status}" >&2; exit 2 ;;
esac