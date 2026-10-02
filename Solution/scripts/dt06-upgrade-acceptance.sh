#!/usr/bin/env bash
# DT-06 acceptance evidence: historical clone -> upgrade -> replay -> restore.
# Opt-in only. Creates/uses only a new JornadaDT06_* database and never touches JornadaLocal.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DB="${JORNADA_DT06_TEST_DATABASE:?Defina JORNADA_DT06_TEST_DATABASE=JornadaDT06_<sufixo>}"
[[ "${JORNADA_DT06_EVIDENCE:-}" == "1" ]] || { echo "DT06: defina JORNADA_DT06_EVIDENCE=1" >&2; exit 2; }
[[ "${JORNADA_DT06_CONFIRM_CREATE:-}" == "YES" ]] || { echo "DT06: exigido JORNADA_DT06_CONFIRM_CREATE=YES" >&2; exit 2; }
[[ "$DB" =~ ^JornadaDT06_[A-Za-z0-9_]{1,40}$ ]] || { echo "DT06: prefixo reservado JornadaDT06_ obrigatório" >&2; exit 2; }
[[ "$DB" != "JornadaLocal" && "$DB" != "JornadaE2E" && "$DB" != "JornadaSyntheticDev" ]] || exit 2
BASELINE="${JORNADA_DT06_BASELINE:-database/baselines/Jornada_Fase1_v3.57.sql}"
[[ "$BASELINE" == database/baselines/* && -f "$ROOT/$BASELINE" ]] || { echo "DT06: baseline histórico inválido: $BASELINE" >&2; exit 2; }
SERVER="${JORNADA_SQL_SERVER:-${SQLCMDSERVER:-localhost}}"
BIN="${SQLCMD_BIN:-sqlcmd}"
USER_NAME="${JORNADA_SQL_USER:-${SQLCMDUSER:-}}"
PASSWORD="${JORNADA_SQL_PASSWORD:-${SQLCMDPASSWORD:-}}"
BACKUP_PATH="${JORNADA_DT06_BACKUP_PATH:-/var/opt/mssql/data/${DB}_before_upgrade.bak}"
[[ "$BACKUP_PATH" != *"'"* ]] || { echo "DT06: caminho de backup não pode conter aspas simples" >&2; exit 2; }
command -v "$BIN" >/dev/null || exit 2
command -v sha256sum >/dev/null || exit 2
if [[ -n "$USER_NAME" || -n "$PASSWORD" ]]; then
  [[ -n "$USER_NAME" && -n "$PASSWORD" ]] || exit 2
  export SQLCMDPASSWORD="$PASSWORD"
fi
args=(-S "$SERVER" -C -b -I)
[[ -z "$USER_NAME" ]] || args+=(-U "$USER_NAME")
sql(){ "$BIN" "${args[@]}" "$@"; }
scalar(){
  local query="$1"
  local value
  value="$(sql -d "$DB" -W -h -1 -Q "SET NOCOUNT ON; $query" | tr -d '\r' | sed '/^[[:space:]]*$/d' | tail -1 | xargs)"
  [[ -n "$value" ]] || { echo "DT06: consulta escalar não retornou valor: $query" >&2; return 7; }
  printf '%s\n' "$value"
}
OUT="$ROOT/.local/dt06-acceptance/$DB"
mkdir -p "$OUT"
MANIFEST="$ROOT/database/migrations/manifest.txt"
manifest_hash(){ sha256sum "$MANIFEST" | awk '{print $1}'; }
invariants(){ sql -d "$DB" -i "$ROOT/database/Jornada_Upgrade_Invariants.sql" -y 0 -w 65535 | sed -n '/^[[:space:]]*{/,$p' | tr -d '\r\n'; }
fingerprint(){ sql -d "$DB" -i "$ROOT/database/Jornada_Dev_DdlFingerprint.sql" -W -h -1 | sed '/^[[:space:]]*$/d' | sha256sum | awk '{print $1}'; }
export_history(){ sql -d "$DB" -h -1 -W -s '|' -i "$ROOT/database/tests/DT06_Exportar_Historico.sql" | sed '/^[[:space:]]*$/d'; }

existing="$(sql -d master -W -h -1 -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name=N'$DB';" | tr -d '\r[:space:]')"
[[ "$existing" == "0" ]] || { echo "DT06: banco já existe; nenhuma operação executada: $DB" >&2; exit 3; }

# 1) Clone histórico sintético isolado.
sql -d master -Q "CREATE DATABASE [$DB];"
sql -d "$DB" -i "$ROOT/$BASELINE"
sql -d "$DB" -Q "INSERT ref.gestor(codigo,nome,ativo) VALUES(N'DT06_SENTINELA',N'Gestor sintético DT06',1),(N'DT06_MASSA_A',N'Massa sintética A',1),(N'DT06_MASSA_B',N'Massa sintética B',0);"
printf '%s\n' "$(manifest_hash)" > "$OUT/manifest-before.sha256"
invariants > "$OUT/invariants-before.json"
sql -d "$DB" -W -h -1 -Q "SET NOCOUNT ON; SELECT COUNT_BIG(*) FROM ref.gestor WHERE codigo LIKE N'DT06_%';" \
  | tr -d '\r' | sed '/^[[:space:]]*$/d' | tail -1 | xargs > "$OUT/synthetic-count-before.txt"
[[ "$(cat "$OUT/synthetic-count-before.txt")" =~ ^[0-9]+$ ]] || { echo "DT06: contagem sintética inicial inválida" >&2; exit 7; }

# 2) Backup comprovadamente legível antes do upgrade.
sql -d master -Q "BACKUP DATABASE [$DB] TO DISK=N'$BACKUP_PATH' WITH INIT,CHECKSUM; RESTORE VERIFYONLY FROM DISK=N'$BACKUP_PATH' WITH CHECKSUM;"
printf '%s\n' "$BACKUP_PATH" > "$OUT/backup-path.txt"

# 3) Upgrade real via ledger de migrations, sem apagar/regravar histórico aplicado.
JORNADA_SQL_DATABASE="$DB" JORNADA_SQL_SERVER="$SERVER" SQLCMD_BIN="$BIN" bash "$ROOT/scripts/apply-migrations.sh" | tee "$OUT/upgrade-first.log"
sql -d "$DB" -i "$ROOT/database/tests/DT06_Verificar_Schema_370.sql" | tee "$OUT/verify-first.txt"
invariants > "$OUT/invariants-after-first.json"
export_history > "$OUT/history-after-first.txt"
fingerprint > "$OUT/fingerprint-after-first.sha256"
printf '%s\n' "$(manifest_hash)" > "$OUT/manifest-after-first.sha256"
sql -d "$DB" -W -h -1 -Q "SET NOCOUNT ON; SELECT COUNT_BIG(*) FROM ref.gestor WHERE codigo LIKE N'DT06_%';" \
  | tr -d '\r' | sed '/^[[:space:]]*$/d' | tail -1 | xargs > "$OUT/synthetic-count-after-upgrade.txt"
cmp -s "$OUT/synthetic-count-before.txt" "$OUT/synthetic-count-after-upgrade.txt" || { echo "DT06: massa sintética não foi preservada no upgrade" >&2; exit 4; }

# 4) Idempotência: segunda aplicação não pode mudar ledger, fingerprint, manifesto ou invariantes.
JORNADA_SQL_DATABASE="$DB" JORNADA_SQL_SERVER="$SERVER" SQLCMD_BIN="$BIN" bash "$ROOT/scripts/apply-migrations.sh" | tee "$OUT/upgrade-second.log"
sql -d "$DB" -i "$ROOT/database/tests/DT06_Verificar_Schema_370.sql" | tee "$OUT/verify-second.txt"
invariants > "$OUT/invariants-after-second.json"
export_history > "$OUT/history-after-second.txt"
fingerprint > "$OUT/fingerprint-after-second.sha256"
printf '%s\n' "$(manifest_hash)" > "$OUT/manifest-after-second.sha256"
cmp -s "$OUT/history-after-first.txt" "$OUT/history-after-second.txt" || { echo "DT06: ledger mudou na reaplicação" >&2; exit 5; }
cmp -s "$OUT/fingerprint-after-first.sha256" "$OUT/fingerprint-after-second.sha256" || { echo "DT06: fingerprint mudou na reaplicação" >&2; exit 5; }
cmp -s "$OUT/invariants-after-first.json" "$OUT/invariants-after-second.json" || { echo "DT06: invariantes/contagens mudaram na reaplicação" >&2; exit 5; }
cmp -s "$OUT/manifest-after-first.sha256" "$OUT/manifest-after-second.sha256" || { echo "DT06: hash do manifesto mudou na reaplicação" >&2; exit 5; }

# 5) Rollback operacional: restaura o backup pré-upgrade no MESMO banco sintético isolado.
sql -d master -Q "ALTER DATABASE [$DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [$DB] FROM DISK=N'$BACKUP_PATH' WITH REPLACE,CHECKSUM; ALTER DATABASE [$DB] SET MULTI_USER;"
invariants > "$OUT/invariants-after-restore.json"
printf '%s\n' "$(manifest_hash)" > "$OUT/manifest-after-restore.sha256"
sql -d "$DB" -W -h -1 -Q "SET NOCOUNT ON; SELECT COUNT_BIG(*) FROM ref.gestor WHERE codigo LIKE N'DT06_%';" \
  | tr -d '\r' | sed '/^[[:space:]]*$/d' | tail -1 | xargs > "$OUT/synthetic-count-after-restore.txt"
cmp -s "$OUT/invariants-before.json" "$OUT/invariants-after-restore.json" || { echo "DT06: rollback não restaurou as contagens/invariantes pré-upgrade" >&2; exit 6; }
cmp -s "$OUT/manifest-before.sha256" "$OUT/manifest-after-restore.sha256" || { echo "DT06: hash do manifesto divergiu após rollback" >&2; exit 6; }
cmp -s "$OUT/synthetic-count-before.txt" "$OUT/synthetic-count-after-restore.txt" || { echo "DT06: massa sintética divergiu após rollback" >&2; exit 6; }

cat > "$OUT/result.txt" <<TXT
DT06_ACCEPTANCE=OK
database=$DB
baseline=$BASELINE
historical_clone=true
synthetic_data_preserved=true
backup_checksum_verified=true
upgrade_to_370_verified=true
idempotent_reapply=true
rollback_restore_verified=true
manifest_hash_restored=true
counts_restored=true
jornada_local_touched=false
TXT
cat "$OUT/result.txt"
echo "DT06 ACCEPTANCE: OK; banco [$DB] preservado no estado histórico restaurado para auditoria."
