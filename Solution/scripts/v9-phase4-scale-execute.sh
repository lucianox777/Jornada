#!/usr/bin/env bash
set -euo pipefail
umask 077
root="$JORNADA_V9_E2E_PRIVATE_ROOT"
run_cli() {
  dotnet run --no-build -c Release --no-launch-profile --project src/Jornada.Linkage.Runner -- --v9-dev-full-10k "$1" >"$root/runner-$1.log" 2>&1 || {
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
  if [[ "$profile" == diagnostic1k || "$profile" == optimized1k ]]; then
    grep -Fxq V9_E2E_CENSUS_REFERENCE_EXACT_PASS "$root/runner-full.log" || {
      echo V9_E2E_CENSUS_REFERENCE_EVIDENCE_MISSING
      return 1
    }
    printf 'V9_E2E_CENSUS_REFERENCE_EXACT_PASS profile=%s\n' "$profile"
  fi
  if [[ "$profile" == diagnostic* || "$profile" == optimized* ]]; then
    full_sql_probe Finish "$profile" >"$root/sql-probe-finish.json"
    python3 scripts/v9-phase4-scale.py sql-probe --root "$root"
  fi
  python3 scripts/v9-e2e-1k-membership-oracle.py --root "$root"
  if [[ "$JORNADA_V9_PHASE4_SCALE" == diagnostic1k || "$JORNADA_V9_PHASE4_SCALE" == optimized1k ]]; then
    run_v8_operational_regression
  fi
  echo 'V9_E2E_FULL_PUBLICATION_PASS_NOT_INTEGRAL'
}

run_v8_operational_regression() {
  local model='88000000-0000-4000-8000-000000008008'
  local version='8008'
  local scenario_count runner_log receipt_log v8_backup
  v8_backup="/var/opt/mssql/data/$db-v8-operational-$JORNADA_V9_PHASE4_SCALE.bak"
  docker exec "$sql_name" test ! -e "$v8_backup"
  pair_sql master "BACKUP DATABASE [$db] TO DISK=N'$v8_backup' WITH COPY_ONLY, CHECKSUM; RESTORE VERIFYONLY FROM DISK=N'$v8_backup' WITH CHECKSUM;"
  pair_file database/tests/20261126_V8_Operational_Regression_Fixture.sql
  if ! scenario_count="$(docker exec "$sql_name" sh -c \
    'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -I -S localhost -U sa -C -b -d "$1" -h -1 -W -Q "$2"' \
    sh "$db" "
      SET NOCOUNT ON;
      SELECT COUNT_BIG(*)
      FROM silver.pessoa_observacao po
      JOIN silver.pessoa_origem o ON o.pessoa_origem_id=po.pessoa_origem_id
      WHERE po.source_as_of>=CONVERT(datetimeoffset(7),'2099-01-01T00:00:00+00:00')
        AND o.codigo_pessoa_origem IN(
          N'V8-OP-RESOLVED',N'V8-OP-CONFLICT',N'V8-OP-BELOW',N'V8-OP-NONE');" 2>/dev/null | tr -d '\r' | xargs)"; then
    echo V8_OPERATIONAL_SCENARIO_QUERY_FAILED
    return 1
  fi
  if [[ "$scenario_count" != 4 ]]; then
    echo V8_OPERATIONAL_SCENARIO_COUNT_INVALID
    return 1
  fi

  runner_log="$root/v8-operational-runner.log"
  env -u Linkage__V9SelectionFile \
    Linkage__SelectedModelVersion="$version" \
    dotnet run --no-build -c Release --no-launch-profile \
      --project src/Jornada.Linkage.Runner -- \
      --mode ON_DEMAND --model-version "$version" \
      --since 2099-01-01T00:00:00+00:00 --max-records 4 \
      --batch-size 100 --max-parallelism 1 --publish true \
      --correlation-id 88080000-0000-4000-8000-000000008008 \
      --requested-by CI_V8_OPERATIONAL_REGRESSION \
      --reason governed-v8-operational-publication-regression \
      >"$runner_log" 2>&1 || {
        python3 scripts/v9-phase4-scale.py v8-runner-failure --root "$root" || \
          echo V8_OPERATIONAL_RUNNER_ERROR=UNCLASSIFIED
        echo V8_OPERATIONAL_RUNNER_FAILED
        return 1
      }
  if ! grep -Eq "^RUNNER_MODEL_PINNED modelId=$model version=$version contractSha256=[0-9a-f]{64}$" "$runner_log" 2>/dev/null; then
    echo V8_OPERATIONAL_PINNED_MARKER_INVALID
    return 1
  fi

  receipt_log="$root/v8-operational-receipt.log"
  docker exec "$sql_name" sh -c \
    'cd /work/Solution && SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -I -S localhost -U sa -C -b -d "$1" -w 65535 -y 0 -i "$2"' \
    sh "$db" database/tests/20261126_V8_Operational_Regression_Receipt.sql \
    >"$receipt_log" 2>&1 || {
      python3 scripts/v9-phase4-scale.py v8-receipt-sql-failure --root "$root" || \
        echo V8_OPERATIONAL_RECEIPT_SQL_ERROR=UNCLASSIFIED
      echo V8_OPERATIONAL_RECEIPT_SQL_FAILED
      return 1
    }
  if ! python3 - "$receipt_log" "$root/v8-operational-private.json" <<'PY'
import json, pathlib, sys
try:
    source, target = map(pathlib.Path, sys.argv[1:])
    if (not source.is_file() or source.is_symlink()
            or source.stat().st_size > 1024*1024 or target.is_symlink()):
        raise ValueError
    prefix = "V8_OPERATIONAL_PRIVATE_JSON="
    lines = [line.strip() for line in source.read_text(encoding="utf-8").splitlines()]
    matches = [line[len(prefix):] for line in lines if line.startswith(prefix)]
    if len(matches) != 1:
        raise ValueError
    receipt = json.loads(matches[0])
    if (not isinstance(receipt,dict)
            or receipt.get("schema") != "V8_OPERATIONAL_REGRESSION_PRIVATE_V2"):
        raise ValueError
    target.write_text(json.dumps(receipt, sort_keys=True, separators=(",", ":")), encoding="utf-8")
except (OSError,UnicodeError,ValueError,TypeError):
    raise SystemExit(1) from None
PY
  then
    echo V8_OPERATIONAL_RECEIPT_PARSE_FAILED
    return 1
  fi
  pair_sql master "
    ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    RESTORE DATABASE [$db] FROM DISK=N'$v8_backup' WITH REPLACE, CHECKSUM;
    ALTER DATABASE [$db] SET MULTI_USER;"
  docker exec "$sql_name" rm -f -- "$v8_backup"
  echo V8_OPERATIONAL_REAL_RUNASYNC_PUBLICATION_CAPTURED
}

full_sql_probe() {
  local operation="$1" profile="$2"
  case "$operation:$profile" in
    Start:diagnostic1k|Start:optimized1k|Start:optimized2k|Start:optimized4k|Start:optimized10k|Finish:diagnostic1k|Finish:optimized1k|Finish:optimized2k|Finish:optimized4k|Finish:optimized10k) ;;
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
    cp -- "$root/census.sql" "$root/scale-census.sql" "$root/http-lineage.json" \
      "$root/wave-preflight-receipt.json" "$root/ingest-elapsed.txt" "$root/pair/"
    cp -a -- "$root/fixture" "$root/pair/"
    root="$root/pair"
    export JORNADA_V9_E2E_PRIVATE_ROOT="$root" RUNNER_TEMP="$root/diagnostics"
    export JORNADA_V9_PHASE4_SCALE="$paired_profile"
    run_full
    python3 scripts/v9-phase4-scale.py diagnostic --root "$root"
  )
}

stop_apps() {
  kill "$worker" "$api" "$resultado" 2>/dev/null || true
  wait "$worker" "$api" "$resultado" 2>/dev/null || true
}

start_apps() {
  contracts="$PWD/config/contracts/gestores/SEHAB"
  support="$PWD/tests/fixtures/external-contracts/gestores/SEHAB"
  if [[ -e "$contracts" ]]; then diff -qr "$support" "$contracts" >/dev/null; else cp -R "$support" "$contracts"; fi
  export ASPNETCORE_URLS=http://127.0.0.1:5088
  export BronzeStorage__Provider=FileSystem
  export BronzeStorage__RootPath="${1:-$root}/bronze"
  export IngestionStaging__RootPath="${1:-$root}/staging"
  export Processor__PollingMilliseconds=100
  export SemiblindIdentitySearch__Enabled=false
  dotnet run --no-build -c Release --no-launch-profile --project src/Jornada.Api >"$root/api.log" 2>&1 & api=$!
  dotnet run --no-build -c Release --no-launch-profile --project src/Jornada.Processor.Worker >"$root/processor.log" 2>&1 & worker=$!
  ASPNETCORE_URLS=http://127.0.0.1:5089 JornadaApiBaseUrl=http://127.0.0.1:5088 dotnet run --no-build -c Release --no-launch-profile --project src/Jornada.Resultado.Api >"$root/resultado.log" 2>&1 & resultado=$!
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
}

if [[ "${BASH_SOURCE[0]}" != "$0" ]]; then return 0; fi

run_cli catalog
echo 'V9_E2E_CATALOG_EXECUTED'
start_apps
/usr/bin/time -f '%e' -o "$root/ingest-elapsed.txt" \
  python3 scripts/v9-phase4-scale.py ingest --root "$root/fixture"
cp "$root/fixture/http-lineage.json" "$root/http-lineage.json"
export JORNADA_V9_PHASE4_MATRIX=true
bind_disposable_sql
if [[ "$JORNADA_V9_PHASE4_SCALE" == diagnostic1k || "$JORNADA_V9_PHASE4_SCALE" == optimized1k ]]; then
  stop_apps
  trap - EXIT
  prepare_pair
fi
run_full
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
