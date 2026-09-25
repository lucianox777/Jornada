#!/usr/bin/env bash
# Mock-only credential and fail-closed test for real DEV Bash SQL entrypoints.
# Never run the upgrade or calibration against a database or real Docker.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FIXTURE="$(mktemp -d)"
trap 'rm -rf "$FIXTURE"' EXIT
mkdir -p "$FIXTURE/scripts" "$FIXTURE/bin"

MOCK_SECRET='Synthetic_Ddl_Calibration_Mock_2026!'
export MOCK_SECRET
MOCK_LOG="$FIXTURE/calls.log"
export MOCK_LOG
export SQLCMDPASSWORD='PARENT_SCOPE_SENTINEL'
export MOCK_FAIL=none

# Load just the actual, single-line sqlcmd() function from the upgrade script.
DDL_SOURCE="$ROOT/scripts/local-ddl-upgrade.sh"
[[ "$(grep -c '^sqlcmd(){' "$DDL_SOURCE")" -eq 1 ]] || {
  echo 'Missing or duplicate DDL sqlcmd function.' >&2; exit 1;
}
source <(grep '^sqlcmd(){' "$DDL_SOURCE")
JORNADA_SQL_SA_PASSWORD="$MOCK_SECRET"
compose() {
  [[ "$SQLCMDPASSWORD" == "$MOCK_SECRET" ]] || {
    echo 'DDL SQL did not inherit SQLCMDPASSWORD.' >&2; return 51;
  }
  local previous='' found_env=0
  for arg in "$@"; do
    [[ "$arg" != *"$MOCK_SECRET"* && "$arg" != SQLCMDPASSWORD=* ]] || {
      echo 'DDL secret exposed in Docker argv.' >&2; return 52;
    }
    if [[ "$previous" == -e && "$arg" == SQLCMDPASSWORD ]]; then found_env=1; fi
    previous="$arg"
  done
  [[ "$found_env" -eq 1 ]] || { echo 'DDL must use -e SQLCMDPASSWORD.' >&2; return 53; }
  printf 'DDL_SQL\n' >> "$MOCK_LOG"
  [[ "$MOCK_FAIL" != ddl ]]
}
sqlcmd -d JornadaSyntheticDev -Q 'SELECT 1'
[[ "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || {
  echo 'DDL SQL modified the caller environment.' >&2; exit 1;
}
[[ "$(cat "$MOCK_LOG")" == DDL_SQL ]] || exit 1
MOCK_FAIL=ddl
if sqlcmd -d JornadaSyntheticDev -Q 'SELECT 1' > "$FIXTURE/ddl-failure.out" 2>&1; then
  echo 'DDL sqlcmd swallowed the simulated Docker failure.' >&2; exit 1;
fi
[[ "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || exit 1
[[ "$(wc -l < "$MOCK_LOG")" -eq 2 ]] || exit 1

# Exercise the full real calibration entrypoint in an isolated copy.
cp "$ROOT/scripts/local-synthetic-calibration.sh" "$FIXTURE/scripts/"
printf 'JORNADA_SQL_SA_PASSWORD=%s\nJORNADA_SQL_DATABASE=JornadaSyntheticDev\n' "$MOCK_SECRET" > "$FIXTURE/.env"
cat > "$FIXTURE/scripts/local-db.sh" <<'MOCK_DB'
#!/usr/bin/env bash
set -euo pipefail
[[ "$1" == up && "$2" == --no-synthetic-corpus ]] || exit 61
[[ "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || exit 62
printf 'BOOTSTRAP\n' >> "$MOCK_LOG"
MOCK_DB
cat > "$FIXTURE/bin/docker" <<'MOCK_DOCKER'
#!/usr/bin/env bash
set -euo pipefail
[[ "$1" == compose ]] || exit 63
[[ "$SQLCMDPASSWORD" == "$MOCK_SECRET" ]] || exit 64
previous='' found_env=0 phase=''
for arg in "$@"; do
  [[ "$arg" != *"$MOCK_SECRET"* && "$arg" != SQLCMDPASSWORD=* ]] || exit 65
  if [[ "$previous" == -e && "$arg" == SQLCMDPASSWORD ]]; then found_env=1; fi
  case "$arg" in
    *SyntheticCalibration_ExclusivePreflight.sql) phase=PREFLIGHT ;;
    *SyntheticCalibration_Cleanup.sql) phase=CLEANUP ;;
  esac
  previous="$arg"
done
[[ "$found_env" -eq 1 && -n "$phase" ]] || exit 66
printf '%s\n' "$phase" >> "$MOCK_LOG"
if [[ "$MOCK_FAIL" == preflight && "$phase" == PREFLIGHT ]]; then exit 29; fi
if [[ "$MOCK_FAIL" == cleanup && "$phase" == CLEANUP ]]; then exit 29; fi
MOCK_DOCKER
cat > "$FIXTURE/bin/dotnet" <<'MOCK_DOTNET'
#!/usr/bin/env bash
set -euo pipefail
[[ "$1" == run ]] || exit 67
[[ "$ConnectionStrings__Jornada" == *"$MOCK_SECRET"* ]] || exit 68
printf 'DOTNET\n' >> "$MOCK_LOG"
MOCK_DOTNET
chmod 0755 "$FIXTURE/bin/docker" "$FIXTURE/bin/dotnet" "$FIXTURE/scripts/local-db.sh"
export PATH="$FIXTURE/bin:$PATH"
export JORNADA_SYNTH_PSEUDONYMIZATION_KEY='Test_Only_Pseudonymization_Key_2026'
MOCK_FAIL=none
: > "$MOCK_LOG"
bash "$FIXTURE/scripts/local-synthetic-calibration.sh" > "$FIXTURE/success.out" 2>&1 || {
  cat "$FIXTURE/success.out" >&2; echo 'Synthetic calibration mock success failed.' >&2; exit 1;
}
printf 'BOOTSTRAP\nPREFLIGHT\nCLEANUP\nDOTNET\n' > "$FIXTURE/expected.out"
cmp -s "$FIXTURE/expected.out" "$MOCK_LOG" || {
  echo 'Synthetic preflight/cleanup/runner sequence changed.' >&2; exit 1;
}
[[ "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || exit 1

for failure in preflight cleanup; do
  : > "$MOCK_LOG"
  MOCK_FAIL="$failure"
  export MOCK_FAIL
  if bash "$FIXTURE/scripts/local-synthetic-calibration.sh" > "$FIXTURE/failure.out" 2>&1; then
    echo "Synthetic $failure failure was ignored." >&2; exit 1;
  fi
  if [[ "$failure" == preflight ]]; then
    printf 'BOOTSTRAP\nPREFLIGHT\n' > "$FIXTURE/expected.out"
  else
    printf 'BOOTSTRAP\nPREFLIGHT\nCLEANUP\n' > "$FIXTURE/expected.out"
  fi
  cmp -s "$FIXTURE/expected.out" "$MOCK_LOG" || {
    echo "Synthetic $failure did not stop before the next step." >&2; exit 1;
  }
  [[ "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || exit 1
done
if grep -Fq "$MOCK_SECRET" "$FIXTURE/ddl-failure.out" "$FIXTURE/success.out" "$FIXTURE/failure.out"; then
  echo 'A mock entrypoint printed its SQL credential.' >&2; exit 1;
fi
echo 'DDL + SYNTHETIC SQLCMD SECRET TRANSPORT MOCK: OK'
