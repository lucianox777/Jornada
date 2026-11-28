#!/usr/bin/env bash
set -euo pipefail
umask 077
source scripts/v9-phase4-scale-execute.sh
bind_disposable_sql
python3 scripts/v9-six-arm-comparison.py admit --root "$root"
run_cli catalog
verify_pair_database
backup="/var/opt/mssql/data/$db-six-arm-empty.bak"
docker exec "$sql_name" test ! -e "$backup"
pair_sql master "BACKUP DATABASE [$db] TO DISK=N'$backup' WITH COPY_ONLY,CHECKSUM; RESTORE VERIFYONLY FROM DISK=N'$backup' WITH CHECKSUM;"
start_apps
python3 scripts/v9-six-arm-comparison.py ingest --root "$root"
cp "$root/fixture/http-lineage.json" "$root/http-lineage.json"
stop_apps
trap - EXIT
run_full
python3 scripts/v9-six-arm-comparison.py controls --root "$root"
run_cli repeat-2
python3 scripts/v9-six-arm-comparison.py controls --root "$root"
# Restore exactly the catalog-only backup for the independent staged trajectory.
# No subsequent round is ever restored to its own pre-FULL state.
pair_sql master "ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [$db] FROM DISK=N'$backup' WITH REPLACE,CHECKSUM; ALTER DATABASE [$db] SET MULTI_USER;"
verify_pair_database
mkdir -m 700 "$root/staged-ingestion"
start_apps "$root/staged-ingestion"
python3 scripts/v9-six-arm-comparison.py ingest-staged --root "$root"
stop_apps
trap - EXIT
python3 scripts/v9-six-arm-comparison.py controls --root "$root"
run_cli six-compare
python3 scripts/v9-six-arm-comparison.py evaluate --root "$root"
