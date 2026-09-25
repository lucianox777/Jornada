#!/usr/bin/env bash
# Exercise only the real SQL helpers. Never source the validation entrypoint.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SOURCE="$ROOT/scripts/local-linkage-validation.sh"
bash -n "$SOURCE"
for name in sqlcmd sql_lines scalar; do
  code="$(sed -n "/^$name() {/,/^}/p" "$SOURCE")"
  [[ "$code" == *"$name()"* ]] || { echo "Missing $name function." >&2; exit 1; }
  eval "$code"
done
[[ "$(grep -c '^sqlcmd -i "\$FIXTURE"' "$SOURCE")" -eq 1 ]] || exit 1
[[ "$(grep -c '^sqlcmd -v PAGE_SIZE=1000 -i ' "$SOURCE")" -eq 1 ]] || exit 1
SQL_PASSWORD='Synthetic_Validation_Bash_Secret_2026!'
SQLCMDPASSWORD='PARENT_SCOPE_SENTINEL'
ENV_FILE='not-read.env'
DB='JornadaMock'
FIXTURE='/workspace/mock-only.sql'
MOCK_FAIL=none
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
MOCK_LOG="$TMP/docker.log"
export SQL_PASSWORD SQLCMDPASSWORD
docker(){
  [[ "$1" == compose && "$SQLCMDPASSWORD" == "$SQL_PASSWORD" ]] || return 51
  local arg previous='' found=0
  for arg in "$@"; do
    [[ "$arg" != *"$SQL_PASSWORD"* && "$arg" != SQLCMDPASSWORD=* ]] || return 52
    if [[ "$previous" == -e && "$arg" == SQLCMDPASSWORD ]]; then found=1; fi
    previous="$arg"
  done
  [[ "$found" -eq 1 ]] || return 53
  if [[ " $* " == *' /workspace/scripts/local-progressive-identity-backfill.sql '* ]]; then
    [[ " $* " == *' -v PAGE_SIZE=1000 '* ]] || return 54
  fi
  printf 'SQL\n' >> "$MOCK_LOG"
  [[ "$MOCK_FAIL" == none ]] || return 29
  printf ' 17 \n(1 row affected)\n 18 \n'
}
sqlcmd -i "$FIXTURE" > "$TMP/fixture.out"
sqlcmd -v PAGE_SIZE=1000 -i /workspace/scripts/local-progressive-identity-backfill.sql > "$TMP/backfill.out"
result="$(sql_lines 'SELECT 17' 2>"$TMP/lines.err")"
[[ "$result" == *17* && "$result" == *18* && "$result" != *'rows affected'* ]] || exit 1
[[ "$(scalar 'SELECT 18' 2>"$TMP/scalar.err")" == 18 ]] || exit 1
[[ "$(wc -l < "$MOCK_LOG")" -eq 4 && "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || exit 1
MOCK_FAIL=sql
if sqlcmd -i "$FIXTURE" >/dev/null; then echo 'SQL file swallowed error.' >&2; exit 1; fi
if sql_lines 'SELECT 17' >"$TMP/fail.out" 2>"$TMP/fail.err"; then
  echo 'SQL lines swallowed error.' >&2; exit 1
fi
if scalar 'SELECT 18' >"$TMP/scalar-fail.out" 2>"$TMP/scalar-fail.err"; then
  echo 'SQL scalar swallowed error.' >&2; exit 1
fi
[[ "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || exit 1
if grep -Fq "$SQL_PASSWORD" "$TMP/lines.err" "$TMP/scalar.err" "$TMP/fail.err" "$TMP/scalar-fail.err"; then
  echo 'SQL secret leaked through logs.' >&2; exit 1
fi
echo 'LINKAGE VALIDATION BASH SQLCMD SECRET TRANSPORT MOCK: OK'
