#!/usr/bin/env bash
# One-use, masked SQL Server password: never use a public run_id-derived password.
# Called only AFTER workflow_dispatch SHA/pin gate; all SQL is disposable DEV.
set -euo pipefail
if [[ -z "${GITHUB_ENV:-}" || -z "${GITHUB_RUN_ID:-}" || -z "${GITHUB_RUN_ATTEMPT:-}" ]]; then
  echo "GitHub Actions environment is mandatory for disposable V9 SQL." >&2
  exit 1
fi
command -v openssl >/dev/null
command -v docker >/dev/null

sql_name="v9-probe-sql-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}"
# Only the proposed standard 100k technical workflow configures this budget.
# Existing physical 30k workflows remain unchanged when absent.
sql_memory_args=()
if [[ -n "${V9_SQL_MEMORY_LIMIT_MB:-}" ]]; then
  [[ "$V9_SQL_MEMORY_LIMIT_MB" =~ ^[0-9]+$ ]] &&
    (( 2048 <= V9_SQL_MEMORY_LIMIT_MB && V9_SQL_MEMORY_LIMIT_MB <= 4096 )) || {
      echo "FAIL: invalid SQL runtime memory budget" >&2; exit 1;
    }
  sql_memory_args=(--memory 5g)
fi
sql_password="V9aA1!$(openssl rand -hex 24)"
# Register masking BEFORE any Docker operation that might print expanded args.
printf '::add-mask::%s\n' "$sql_password"
{
  printf 'JORNADA_SQL_SA_PASSWORD=%s\n' "$sql_password"
  printf 'ConnectionStrings__Jornada=Server=127.0.0.1,1433;Database=JornadaHarness;User Id=sa;Password=%s;TrustServerCertificate=true;Encrypt=false\n' "$sql_password"
} >> "$GITHUB_ENV"

# No credential in process arguments: pass a private 0600 file path to
# Docker rather than --env NAME=<password>, which leaks through process argv.
# No service-container provisioning (the log precedes GitHub add-mask).
sql_env_file="$(mktemp "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/jornada-v9-sql-env.XXXXXXXX")"
chmod 600 "$sql_env_file"
trap 'rm -f -- "$sql_env_file"' EXIT
printf 'MSSQL_SA_PASSWORD=%s\n' "$sql_password" > "$sql_env_file"
if [[ -n "${V9_SQL_MEMORY_LIMIT_MB:-}" ]]; then
  printf 'MSSQL_MEMORY_LIMIT_MB=%s\n' "$V9_SQL_MEMORY_LIMIT_MB" >> "$sql_env_file"
fi
docker run --detach --rm \
  "${sql_memory_args[@]}" \
  --name "$sql_name" \
  --publish 127.0.0.1:1433:1433 \
  --env ACCEPT_EULA=Y --env MSSQL_PID=Developer \
  --env-file "$sql_env_file" \
  mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89 \
  >/dev/null

for attempt in $(seq 1 60); do
  # The health command reads the password inside the container environment;
  # no password travels in command arguments or output.
  if docker exec "$sql_name" sh -c \
      'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -I -S localhost -U sa -C -Q "SELECT 1"' \
      >/dev/null 2>&1; then
    echo "PASS: disposable SQL Server healthy; SA credential masked before startup."
    exit 0
  fi
  sleep 2
done
echo "Fail closed: disposable SQL Server did not become healthy." >&2
exit 1
