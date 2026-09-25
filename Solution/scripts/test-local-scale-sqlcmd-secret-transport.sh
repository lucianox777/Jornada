#!/usr/bin/env bash
# Extract the real shell scale SQL routines only. Never execute local-scale.sh:
# its entrypoint first resets the selected database.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SOURCE="$ROOT/scripts/local-scale.sh"
FUNCTION_TEXT="$(sed -n '/^sqlcmd() {/,/^}/p' "$SOURCE")"
[[ "$FUNCTION_TEXT" == *"SQLCMDPASSWORD"* && "$FUNCTION_TEXT" == *"compose exec"* ]] || {
  echo 'The real scale sqlcmd function was not extracted.' >&2; exit 1;
}
# This file is repository-controlled source, not user input.
eval "$FUNCTION_TEXT"
SCALAR_TEXT="$(grep '^scalar() {' "$SOURCE")"
[[ "$SCALAR_TEXT" == *'sqlcmd'* ]] || {
  echo 'The real scale scalar function was not extracted.' >&2; exit 1;
}
eval "$SCALAR_TEXT"

JORNADA_SQL_SA_PASSWORD='Synthetic_Scale_Password_2026!Only'
SQLCMDPASSWORD='PARENT_SCOPE_SENTINEL'
MOCK_SCALE_CALLS=0
MOCK_SCALE_FAIL=0
export JORNADA_SQL_SA_PASSWORD SQLCMDPASSWORD
compose() {
  [[ "$SQLCMDPASSWORD" == "$JORNADA_SQL_SA_PASSWORD" ]] || {
    echo 'The scale compose call did not inherit the expected secret.' >&2; return 51;
  }
  local argument previous='' found_env=0 has_scalar=0
  for argument in "$@"; do
    [[ "$argument" != *"$JORNADA_SQL_SA_PASSWORD"* && "$argument" != SQLCMDPASSWORD=* ]] || {
      echo 'The SQL password was included in Docker argv.' >&2; return 52;
    }
    [[ "$previous" == -e && "$argument" == SQLCMDPASSWORD ]] && found_env=1
    [[ "$argument" == -y ]] && has_scalar=1
    previous="$argument"
  done
  [[ "$found_env" -eq 1 ]] || { echo 'Missing -e SQLCMDPASSWORD.' >&2; return 53; }
  MOCK_SCALE_CALLS=$((MOCK_SCALE_CALLS+1))
  [[ "$MOCK_SCALE_FAIL" -eq 0 ]] || return 29
  if [[ "$has_scalar" -eq 1 ]]; then printf ' 42 \n'; fi
}
sqlcmd -d JornadaScaleMock -Q 'SELECT 1'
[[ "$MOCK_SCALE_CALLS" -eq 1 && "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || exit 1
# scalar() invokes the same real sqlcmd() through a pipe. Its last-step output
# validates the successful path; its failure must propagate under pipefail.
result="$(scalar 'SELECT 1')"
[[ "$result" == 42 && "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || {
  echo 'The scalar did not return the mocked value.' >&2; exit 1;
}
MOCK_SCALE_FAIL=1
if sqlcmd -d JornadaScaleMock -Q 'SELECT 1'; then
  echo 'Scale sqlcmd swallowed a synthetic Docker failure.' >&2; exit 1;
fi
if scalar 'SELECT 1' >/dev/null; then
  echo 'Scale scalar swallowed a synthetic Docker failure.' >&2; exit 1;
fi
[[ "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || {
  echo 'The scale SQL call modified the parent environment.' >&2; exit 1;
}
echo 'SCALE BASH SQLCMD SECRET TRANSPORT MOCK: OK'
