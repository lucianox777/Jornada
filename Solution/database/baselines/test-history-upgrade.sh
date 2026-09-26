#!/usr/bin/env bash
# DT-06 isolated SQL Server test. NEVER drop, reset, or touch an existing DB.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DB="${JORNADA_DT06_TEST_DATABASE:?Defina JORNADA_DT06_TEST_DATABASE=JornadaDT06_<sufixo>}"
[[ "${JORNADA_DT06_CONFIRM_CREATE:-}" == "YES" ]] || { echo "DT06: exigido JORNADA_DT06_CONFIRM_CREATE=YES" >&2; exit 2; }
[[ "$DB" =~ ^JornadaDT06_[A-Za-z0-9_]{1,40}$ ]] || { echo "DT06: prefixo reservado JornadaDT06_ obrigatório" >&2; exit 2; }
SERVER="${JORNADA_SQL_SERVER:-${SQLCMDSERVER:-localhost}}"
BIN="${SQLCMD_BIN:-sqlcmd}"
USER_NAME="${JORNADA_SQL_USER:-${SQLCMDUSER:-}}"
PASSWORD="${JORNADA_SQL_PASSWORD:-${SQLCMDPASSWORD:-}}"
command -v "$BIN" >/dev/null || exit 2
command -v sha256sum >/dev/null || exit 2
if [[ -n "$USER_NAME" || -n "$PASSWORD" ]]; then
 [[ -n "$USER_NAME" && -n "$PASSWORD" ]] || exit 2
 export SQLCMDPASSWORD="$PASSWORD"
fi
args=(-S "$SERVER" -C -b -I)
[[ -z "$USER_NAME" ]] || args+=(-U "$USER_NAME")
sql() { "$BIN" "${args[@]}" "$@"; }
scalar() { sql -d "$DB" -W -h -1 -Q "SET NOCOUNT ON; $1" | tr -d '\r[:space:]'; }
existing="$(sql -d master -W -h -1 -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name=N'$DB';" | tr -d '\r[:space:]')"
[[ "$existing" == "0" ]] || { echo "DT06: banco de teste já existe: nenhuma operação" >&2; exit 3; }
mapfile -t entries < <(sed -e 's/[[:space:]]*#.*$//' -e '/^[[:space:]]*$/d' "$ROOT/database/migrations/manifest.txt" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')
[[ "${entries[0]}" == "migrations/20260916_Schema_Migration_Ledger.sql" && "${entries[1]}" == "Jornada_Identidade_Progressiva.sql" && "${entries[2]}" == "migrations/20260907_Cpf_Ancora.sql" ]] || { echo "DT06: manifesto mudou, revalidar fixture" >&2; exit 3; }
sql -d master -Q "CREATE DATABASE [$DB];"
sql -d "$DB" -i "$ROOT/database/baselines/Jornada_Fase1_v3.65.sql"
sql -d "$DB" -Q "INSERT INTO ref.gestor(codigo,nome,ativo) VALUES(N'DT06_HISTORY_SENTINEL',N'Sentinela DT06',1);"
# Simulate a real interrupted predecessor with three successfully applied,
# SHA-pinned, immutable migration history rows.
for entry in "${entries[@]:0:3}"; do
 sql -d "$DB" -i "$ROOT/database/$entry"
 checksum="$(sha256sum "$ROOT/database/$entry" | awk '{print $1}')"
 sql -d "$DB" -Q "INSERT jornada.schema_migration(migration_name,sha256) VALUES(N'$entry','$checksum');"
done
[[ "$(scalar 'SELECT COUNT(*) FROM jornada.schema_migration;')" == "3" ]] || exit 4
JORNADA_SQL_DATABASE="$DB" JORNADA_SQL_SERVER="$SERVER" SQLCMD_BIN="$BIN" bash "$ROOT/scripts/apply-migrations.sh"
total="$(scalar 'SELECT COUNT(*) FROM jornada.schema_migration;')"
[[ "$total" == "${#entries[@]}" ]] || { echo "DT06: ledger incompleto" >&2; exit 4; }
[[ "$(scalar "SELECT COUNT(*) FROM ref.gestor WHERE codigo=N'DT06_HISTORY_SENTINEL';")" == "1" ]] || exit 4
JORNADA_SQL_DATABASE="$DB" JORNADA_SQL_SERVER="$SERVER" SQLCMD_BIN="$BIN" bash "$ROOT/scripts/apply-migrations.sh"
[[ "$(scalar 'SELECT COUNT(*) FROM jornada.schema_migration;')" == "$total" ]] || exit 4
[[ "$(scalar "SELECT COUNT(*) FROM ref.gestor WHERE codigo=N'DT06_HISTORY_SENTINEL';")" == "1" ]] || exit 4
echo "DT06: upgrade SQL OK; três migrations pré-aplicadas; $total no ledger após upgrade e replay; sentinela preservada."
echo "DT06: banco de teste [$DB] preservado para auditoria, sem DROP automático."
