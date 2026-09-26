#!/usr/bin/env bash
# DT-06: install from zero with ledger populated by existing canonical runner.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DB="${JORNADA_SQL_DATABASE:?Defina JORNADA_SQL_DATABASE para banco NOVO}"
SERVER="${JORNADA_SQL_SERVER:-${SQLCMDSERVER:-localhost}}"
BIN="${SQLCMD_BIN:-sqlcmd}"
USER_NAME="${JORNADA_SQL_USER:-${SQLCMDUSER:-}}"
PASSWORD="${JORNADA_SQL_PASSWORD:-${SQLCMDPASSWORD:-}}"
[[ "$DB" =~ ^[A-Za-z][A-Za-z0-9_]{0,120}$ ]] || { echo "DT06: nome de banco inválido" >&2; exit 2; }
case "${DB,,}" in master|model|msdb|tempdb) echo "DT06: banco de sistema proibido" >&2; exit 2;; esac
command -v "$BIN" >/dev/null || { echo "DT06: sqlcmd ausente" >&2; exit 2; }
command -v sha256sum >/dev/null || { echo "DT06: sha256sum ausente" >&2; exit 2; }
if [[ -n "$USER_NAME" || -n "$PASSWORD" ]]; then
 [[ -n "$USER_NAME" && -n "$PASSWORD" ]] || { echo "DT06: usuário/senha incompletos" >&2; exit 2; }
 export SQLCMDPASSWORD="$PASSWORD"
fi
args=(-S "$SERVER" -C -b -I)
[[ -z "$USER_NAME" ]] || args+=(-U "$USER_NAME")
sql() { "$BIN" "${args[@]}" "$@"; }
existing="$(sql -d master -W -h -1 -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name=N'$DB';" | tr -d '\r[:space:]')"
[[ "$existing" == "0" ]] || { echo "DT06: banco existente; execute APENAS apply-migrations.sh após backup" >&2; exit 3; }
sql -d master -Q "CREATE DATABASE [$DB];"
# Execute the base once, then the canonical migration runner; do not run the
# full wrapper before the ledger-aware runner.
sql -d "$DB" -i "$ROOT/database/Jornada_Fase1.sql"
JORNADA_SQL_DATABASE="$DB" JORNADA_SQL_SERVER="$SERVER" SQLCMD_BIN="$BIN" bash "$ROOT/scripts/apply-migrations.sh"
expected="$(awk 'NF && $1 !~ /^#/ {n++} END {print n+0}' "$ROOT/database/migrations/manifest.txt")"
actual="$(sql -d "$DB" -W -h -1 -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM jornada.schema_migration;" | tr -d '\r[:space:]')"
[[ "$actual" == "$expected" ]] || { echo "DT06: ledger incompleto ($actual/$expected); banco preservado" >&2; exit 4; }
echo "DT06: instalação nova OK ($actual/$expected migrations); sem seed ou reset implícito."
