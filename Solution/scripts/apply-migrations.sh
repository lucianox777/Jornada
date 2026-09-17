#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MANIFEST="${JORNADA_MIGRATION_MANIFEST:-$ROOT/database/migrations/manifest.txt}"
DB="${JORNADA_SQL_DATABASE:-JornadaLocal}"
SQLCMD_BIN="${SQLCMD_BIN:-sqlcmd}"
SQL_SERVER="${JORNADA_SQL_SERVER:-${SQLCMDSERVER:-localhost}}"
SQL_USER="${JORNADA_SQL_USER:-${SQLCMDUSER:-}}"
SQL_PASSWORD="${JORNADA_SQL_PASSWORD:-${SQLCMDPASSWORD:-}}"
TARGET_SCHEMA="3.70"
LEDGER_MIGRATION="migrations/20260916_Schema_Migration_Ledger.sql"
FINAL_MIGRATION="migrations/20260910_Schema_Consolidation_370.sql"

[[ -f "$MANIFEST" ]] || { echo "ERRO: manifesto de migrações ausente: $MANIFEST" >&2; exit 2; }
command -v "$SQLCMD_BIN" >/dev/null 2>&1 || { echo "ERRO: sqlcmd não encontrado: $SQLCMD_BIN" >&2; exit 2; }
command -v sha256sum >/dev/null 2>&1 || { echo "ERRO: sha256sum não encontrado" >&2; exit 2; }
[[ -n "$SQL_SERVER" ]] || { echo "ERRO: servidor SQL não definido" >&2; exit 2; }
if [[ -n "$SQL_USER" || -n "$SQL_PASSWORD" ]]; then
  [[ -n "$SQL_USER" && -n "$SQL_PASSWORD" ]] || {
    echo "ERRO: usuário e senha SQL devem ser informados em conjunto" >&2
    exit 2
  }
fi

mapfile -t MIGRATIONS < <(
  sed 's/#.*$//' "$MANIFEST" \
    | sed 's/^[[:space:]]*//;s/[[:space:]]*$//' \
    | sed '/^$/d'
)

[[ ${#MIGRATIONS[@]} -gt 0 ]] || { echo "ERRO: manifesto operacional vazio" >&2; exit 3; }
[[ "${MIGRATIONS[0]}" == "$LEDGER_MIGRATION" ]] || {
  echo "ERRO: manifesto 3.70 deve iniciar em $LEDGER_MIGRATION" >&2
  exit 3
}
last_index=$((${#MIGRATIONS[@]} - 1))
[[ "${MIGRATIONS[$last_index]}" == "$FINAL_MIGRATION" ]] || {
  echo "ERRO: manifesto 3.70 deve terminar em $FINAL_MIGRATION" >&2
  exit 3
}

for entry in "${MIGRATIONS[@]}"; do
  [[ "$entry" =~ ^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*\.sql$ ]] || {
    echo "ERRO: entrada inválida no manifesto: $entry" >&2
    exit 3
  }
  IFS='/' read -r -a components <<< "$entry"
  for component in "${components[@]}"; do
    [[ "$component" != "." && "$component" != ".." ]] || {
      echo "ERRO: travessia de diretório não permitida no manifesto: $entry" >&2
      exit 3
    }
  done
  [[ -f "$ROOT/database/$entry" ]] || {
    echo "ERRO: migração declarada não existe: $entry" >&2
    exit 3
  }
done

SQLCMD_ARGS=(-S "$SQL_SERVER" -C -b -I -d "$DB")
if [[ -n "$SQL_USER" ]]; then
  SQLCMD_ARGS+=(-U "$SQL_USER")
  export SQLCMDPASSWORD="$SQL_PASSWORD"
fi
run_sql() { "$SQLCMD_BIN" "${SQLCMD_ARGS[@]}" "$@"; }

# O ledger faz parte do próprio manifesto. A primeira migração é idempotente e é
# executada uma vez como bootstrap para que os checksums possam ser consultados.
run_sql -i "$ROOT/database/$LEDGER_MIGRATION"

required=${#MIGRATIONS[@]}
applied=0
for entry in "${MIGRATIONS[@]}"; do
  file="$ROOT/database/$entry"
  checksum="$(sha256sum "$file" | awk '{print $1}')"

  existing="$(run_sql -h -1 -W -Q "SET NOCOUNT ON; SELECT sha256 FROM jornada.schema_migration WHERE migration_name=N'${entry//\'/\'\'}';" | tr -d '\r[:space:]')"
  if [[ -n "$existing" ]]; then
    [[ "$existing" == "$checksum" ]] || {
      echo "ERRO: checksum divergente para migração já aplicada: $entry" >&2
      exit 4
    }
    echo "OK já aplicada: $entry"
    applied=$((applied+1))
    continue
  fi

  echo "Aplicando: $entry"
  run_sql -i "$file"
  run_sql -Q "INSERT INTO jornada.schema_migration(migration_name,sha256) VALUES(N'${entry//\'/\'\'}','$checksum');"
  applied=$((applied+1))
done

[[ "$applied" -eq "$required" ]] || { echo "ERRO: conjunto obrigatório de migrações incompleto" >&2; exit 5; }

# Somente a consolidação final promove o marcador. O executor apenas verifica o
# resultado; assim nenhuma lista incompleta pode ser mascarada por um UPDATE externo.
schema="$(run_sql -h -1 -W -Q "SET NOCOUNT ON; SELECT CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema'));" | tr -d '\r[:space:]')"
[[ "$schema" == "$TARGET_SCHEMA" ]] || {
  echo "ERRO: manifesto aplicado, mas Jornada.SolutionSchema=$schema (esperado $TARGET_SCHEMA)" >&2
  exit 5
}

echo "Migrations OK: $applied/$required; Jornada.SolutionSchema=$schema"
