#!/usr/bin/env bash
set -euo pipefail
umask 077
root="$JORNADA_V9_E2E_PRIVATE_ROOT"
run_cli() {
  flag=--v9-dev-full-10k
  [[ "$JORNADA_V9_PHASE4_SCALE" != audit1k ]] || flag=--v9-dev-full-1k
  dotnet run --no-build -c Release --no-launch-profile --project src/Jornada.Linkage.Runner -- "$flag" "$1" >"$root/runner-$1.log" 2>&1 || {
    grep -E '^V9_DEV_E2E_FAILURE stage=[a-z-]+ type=[A-Za-z0-9_-]+ code=[A-Z0-9_]+$' "$root/runner-$1.log" || true
    echo "V9_E2E_RUNNER_STAGE_FAILED_$1"; exit 1;
  }
}
run_full() {
  local profile="$JORNADA_V9_PHASE4_SCALE"
  if [[ "$profile" == diagnostic* || "$profile" == optimized* ]]; then
    verify_pair_database
    full_sql_probe Start "$profile" >"$root/sql-probe-start.log"
  fi
  run_cli full
  if [[ "$profile" == diagnostic* || "$profile" == optimized* ]]; then
    full_sql_probe Finish "$profile" >"$root/sql-probe-finish.json"
    python3 scripts/v9-phase4-scale.py sql-probe --root "$root"
  fi
  python3 scripts/v9-e2e-1k-membership-oracle.py --root "$root"
  echo 'V9_E2E_FULL_PUBLICATION_PASS_NOT_INTEGRAL'
}

full_sql_probe() {
  local operation="$1" profile="$2"
  case "$operation:$profile" in
    Start:diagnostic1k|Start:optimized1k|Start:optimized2k|Start:optimized4k|Finish:diagnostic1k|Finish:optimized1k|Finish:optimized2k|Finish:optimized4k) ;;
    *) return 1 ;;
  esac
  docker exec "$sql_name" sh -c \
    'cd /work/Solution && SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -I -S localhost -U sa -C -b -r 1 -d master -h -1 -W -w 1024 -i "$1" -v "ProbeFile=$2"' \
    sh "database/tests/20261126_V9_Full_Sql_Probe_${operation}.sql" "v9-full-$profile" \
    2>>"$root/sql-probe-error.log" || {
    sed -nE 's/^Msg ([0-9]{1,6}), Level ([0-9]{1,2}), State ([0-9]{1,3}).*/V9_FULL_SQL_PROBE_ERROR=\1_\2_\3/p' "$root/sql-probe-error.log" >&2
    printf 'V9_FULL_SQL_PROBE_FAILED_%s\n' "$operation" >&2
    return 1
  }
}

pair_sql() {
  docker exec "$sql_name" sh -c \
    'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -I -S localhost -U sa -C -b -d "$1" -Q "$2"' \
    sh "$1" "$2" >>"$root/pair-sql.log" 2>&1 || {
    echo 'V9_SCALE_PAIR_SQL_FAILED'
    return 1
  }
}

pair_file() {
  docker exec "$sql_name" sh -c \
    'cd /work/Solution && SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -I -S localhost -U sa -C -b -d "$1" -i "$2"' \
    sh "$db" "$1" >>"$root/pair-sql.log" 2>&1 || {
    echo 'V9_SCALE_PAIR_MIGRATION_FAILED'
    return 1
  }
}

verify_pair_database() {
  pair_sql "$db" "
    IF DB_NAME() <> N'$db'
      OR CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'UserAccess')) <> N'MULTI_USER'
      OR NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND
        name = N'Jornada.V9.DevAttempt' AND CONVERT(nvarchar(40), value) = N'$attempt')
      OR NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND
        name = N'Jornada.EnvironmentProfile' AND CONVERT(nvarchar(40), value) = N'Development')
      OR EXISTS (SELECT 1 FROM identidade.linkage_run)
      THROW 52990, 'V9_SCALE_PAIR_DATABASE_REJECTED', 1;"
}

bind_disposable_sql() {
  [[ "${GITHUB_ACTIONS:-}" == true && "${GITHUB_EVENT_NAME:-}" == pull_request &&
     "${GITHUB_REPOSITORY:-}" == lucianox777/Jornada && "${JORNADA_RUNTIME_MODE:-}" == DEV &&
     "${GITHUB_RUN_ID:-}" =~ ^[0-9]+$ && "${GITHUB_RUN_ATTEMPT:-}" =~ ^[0-9]+$ ]]
  sql_name="v9-probe-sql-$GITHUB_RUN_ID-$GITHUB_RUN_ATTEMPT"
  attempt="$JORNADA_V9_DEV_ATTEMPT"
  db="$JORNADA_V9_DEV_ATTEMPT_DB"
  [[ "$attempt" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ &&
     "$db" == "JornadaV9Dev_${attempt//-/}" &&
     "$root" == "$RUNNER_TEMP/jornada-v9-e2e-$attempt" && ! -L "$root" ]]
}

prepare_pair() {
  verify_pair_database
  backup="/var/opt/mssql/data/$db-pre-full.bak"
  docker exec "$sql_name" test ! -e "$backup"
  pair_sql master "
    BACKUP DATABASE [$db] TO DISK=N'$backup' WITH COPY_ONLY, CHECKSUM;
    RESTORE VERIFYONLY FROM DISK=N'$backup' WITH CHECKSUM;"
}

repeat_full_from_pair() {
  (
    # The original HTTP lineage, physical rows, marker and DB name come back
    # together. The independent Runner writes a different run to the restored DB.
    pair_sql master "
      ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
      RESTORE DATABASE [$db] FROM DISK=N'$backup' WITH REPLACE, CHECKSUM;
      ALTER DATABASE [$db] SET MULTI_USER;" || exit $?
    verify_pair_database || exit $?
    case "$JORNADA_V9_PHASE4_SCALE" in
      diagnostic1k)
        pair_file database/migrations/20261125_Linkage_Publication_Scale.sql || exit $?
        pair_file database/migrations/20261126_Linkage_Publication_Scale_V2.sql || exit $?
        pair_file database/migrations/20261126_Linkage_Publication_Scale_V3.sql || exit $?
        paired_profile=optimized1k
        ;;
      optimized1k)
        pair_file database/rollback/20261126_Linkage_Publication_Scale_V3_Restore.sql || exit $?
        pair_file database/rollback/20261126_Linkage_Publication_Scale_V2_Restore.sql || exit $?
        pair_file database/rollback/20261125_Linkage_Publication_Scale_Restore.sql || exit $?
        paired_profile=diagnostic1k
        ;;
      *) exit 1 ;;
    esac
    mkdir -m 700 "$root/pair" "$root/pair/diagnostics"
    cp -- "$root/census.sql" "$root/http-lineage.json" "$root/wave-preflight-receipt.json" "$root/ingest-elapsed.txt" "$root/pair/"
    cp -a -- "$root/fixture" "$root/pair/"
    root="$root/pair"
    export JORNADA_V9_E2E_PRIVATE_ROOT="$root" RUNNER_TEMP="$root/diagnostics"
    export JORNADA_V9_PHASE4_SCALE="$paired_profile"
    run_full
    python3 scripts/v9-phase4-scale.py diagnostic --root "$root"
  )
}

run_cli catalog
echo 'V9_E2E_CATALOG_EXECUTED'
contracts="$PWD/config/contracts/gestores/SEHAB"
support="$PWD/tests/fixtures/external-contracts/gestores/SEHAB"
if [[ -e "$contracts" ]]; then diff -qr "$support" "$contracts" >/dev/null; else cp -R "$support" "$contracts"; fi
export ASPNETCORE_URLS=http://127.0.0.1:5088
export BronzeStorage__Provider=FileSystem
export BronzeStorage__RootPath="$root/bronze"
export IngestionStaging__RootPath="$root/staging"
export Processor__PollingMilliseconds=100
export SemiblindIdentitySearch__Enabled=false
dotnet run --no-build -c Release --no-launch-profile --project src/Jornada.Api >"$root/api.log" 2>&1 & api=$!
dotnet run --no-build -c Release --no-launch-profile --project src/Jornada.Processor.Worker >"$root/processor.log" 2>&1 & worker=$!
ASPNETCORE_URLS=http://127.0.0.1:5089 JornadaApiBaseUrl=http://127.0.0.1:5088 dotnet run --no-build -c Release --no-launch-profile --project src/Jornada.Resultado.Api >"$root/resultado.log" 2>&1 & resultado=$!
stop_apps() {
  kill "$worker" "$api" "$resultado" 2>/dev/null || true
  wait "$worker" "$api" "$resultado" 2>/dev/null || true
}
trap stop_apps EXIT
ready=0
for _ in $(seq 1 120); do
  if curl --silent --fail --output "$root/ready.json" http://127.0.0.1:5088/health/ready; then ready=1; break; fi
  sleep 1
done
[[ "$ready" == 1 ]] || { echo 'V9_E2E_API_READINESS_FAILED'; exit 1; }
ready=0
for _ in $(seq 1 60); do
  if curl --silent --fail --output "$root/resultado-ready.json" http://127.0.0.1:5089/health; then ready=1; break; fi
  sleep 1
done
[[ "$ready" == 1 ]] || { echo 'V9_E2E_RESULTADO_READINESS_FAILED'; exit 1; }
/usr/bin/time -f '%e' -o "$root/ingest-elapsed.txt"   python3 scripts/v9-phase4-scale.py ingest --root "$root/fixture"
cp "$root/fixture/http-lineage.json" "$root/http-lineage.json"
export JORNADA_V9_PHASE4_MATRIX=true
if [[ "$JORNADA_V9_PHASE4_SCALE" == diagnostic* || "$JORNADA_V9_PHASE4_SCALE" == optimized* ]]; then
  bind_disposable_sql
fi
if [[ "$JORNADA_V9_PHASE4_SCALE" == diagnostic1k || "$JORNADA_V9_PHASE4_SCALE" == optimized1k ]]; then
  stop_apps
  trap - EXIT
  prepare_pair
fi
run_full
if [[ "$JORNADA_V9_PHASE4_SCALE" == diagnostic* || "$JORNADA_V9_PHASE4_SCALE" == optimized* ]]; then
  python3 scripts/v9-phase4-scale.py diagnostic --root "$root"
  out="$RUNNER_TEMP/v9-scale-aggregate"
  mkdir -m 700 "$out"
  cp "$root/diagnostic-receipt.json" "$root/membership-receipt.json" "$root/scalar-receipt.json" "$root/actual-graph-receipt.json" "$out/"
  if [[ "$JORNADA_V9_PHASE4_SCALE" == diagnostic1k || "$JORNADA_V9_PHASE4_SCALE" == optimized1k ]]; then
    repeat_full_from_pair
    python3 scripts/v9-phase4-scale.py pair --root "$root"
    cp -- "$root/pair-receipt.json" "$out/"
    cp -- "$root/pair/diagnostic-receipt.json" "$out/restored-diagnostic-receipt.json"
  fi
  printf '%s\n' "$JORNADA_E2E_EXACT_SHA" > "$out/commit.txt"
  echo 'V9_FULL_SCALE_DIAGNOSTIC_MEASURED_NOT_MATRIX'
  exit 0
fi
python3 scripts/v9-phase4-scale.py plan --root "$root"
run_cli matrix
python3 scripts/v9-phase4-scale.py measure --root "$root"
out="$RUNNER_TEMP/v9-scale-aggregate"
mkdir -m 700 "$out"
cp "$root/selection/winner-pin-receipt.json" "$root/http-lineage.json" "$root/runner-receipt.json" "$root/identity-receipt.json" "$root/membership-receipt.json" "$root/scalar-receipt.json" "$root/actual-graph-receipt.json" "$root/s4b-receipt.json" "$root/scale-receipt.json" "$root/case-audit.jsonl" "$out/"
printf '%s\n' "$JORNADA_E2E_EXACT_SHA" > "$out/commit.txt"
echo 'V9_SCALE_MEASURED_NONPUBLISHING'
