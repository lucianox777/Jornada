#!/usr/bin/env bash
# Exercise only original E2E SQL helpers: never source/run local-e2e.sh,
# whose entrypoint resets the selected SQL database.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SOURCE="$ROOT/scripts/local-e2e.sh"
bash -n "$SOURCE"
sql_fn="$(sed -n '/^compose_sql(){/,/^}/p' "$SOURCE")"
scalar_fn="$(grep '^scalar(){' "$SOURCE")"
[[ "$sql_fn" == *'compose_sql(){'* && "$sql_fn" == *'docker compose'* &&
   "$scalar_fn" == *'compose_sql'* ]] || {
  echo 'Real E2E SQL helper functions were not found.' >&2; exit 1;
}
eval "$sql_fn"
eval "$scalar_fn"
JORNADA_SQL_SA_PASSWORD='Synthetic_E2E_SQL_Only_2026!'
SQLCMDPASSWORD='PARENT_SCOPE_SENTINEL'
ENV_FILE='synthetic-e2e-env-not-opened'
DB='JornadaE2EMock'
MOCK_FAIL=none
MOCK_DIR="$(mktemp -d)"
trap 'rm -rf "$MOCK_DIR"' EXIT
MOCK_CALLS="$MOCK_DIR/calls"
export JORNADA_SQL_SA_PASSWORD SQLCMDPASSWORD
docker(){
  [[ "$SQLCMDPASSWORD" == "$JORNADA_SQL_SA_PASSWORD" ]] || {
    echo 'Docker did not inherit the E2E SQL password.' >&2; return 51;
  }
  local arg previous='' found_env=0 found_db=0 found_sql=0
  for arg in "$@"; do
    [[ "$arg" != *"$JORNADA_SQL_SA_PASSWORD"* && "$arg" != SQLCMDPASSWORD=* ]] || {
      echo 'SQL password exposed in E2E Docker arguments.' >&2; return 52;
    }
    [[ "$previous" == -e && "$arg" == SQLCMDPASSWORD ]] && found_env=1
    [[ "$previous" == -d && "$arg" == "$DB" ]] && found_db=1
    [[ "$arg" == '/opt/mssql-tools18/bin/sqlcmd' ]] && found_sql=1
    previous="$arg"
  done
  [[ "$1" == compose && "$found_env" -eq 1 && "$found_db" -eq 1 &&
     "$found_sql" -eq 1 ]] || {
    echo 'E2E SQL call did not preserve Docker/sqlcmd arguments.' >&2; return 53;
  }
  printf 'SQL\n' >> "$MOCK_CALLS"
  [[ "$MOCK_FAIL" == none ]] || return 29
  printf ' 42\r\n'
}
[[ "$(compose_sql 'SELECT 42')" == ' 42' ]] || {
  echo 'E2E compose_sql changed scalar output.' >&2; exit 1;
}
[[ "$(scalar 'SELECT 42')" == 42 ]] || {
  echo 'E2E scalar changed scalar output.' >&2; exit 1;
}
[[ "$(wc -l < "$MOCK_CALLS")" -eq 2 ]] || {
  echo 'Expected two successful mocked Docker calls.' >&2; exit 1;
}
[[ "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || {
  echo 'The E2E helper mutated the caller environment.' >&2; exit 1;
}
MOCK_FAIL=sql
if compose_sql 'SELECT 42' >"$MOCK_DIR/fail.out" 2>"$MOCK_DIR/fail.err"; then
  echo 'E2E compose_sql hid the simulated SQL error.' >&2; exit 1;
fi
if scalar 'SELECT 42' >"$MOCK_DIR/scalar.out" 2>"$MOCK_DIR/scalar.err"; then
  echo 'E2E scalar hid the simulated SQL error.' >&2; exit 1;
fi
[[ "$(wc -l < "$MOCK_CALLS")" -eq 4 &&
   "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || exit 1
if grep -Fq "$JORNADA_SQL_SA_PASSWORD" "$MOCK_DIR/fail.out" "$MOCK_DIR/fail.err" \
    "$MOCK_DIR/scalar.out" "$MOCK_DIR/scalar.err"; then
  echo 'E2E SQL password exposed in logs.' >&2; exit 1;
fi
echo 'E2E BASH SQLCMD SECRET TRANSPORT MOCK: OK'
