#!/usr/bin/env bash
# Run the real Bash provisioning entrypoint against a synthetic Docker stub only.
# No real container, SQL connection, reset or clean is possible in this test.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FIXTURE="$(mktemp -d)"
trap 'rm -rf "$FIXTURE"' EXIT
mkdir -p "$FIXTURE/scripts" "$FIXTURE/bin"
cp "$ROOT/scripts/local-db.sh" "$FIXTURE/scripts/local-db.sh"
MOCK_EXPECTED_PASSWORD='Synthetic_Db_Mock_2026!Only'
export MOCK_EXPECTED_PASSWORD
printf 'JORNADA_SQL_SA_PASSWORD=%s\nJORNADA_SQL_DATABASE=JornadaSyntheticDev\n' "$MOCK_EXPECTED_PASSWORD" > "$FIXTURE/.env"
MOCK_DB_LOG="$FIXTURE/command-counts.log"
export MOCK_DB_LOG
cat > "$FIXTURE/bin/docker" <<'MOCK_DOCKER'
#!/usr/bin/env bash
set -euo pipefail
if [[ "$1" == inspect ]]; then
  [[ "$SQLCMDPASSWORD" == 'PARENT_SCOPE_SENTINEL' ]] || exit 51
  printf 'healthy\n'
  exit 0
fi
[[ "$1" == compose ]] || exit 52
while [[ "$#" -gt 0 && "$1" != exec ]]; do shift; done
if [[ "$#" -eq 0 ]]; then exit 0; fi
shift
[[ "$SQLCMDPASSWORD" == "$MOCK_EXPECTED_PASSWORD" ]] || exit 53
previous=''
found_env=0
for arg in "$@"; do
  [[ "$arg" != *"$MOCK_EXPECTED_PASSWORD"* && "$arg" != SQLCMDPASSWORD=* ]] || exit 54
  if [[ "$previous" == -e && "$arg" == SQLCMDPASSWORD ]]; then found_env=1; fi
  previous="$arg"
done
[[ "$found_env" -eq 1 ]] || exit 55
printf 'exec\n' >> "$MOCK_DB_LOG"
if [[ "$MOCK_DB_FAIL_SQL" == 1 ]]; then exit 29; fi
for arg in "$@"; do
  if [[ "$arg" == *"SELECT CASE WHEN OBJECT_ID("* ]]; then printf '0\n'; break; fi
done
MOCK_DOCKER
chmod 0755 "$FIXTURE/bin/docker"
export PATH="$FIXTURE/bin:$PATH" SQLCMDPASSWORD='PARENT_SCOPE_SENTINEL' MOCK_DB_FAIL_SQL=0
bash "$FIXTURE/scripts/local-db.sh" up --no-synthetic-corpus > "$FIXTURE/success.out" 2>&1 || {
  cat "$FIXTURE/success.out" >&2
  echo 'Synthetic safe bootstrap failed.' >&2
  exit 1
}
count="$(wc -l < "$MOCK_DB_LOG")"
[[ "$count" -ge 8 ]] || { echo "Expected repeated real sqlcmd calls, got $count." >&2; exit 1; }
[[ "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || exit 1
if grep -Fq "$MOCK_EXPECTED_PASSWORD" "$FIXTURE/success.out"; then
  echo 'Bootstrap output exposed SQL credential.' >&2
  exit 1
fi
: > "$MOCK_DB_LOG"
export MOCK_DB_FAIL_SQL=1
if bash "$FIXTURE/scripts/local-db.sh" up --no-synthetic-corpus > "$FIXTURE/failure.out" 2>&1; then
  echo 'SQL invocation failure must stop local-db.sh.' >&2
  exit 1
fi
[[ "$(wc -l < "$MOCK_DB_LOG")" -eq 1 ]] || { echo 'Synthetic SQL error was not propagated.' >&2; exit 1; }
[[ "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || exit 1
if grep -Fq "$MOCK_EXPECTED_PASSWORD" "$FIXTURE/failure.out"; then
  echo 'Failure output exposed SQL credential.' >&2
  exit 1
fi
echo 'LOCAL-DB BASH SQLCMD SECRET TRANSPORT MOCK: OK'
