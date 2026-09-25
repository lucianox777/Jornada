#!/usr/bin/env bash
# Extract only actual SQL wrappers. Never source the flow or smoke entrypoints:
# they may calibrate, write evidence or mutate an isolated SQL fixture.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
MOCK_ROOT="$(mktemp -d)"
trap 'rm -rf "$MOCK_ROOT"' EXIT
flow="$ROOT/scripts/local-linkage-validation-flow.sh"
smoke="$ROOT/scripts/linkage-evaluation-smoke.sh"
flow_fn="$(sed -n '/^sql_scalar() {/,/^}/p' "$flow")"
smoke_fn="$(sed -n '/^sqlcmd(){/,/^}/p' "$smoke")"
[[ "$flow_fn" == *'SQLCMDPASSWORD'* && "$smoke_fn" == *'SQLCMDPASSWORD'* ]] || {
  echo 'Unable to extract real linkage SQL wrappers.' >&2; exit 1;
}
# Evaluate source-controlled function definitions only, never entrypoint logic.
eval "$flow_fn"
eval "$smoke_fn"
SQL_PASSWORD='Synthetic_Linkage_Bash_SQL_Mock_2026!'
SQLCMDPASSWORD='PARENT_SCOPE_SENTINEL'
ENV_FILE="$MOCK_ROOT/.env"
CID='mock-only-container'
DB='JornadaLinkageMock'
MOCK_FAIL=none
MOCK_LOG="$MOCK_ROOT/docker.log"
export SQLCMDPASSWORD SQL_PASSWORD
docker() {
  [[ "$SQLCMDPASSWORD" == "$SQL_PASSWORD" ]] || {
    echo 'Docker did not inherit temporary SQLCMDPASSWORD.' >&2; return 51;
  }
  local arg previous='' found_env=0
  for arg in "$@"; do
    [[ "$arg" != *"$SQL_PASSWORD"* && "$arg" != SQLCMDPASSWORD=* ]] || {
      echo 'Linkage password leaked into Docker argv.' >&2; return 52;
    }
    if [[ "$previous" == -e && "$arg" == SQLCMDPASSWORD ]]; then found_env=1; fi
    previous="$arg"
  done
  [[ "$found_env" == 1 ]] || { echo 'Missing -e SQLCMDPASSWORD.' >&2; return 53; }
  case "$1" in
    compose)
      [[ " $* " == *' sqlserver '* ]] || return 54
      printf 'FLOW\n' >> "$MOCK_LOG"
      ;;
    exec)
      [[ "$2" == -i && "$4" == "$CID" ]] || return 55
      printf 'EVALUATION\n' >> "$MOCK_LOG"
      ;;
    *) return 56 ;;
  esac
  [[ "$MOCK_FAIL" == none ]] || return 29
  printf ' 17 \n'
}
[[ "$(sql_scalar 'SELECT 17' 2>"$MOCK_ROOT/flow.err")" == 17 ]] || {
  echo 'Linkage flow scalar result changed.' >&2; exit 1;
}
[[ "$(sqlcmd -Q 'SELECT 17')" == ' 17 ' ]] || {
  echo 'Evaluation sqlcmd result changed.' >&2; exit 1;
}
printf 'FLOW\nEVALUATION\n' > "$MOCK_ROOT/expected"
cmp -s "$MOCK_ROOT/expected" "$MOCK_LOG" || {
  echo 'Unexpected mock invocation sequence.' >&2; exit 1;
}
[[ "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || exit 1
MOCK_FAIL=sql
if sql_scalar 'SELECT 17' >"$MOCK_ROOT/fail.out" 2>"$MOCK_ROOT/fail.err"; then
  echo 'Linkage flow swallowed SQL failure.' >&2; exit 1;
fi
if sqlcmd -Q 'SELECT 17' >"$MOCK_ROOT/eval-fail.out" 2>&1; then
  echo 'Evaluation wrapper swallowed SQL failure.' >&2; exit 1;
fi
[[ "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || {
  echo 'Wrapper changed parent SQLCMDPASSWORD.' >&2; exit 1;
}
if grep -Fq "$SQL_PASSWORD" "$MOCK_ROOT/flow.err" "$MOCK_ROOT/fail.err" "$MOCK_ROOT/eval-fail.out"; then
  echo 'Linkage wrapper printed its SQL secret.' >&2; exit 1;
fi
echo 'LINKAGE FLOW + EVALUATION BASH SECRET TRANSPORT MOCK: OK'
