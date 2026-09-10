#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$ROOT/.env"
EXAMPLE="$ROOT/.env.example"
BASELINE_REL="${JORNADA_DDL_BASELINE:-database/baselines/Jornada_Fase1_v3.65.sql}"
BASELINE_SEED_REL="${JORNADA_DDL_BASELINE_SEED:-database/baselines/Jornada_Seed_Dev_v3.65.sql}"
CURRENT_REL="${JORNADA_DDL_CURRENT:-database/Jornada_Fase1_v3.70.sql}"
DB="${JORNADA_DDL_UPGRADE_DATABASE:-JornadaDdlUpgradeCheck}"

need(){ command -v "$1" >/dev/null 2>&1 || { echo "ERRO: comando '$1' não encontrado." >&2; exit 2; }; }
need docker; need sha256sum
[[ -f "$ENV_FILE" ]] || cp "$EXAMPLE" "$ENV_FILE"
# shellcheck disable=SC1090
set -a; source "$ENV_FILE"; set +a
: "${JORNADA_SQL_SA_PASSWORD:?JORNADA_SQL_SA_PASSWORD não definido}"
[[ "$DB" =~ ^[A-Za-z0-9_]+$ ]] || { echo "ERRO: nome de banco inválido." >&2; exit 2; }
[[ -f "$ROOT/$BASELINE_REL" ]] || { echo "ERRO: baseline não encontrado: $BASELINE_REL" >&2; exit 2; }
[[ -f "$ROOT/$BASELINE_SEED_REL" ]] || { echo "ERRO: seed do baseline não encontrado: $BASELINE_SEED_REL" >&2; exit 2; }
[[ -f "$ROOT/$CURRENT_REL" ]] || { echo "ERRO: instalador corrente não encontrado: $CURRENT_REL" >&2; exit 2; }
mkdir -p "$ROOT/.local/ddl-upgrade"

compose(){ (cd "$ROOT" && docker compose --env-file "$ENV_FILE" "$@"); }
sqlcmd(){ compose exec -T -e "SQLCMDPASSWORD=$JORNADA_SQL_SA_PASSWORD" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I "$@"; }
wait_healthy(){ for _ in $(seq 1 60); do [[ "$(docker inspect -f '{{.State.Health.Status}}' jornada-sqlserver-local 2>/dev/null || true)" == healthy ]] && return 0; sleep 2; done; echo "ERRO: SQL Server não ficou healthy." >&2; exit 3; }
fingerprint(){ local tag="$1"; local out="$ROOT/.local/ddl-upgrade/fingerprint-$tag.txt"; sqlcmd -d "$DB" -i /workspace/database/Jornada_Dev_DdlFingerprint.sql -W -h -1 > "$out"; sed -i '/^[[:space:]]*$/d' "$out"; sha256sum "$out" | awk '{print $1}'; }
assert_sentinel(){ local n; n="$(sqlcmd -d "$DB" -W -h -1 -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM ref.gestor WHERE codigo='ZZ_UPGRADE_SENTINEL' AND nome='Sentinela DDL Upgrade';" | tr -d '[:space:]')"; [[ "$n" == 1 ]] || { echo "ERRO: dado sentinela não foi preservado." >&2; exit 4; }; }
assert_phone_v2(){
  local n
  n="$(sqlcmd -d "$DB" -W -h -1 -Q "SET NOCOUNT ON; SELECT CASE WHEN ref.fn_telefone_br_canonico_v2(N'00 55 11 99999-0001')='5511999990001' AND ref.fn_telefone_br_canonico_v2(N'+55 (11) 99999-0001')='5511999990001' AND ref.fn_telefone_br_canonico_v2(NCHAR(9)+N'+1 (212) 555-0100'+NCHAR(13)+NCHAR(10))='12125550100' AND ref.fn_telefone_br_canonico_v2(NCHAR(160)+N'+55 (11) 99999-0001'+NCHAR(160))='5511999990001' AND (SELECT atributo_instancia_chave FROM silver.pessoa_atributo_observacao WHERE source_record_id='SEH001-TEL-1')='5511999990001' AND (SELECT atributo_instancia_chave FROM gold.pessoa_atributo WHERE source_record_id='SEH001-TEL-1' AND vigencia_fim IS NULL)='5511999990001' THEN 1 ELSE 0 END;" | tr -d '[:space:]')"
  [[ "$n" == 1 ]] || { echo "ERRO: migração TELEFONE_BR_CANONICO_V2 não convergiu a chave legada 00." >&2; exit 6; }
}
assert_email_v2(){
  local n
  n="$(sqlcmd -d "$DB" -W -h -1 -Q "SET NOCOUNT ON; SELECT CASE WHEN ref.fn_email_canonico_v2(N'JOSÉ@EXAMPLE.ORG')=N'josÉ@example.org' AND ref.fn_email_canonico_v2(N'Jose'+NCHAR(769)+N'@Example.org')=N'jose'+NCHAR(769)+N'@example.org' AND (SELECT atributo_instancia_chave FROM silver.pessoa_atributo_observacao WHERE source_record_id='SEH002-EMAIL-1')=N'josÉ@example.org' AND (SELECT atributo_instancia_chave FROM gold.pessoa_atributo WHERE source_record_id='SEH002-EMAIL-1' AND vigencia_fim IS NULL)=N'josÉ@example.org' THEN 1 ELSE 0 END;" | tr -d '[:space:]')"
  [[ "$n" == 1 ]] || { echo "ERRO: migração EMAIL_CANONICO_V2 não convergiu a chave legada." >&2; exit 7; }
}
assert_schema_marker(){
  local n
  n="$(sqlcmd -d "$DB" -W -h -1 -Q "SET NOCOUNT ON; SELECT CASE WHEN CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.BaseNormativa'))=N'3.62' AND CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema'))=N'3.70' THEN 1 ELSE 0 END;" | tr -d '[:space:]')"
  [[ "$n" == 1 ]] || { echo "ERRO: marcador de versão do schema não está em Base 3.62 / Solution 3.70." >&2; exit 8; }
}

compose up -d sqlserver; wait_healthy
sqlcmd -Q "IF DB_ID(N'$DB') IS NOT NULL BEGIN ALTER DATABASE [$DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DB]; END; CREATE DATABASE [$DB];"
# O container monta a raiz em /workspace; baseline é configurável para futuros releases.
sqlcmd -d "$DB" -i "/workspace/$BASELINE_REL"
sqlcmd -d "$DB" -Q "INSERT ref.gestor(codigo,nome,ativo) VALUES('ZZ_UPGRADE_SENTINEL','Sentinela DDL Upgrade',1);"
# Fixture real de upgrade a partir do baseline v3.65: simula chave V1 que perdeu o prefixo internacional explícito 00.
sqlcmd -d "$DB" -i "/workspace/$BASELINE_SEED_REL"
sqlcmd -d "$DB" -Q "DECLARE @id BIGINT=(SELECT pessoa_atributo_observacao_id FROM silver.pessoa_atributo_observacao WHERE source_record_id='SEH001-TEL-1'); UPDATE silver.pessoa_atributo_observacao SET valor=N'00 55 11 99999-0001',atributo_instancia_chave='005511999990001' WHERE pessoa_atributo_observacao_id=@id; UPDATE gold.pessoa_atributo SET valor=N'00 55 11 99999-0001',atributo_instancia_chave='005511999990001' WHERE pessoa_atributo_observacao_id=@id AND vigencia_fim IS NULL;"
sqlcmd -d "$DB" -Q "DECLARE @id BIGINT=(SELECT pessoa_atributo_observacao_id FROM silver.pessoa_atributo_observacao WHERE source_record_id='SEH002-EMAIL-1'); UPDATE silver.pessoa_atributo_observacao SET valor=N'JOSÉ@EXAMPLE.ORG',atributo_instancia_chave=N'josé@example.org' WHERE pessoa_atributo_observacao_id=@id; UPDATE gold.pessoa_atributo SET valor=N'JOSÉ@EXAMPLE.ORG',atributo_instancia_chave=N'josé@example.org' WHERE pessoa_atributo_observacao_id=@id AND vigencia_fim IS NULL;"
baseline_hash="$(fingerprint baseline)"
# FOR JSON retorna NVARCHAR(MAX); -y 0 evita truncamento do payload que seria entregue ao gate Python.
sqlcmd -d "$DB" -i /workspace/database/Jornada_Upgrade_Invariants.sql -y 0 -w 65535 | sed -n '/^[[:space:]]*{/,$p' | tr -d "\r\n" > "$ROOT/.local/ddl-upgrade/invariants-before.json"

sqlcmd -d "$DB" -i "/workspace/$CURRENT_REL"
assert_sentinel
assert_phone_v2
assert_email_v2
assert_schema_marker
sqlcmd -d "$DB" -i /workspace/database/Jornada_Runtime_Smoke.sql
first_hash="$(fingerprint current-first)"

sqlcmd -d "$DB" -i "/workspace/$CURRENT_REL"
assert_sentinel
second_hash="$(fingerprint current-second)"
sqlcmd -d "$DB" -i /workspace/database/Jornada_Upgrade_Invariants.sql -y 0 -w 65535 | sed -n '/^[[:space:]]*{/,$p' | tr -d "\r\n" > "$ROOT/.local/ddl-upgrade/invariants-after.json"
python3 "$ROOT/scripts/upgrade-invariant-gate.py" "$ROOT/.local/ddl-upgrade/invariants-before.json" "$ROOT/.local/ddl-upgrade/invariants-after.json" --summary "$ROOT/.local/ddl-upgrade/invariant-summary.json"

[[ "$first_hash" == "$second_hash" ]] || { echo "ERRO: fingerprint do DDL mudou na segunda aplicação; idempotência violada." >&2; exit 5; }
cat > "$ROOT/.local/ddl-upgrade/result.txt" <<TXT
baseline=$BASELINE_REL
baseline_seed=$BASELINE_SEED_REL
current=$CURRENT_REL
baseline_fingerprint=$baseline_hash
current_first_fingerprint=$first_hash
current_second_fingerprint=$second_hash
sentinel_preserved=true
phone_v2_legacy_00_migrated=true
email_v2_legacy_migrated=true
schema_marker_exact=true
runtime_smoke=true
upgrade_invariants=true
idempotent=true
TXT
cat "$ROOT/.local/ddl-upgrade/result.txt"
echo "DDL UPGRADE GATE: OK"
