#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DRILL_ID="35500000-0000-4000-8000-00000000b001"
FIXTURE="$ROOT/tests/fixtures/bronze/restore-drill.zip"
[[ -f "$FIXTURE" ]] || { echo "Fixture Bronze não encontrado: $FIXTURE" >&2; exit 2; }
"$ROOT/scripts/local-db.sh" up
# shellcheck disable=SC1091
set -a; source "$ROOT/.env"; set +a
DB="${JORNADA_SQL_DATABASE:-JornadaLocal}"; PORT="${JORNADA_SQL_PORT:-14333}"; RESTORE_DB="JornadaRestoreDrill"
mkdir -p "$ROOT/.local/sql-backup" "$ROOT/.local/backup-drill" "$ROOT/data/bronze"
chmod 0777 "$ROOT/.local/sql-backup"
compose() { (cd "$ROOT" && docker compose --env-file "$ROOT/.env" "$@"); }
sqlcmd() { compose exec -T -e "SQLCMDPASSWORD=$JORNADA_SQL_SA_PASSWORD" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b "$@"; }
scalar() { sqlcmd -d "$1" -h -1 -W -Q "SET NOCOUNT ON; $2" | tr -d '\r' | sed '/^[[:space:]]*$/d' | tail -1; }
SHA="$(sha256sum "$FIXTURE" | awk '{print $1}')"; LENGTH="$(wc -c < "$FIXTURE" | tr -d ' ')"
KEY="sha256/${SHA:0:2}/${SHA:2:2}/${SHA}.zip"; DEST="$ROOT/data/bronze/$KEY"; mkdir -p "$(dirname "$DEST")"; cp "$FIXTURE" "$DEST"
sqlcmd -d "$DB" -v DRILL_SHA="$SHA" DRILL_LENGTH="$LENGTH" -i /workspace/database/Jornada_Dev_BackupDrill.sql

cd "$ROOT"
if [[ "${JORNADA_LOCKED_RESTORE:-false}" == "true" ]]; then
  dotnet restore Jornada.sln --locked-mode
else
  dotnet restore Jornada.sln
fi
dotnet build src/Jornada.Bronze.Verify/Jornada.Bronze.Verify.csproj --configuration Release --no-restore
export ConnectionStrings__Jornada="Server=localhost,$PORT;Database=$DB;User Id=sa;Password=$JORNADA_SQL_SA_PASSWORD;TrustServerCertificate=true;Encrypt=false"
export BronzeStorage__RootPath="$(cd "$ROOT/data/bronze" && pwd)"
dotnet run --project src/Jornada.Bronze.Verify --configuration Release --no-build -- --entrega-id "$DRILL_ID" --minimum-count 1 | tee "$ROOT/.local/backup-drill/source-verify.txt"

BACKUP_FILE="${DB}_v370.bak"; rm -f "$ROOT/.local/sql-backup/$BACKUP_FILE"
sqlcmd -Q "BACKUP DATABASE [$DB] TO DISK=N'/var/opt/mssql/backup/$BACKUP_FILE' WITH INIT,CHECKSUM,STATS=10; RESTORE VERIFYONLY FROM DISK=N'/var/opt/mssql/backup/$BACKUP_FILE' WITH CHECKSUM;"
DATA_LOGICAL="$(scalar "$DB" "SELECT TOP(1) name FROM sys.database_files WHERE type_desc='ROWS' ORDER BY file_id;")"
LOG_LOGICAL="$(scalar "$DB" "SELECT TOP(1) name FROM sys.database_files WHERE type_desc='LOG' ORDER BY file_id;")"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"; EVID="$ROOT/.local/backup-drill/$STAMP"; mkdir -p "$EVID"
tar -C "$ROOT/data" -czf "$EVID/bronze.tar.gz" bronze
# Copia os bytes via stdout como root no container; o .bak original mantém suas permissões.
compose exec -T -u 0 sqlserver cat "/var/opt/mssql/backup/$BACKUP_FILE" > "$EVID/$BACKUP_FILE"
RESTORED_BRONZE="$EVID/restored-bronze"; mkdir -p "$RESTORED_BRONZE"; tar -C "$EVID" -xzf "$EVID/bronze.tar.gz"; mv "$EVID/bronze"/* "$RESTORED_BRONZE"/ 2>/dev/null || true; rmdir "$EVID/bronze" 2>/dev/null || true

sqlcmd -Q "IF DB_ID(N'$RESTORE_DB') IS NOT NULL BEGIN ALTER DATABASE [$RESTORE_DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$RESTORE_DB]; END; RESTORE DATABASE [$RESTORE_DB] FROM DISK=N'/var/opt/mssql/backup/$BACKUP_FILE' WITH MOVE N'$DATA_LOGICAL' TO N'/var/opt/mssql/data/${RESTORE_DB}.mdf', MOVE N'$LOG_LOGICAL' TO N'/var/opt/mssql/data/${RESTORE_DB}_log.ldf', REPLACE, RECOVERY, CHECKSUM;"
export ConnectionStrings__Jornada="Server=localhost,$PORT;Database=$RESTORE_DB;User Id=sa;Password=$JORNADA_SQL_SA_PASSWORD;TrustServerCertificate=true;Encrypt=false"
export BronzeStorage__RootPath="$(cd "$RESTORED_BRONZE" && pwd)"
dotnet run --project src/Jornada.Bronze.Verify --configuration Release --no-build -- --entrega-id "$DRILL_ID" --minimum-count 1 | tee "$EVID/restored-verify.txt"

RESTORED_OBJECT="$RESTORED_BRONZE/$KEY"
[[ -f "$RESTORED_OBJECT" ]] || { echo "ERRO: objeto restaurado esperado ausente: $RESTORED_OBJECT" >&2; exit 5; }
ORIGINAL_COPY="$EVID/original-object.zip"; cp "$RESTORED_OBJECT" "$ORIGINAL_COPY"

# Caso negativo 1: referência DISPONIVEL cujo objeto físico desapareceu deve falhar fechado.
mv "$RESTORED_OBJECT" "$RESTORED_OBJECT.missing"
set +e
dotnet run --project src/Jornada.Bronze.Verify --configuration Release --no-build -- --entrega-id "$DRILL_ID" --minimum-count 1 >"$EVID/missing-object.txt" 2>&1
MISSING_RC=$?
set -e
mv "$RESTORED_OBJECT.missing" "$RESTORED_OBJECT"
if [[ "$MISSING_RC" -eq 0 ]] || ! grep -q "MISSING" "$EVID/missing-object.txt"; then
  echo "ERRO: Bronze.Verify não detectou objeto ausente após restore." >&2; cat "$EVID/missing-object.txt" >&2; exit 6
fi

# Caso negativo 2: objeto existente mas adulterado deve falhar por integridade.
printf '\nJORNADA-RESTORE-DRILL-CORRUPTION\n' >> "$RESTORED_OBJECT"
set +e
dotnet run --project src/Jornada.Bronze.Verify --configuration Release --no-build -- --entrega-id "$DRILL_ID" --minimum-count 1 >"$EVID/corrupt-object.txt" 2>&1
CORRUPT_RC=$?
set -e
cp "$ORIGINAL_COPY" "$RESTORED_OBJECT"
if [[ "$CORRUPT_RC" -eq 0 ]] || ! grep -q "DIVERGENT" "$EVID/corrupt-object.txt"; then
  echo "ERRO: Bronze.Verify não detectou objeto corrompido após restore." >&2; cat "$EVID/corrupt-object.txt" >&2; exit 7
fi

# Confirma que a massa íntegra volta a passar depois dos faults controlados.
dotnet run --project src/Jornada.Bronze.Verify --configuration Release --no-build -- --entrega-id "$DRILL_ID" --minimum-count 1 | tee "$EVID/final-verify.txt"

# Verificação profunda + plano de GC exclusivamente DRY-RUN. Cria um órfão físico antigo controlado
# para provar que o inventário diferencia referência válida de candidato de expurgo sem apagar nada.
ORPHAN_TMP="$EVID/orphan-fixture.zip"; cp "$FIXTURE" "$ORPHAN_TMP"; printf '\nJORNADA-ORPHAN-DRY-RUN\n' >> "$ORPHAN_TMP"
ORPHAN_SHA="$(sha256sum "$ORPHAN_TMP" | awk '{print $1}')"; ORPHAN_KEY="sha256/${ORPHAN_SHA:0:2}/${ORPHAN_SHA:2:2}/${ORPHAN_SHA}.zip"
mkdir -p "$RESTORED_BRONZE/$(dirname "$ORPHAN_KEY")"; cp "$ORPHAN_TMP" "$RESTORED_BRONZE/$ORPHAN_KEY"; touch -d '48 hours ago' "$RESTORED_BRONZE/$ORPHAN_KEY"
dotnet run --project src/Jornada.Bronze.Verify --configuration Release --no-build -- --minimum-count 1 --deep --report "$EVID/deep-report.json" --gc-plan "$EVID/gc-plan.json" --orphan-grace-hours 24 | tee "$EVID/deep-verify.txt"
python3 "$ROOT/scripts/bronze-deep-evidence-gate.py" "$EVID/deep-report.json" "$EVID/gc-plan.json" --minimum-gc-candidates 1 --summary "$EVID/deep-summary.json"

sqlcmd -Q "ALTER DATABASE [$RESTORE_DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$RESTORE_DB];"
cat > "$EVID/report.json" <<JSON
{
  "status": "PASS",
  "sourceDatabase": "$DB",
  "restoredDatabase": "$RESTORE_DB",
  "drillEntregaId": "$DRILL_ID",
  "bronzeSha256": "$SHA",
  "bronzeLength": $LENGTH,
  "sqlBackup": "$BACKUP_FILE",
  "sourceVerifyPassed": true,
  "restoredVerifyPassed": true,
  "missingObjectDetected": true,
  "corruptObjectDetected": true,
  "finalVerifyPassed": true,
  "deepVerifyPassed": true,
  "gcDryRunPlanPassed": true,
  "generatedAtUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
}
JSON
python3 "$ROOT/scripts/bronze-restore-evidence-gate.py" "$EVID/report.json"
echo "Backup/restore drill concluído com faults negativos: $EVID/report.json"
