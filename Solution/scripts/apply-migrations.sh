#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MANIFEST="${JORNADA_MIGRATION_MANIFEST:-$ROOT/database/migrations/manifest.txt}"
DB="${JORNADA_SQL_DATABASE:-JornadaLocal}"
SQLCMD_BIN="${SQLCMD_BIN:-sqlcmd}"
TARGET_SCHEMA="3.69"

[[ -f "$MANIFEST" ]] || { echo "ERRO: manifesto de migrações ausente: $MANIFEST" >&2; exit 2; }
command -v sha256sum >/dev/null 2>&1 || { echo "ERRO: sha256sum não encontrado" >&2; exit 2; }

run_sql() { "$SQLCMD_BIN" -C -b -I -d "$DB" "$@"; }

run_sql -Q "IF SCHEMA_ID(N'jornada') IS NULL EXEC(N'CREATE SCHEMA jornada'); IF OBJECT_ID(N'jornada.schema_migration',N'U') IS NULL CREATE TABLE jornada.schema_migration(migration_name nvarchar(260) NOT NULL PRIMARY KEY, sha256 char(64) NOT NULL, applied_at datetime2(3) NOT NULL CONSTRAINT DF_jornada_schema_migration_applied_at DEFAULT SYSUTCDATETIME());"

required=0
applied=0
while IFS= read -r entry || [[ -n "$entry" ]]; do
  entry="${entry%%#*}"
  entry="$(printf '%s' "$entry" | xargs)"
  [[ -z "$entry" ]] && continue
  [[ "$entry" != */* && "$entry" == *.sql ]] || { echo "ERRO: entrada inválida no manifesto: $entry" >&2; exit 3; }
  file="$ROOT/database/migrations/$entry"
  [[ -f "$file" ]] || { echo "ERRO: migração declarada não existe: $entry" >&2; exit 3; }
  checksum="$(sha256sum "$file" | awk '{print $1}')"
  required=$((required+1))

  existing="$(run_sql -h -1 -W -Q "SET NOCOUNT ON; SELECT sha256 FROM jornada.schema_migration WHERE migration_name=N'${entry//\'/\'\'}';" | tr -d '\r[:space:]')"
  if [[ -n "$existing" ]]; then
    [[ "$existing" == "$checksum" ]] || { echo "ERRO: checksum divergente para migração já aplicada: $entry" >&2; exit 4; }
    echo "OK já aplicada: $entry"
    applied=$((applied+1))
    continue
  fi

  echo "Aplicando: $entry"
  run_sql -i "$file"
  run_sql -Q "INSERT INTO jornada.schema_migration(migration_name,sha256) VALUES(N'${entry//\'/\'\'}','$checksum');"
  applied=$((applied+1))
done < "$MANIFEST"

[[ "$required" -gt 0 && "$applied" -eq "$required" ]] || { echo "ERRO: conjunto obrigatório de migrações incompleto" >&2; exit 5; }

run_sql -Q "IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema') EXEC sys.sp_updateextendedproperty @name=N'Jornada.SolutionSchema',@value=N'$TARGET_SCHEMA'; ELSE EXEC sys.sp_addextendedproperty @name=N'Jornada.SolutionSchema',@value=N'$TARGET_SCHEMA';"

echo "Migrations OK: $applied/$required; Jornada.SolutionSchema=$TARGET_SCHEMA"
