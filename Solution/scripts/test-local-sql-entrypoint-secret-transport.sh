#!/usr/bin/env bash
# Exercise the real DEV SQL entrypoints (smoke may run DDL) against a fake Docker and isolated .env.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FIXTURE="$(mktemp -d)"
trap 'rm -rf "$FIXTURE"' EXIT
mkdir -p "$FIXTURE/scripts" "$FIXTURE/bin"
cp "$ROOT/scripts/local-sql-runtime-smoke.sh" "$ROOT/scripts/local-synthetic-diagnostics.sh" "$FIXTURE/scripts/"
MOCK_PASSWORD='Synthetic_SQL_Readonly_Mock_2026!'
export MOCK_PASSWORD
printf 'JORNADA_SQL_SA_PASSWORD=%s\nJORNADA_SQL_DATABASE=JornadaSyntheticDev\n' "$MOCK_PASSWORD" > "$FIXTURE/.env"
MOCK_LOG="$FIXTURE/invocations.log"
export MOCK_LOG
cat > "$FIXTURE/bin/docker" <<'MOCK_DOCKER'
#!/usr/bin/env bash
set -euo pipefail
[[ "$1" == compose ]] || exit 51
while [[ "$#" -gt 0 && "$1" != exec ]]; do shift; done
[[ "$#" -gt 0 ]] || exit 52
shift
[[ "$SQLCMDPASSWORD" == "$MOCK_PASSWORD" ]] || exit 53
previous='' found_env=0
for arg in "$@"; do
  [[ "$arg" != *"$MOCK_PASSWORD"* && "$arg" != SQLCMDPASSWORD=* ]] || exit 54
  if [[ "$previous" == -e && "$arg" == SQLCMDPASSWORD ]]; then found_env=1; fi
  previous="$arg"
done
[[ "$found_env" -eq 1 ]] || exit 55
printf 'exec\n' >> "$MOCK_LOG"
[[ "$MOCK_FAIL" == 0 ]] || exit 29
MOCK_DOCKER
chmod 0755 "$FIXTURE/bin/docker"
export PATH="$FIXTURE/bin:$PATH" SQLCMDPASSWORD=PARENT_SCOPE_SENTINEL MOCK_FAIL=0
export JORNADA_LOCAL_ENV_FILE="$FIXTURE/.env"
bash "$FIXTURE/scripts/local-sql-runtime-smoke.sh" > "$FIXTURE/smoke.out" 2>&1
[[ "$(wc -l < "$MOCK_LOG")" -eq 3 ]] || { echo 'Smoke must issue exactly 3 SQL commands.' >&2; exit 1; }
: > "$MOCK_LOG"
bash "$FIXTURE/scripts/local-synthetic-diagnostics.sh" JornadaSyntheticDev > "$FIXTURE/diag.out" 2>&1
[[ "$(wc -l < "$MOCK_LOG")" -eq 1 ]] || { echo 'Diagnostics must issue one SQL command.' >&2; exit 1; }
[[ "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || exit 1
: > "$MOCK_LOG"
export MOCK_FAIL=1
for name in local-sql-runtime-smoke.sh local-synthetic-diagnostics.sh; do
  if bash "$FIXTURE/scripts/$name" > "$FIXTURE/failure.out" 2>&1; then
    echo "$name ignored the synthetic SQL failure." >&2
    exit 1
  fi
  [[ "$(wc -l < "$MOCK_LOG")" -eq 1 ]] || { echo "$name did not fail at the first SQL command." >&2; exit 1; }
  : > "$MOCK_LOG"
done
if grep -Fq "$MOCK_PASSWORD" "$FIXTURE/smoke.out" "$FIXTURE/diag.out" "$FIXTURE/failure.out"; then
  echo 'Readonly wrapper output contained the credential.' >&2
  exit 1
fi
[[ "$SQLCMDPASSWORD" == PARENT_SCOPE_SENTINEL ]] || exit 1
echo 'LOCAL BASH SQL ENTRYPOINT SECRET TRANSPORT MOCK: OK'
