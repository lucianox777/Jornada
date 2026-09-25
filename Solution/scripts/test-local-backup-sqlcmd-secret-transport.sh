#!/usr/bin/env bash
# Only source the actual backup sqlcmd()/scalar() helpers; never run the
# backup/restore entrypoint (which changes the selected SQL/Bronze volumes).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SOURCE="$ROOT/scripts/local-backup-restore-drill.sh"
SQLCMD_FUNCTION="$(grep '^sqlcmd() {' "$SOURCE")"
SCALAR_FUNCTION="$(grep '^scalar() {' "$SOURCE")"
[[ "$SQLCMD_FUNCTION" == *'SQLCMDPASSWORD'* && "$SCALAR_FUNCTION" == *sqlcmd* ]] || {
  echo 'Backup/restore SQL helpers were not found.' >&2; exit 1;
}
# Only trusted, tracked function definitions are evaluated, never the entrypoint.
eval "$SQLCMD_FUNCTION"
eval "$SCALAR_FUNCTION"
JORNADA_SQL_SA_PASSWORD='Synthetic_Backup_Password_2026!Only'
SQLCMDPASSWORD='PARENT_SCOPE_SENTINEL'
MOCK_FAILURE=0
export JORNADA_SQL_SA_PASSWORD SQLCMDPASSWORD
compose() {
  [[ "$SQLCMDPASSWORD" == "$JORNADA_SQL_SA_PASSWORD" ]] || {
    echo 'Backup Docker did not inherit the intended SQL secret.' >&2; return 51;
  }
  local arg previous='' found_env=0 scalar=0
  for arg in "$@"; do
    [[ "$arg" != *"$JORNADA_SQL_SA_PASSWORD"* && "$arg" != SQLCMDPASSWORD=* ]] || {
      echo 'Backup SQL password found in Docker argv.' >&2; return 52;
    }
    [[ "$previous" == -e && "$arg" == SQLCMDPASSWORD ]] && found_env=1
    [[ "$arg" == -W ]] && scalar=1
    previous="$arg"
  done
  [[ "$found_env" -eq 1 ]] || { echo 'Missing -e SQLCMDPASSWORD.' >&2; return 53; }
  [[ "$MOCK_FAILURE" -eq 0 ]] || return 29
  if [[ "$scalar" -eq 1 ]]; then printf '77\n'; fi
}
sqlcmd -d JornadaRestoreMock -Q 'SELECT 1'
[[ "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || {
  echo 'SQLCMDPASSWORD leaked into the caller after backup SQL.' >&2; exit 1;
}
value="$(scalar JornadaRestoreMock 'SELECT 1')"
[[ "$value" == 77 ]] || { echo 'Backup scalar result was lost.' >&2; exit 1; }
MOCK_FAILURE=1
if sqlcmd -d JornadaRestoreMock -Q 'SELECT 1' >/dev/null; then
  echo 'Backup sqlcmd ignored a simulated SQL failure.' >&2; exit 1;
fi
if scalar JornadaRestoreMock 'SELECT 1' >/dev/null; then
  echo 'Backup scalar ignored a simulated SQL failure.' >&2; exit 1;
fi
[[ "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || {
  echo 'Backup/restore SQL changed the parent environment.' >&2; exit 1;
}
echo 'BACKUP BASH SQLCMD SECRET TRANSPORT MOCK: OK'
