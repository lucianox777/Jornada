#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$ROOT/.env"
OUT_DIR="$ROOT/.local/linkage-validation"
LABELS="$OUT_DIR/positive-labels.csv"
AUDIT="$OUT_DIR/blocking-pass-audit.json"
FIXTURE="/workspace/database/Jornada_Dev_LinkageValidation.sql"
mkdir -p "$OUT_DIR"

[[ -f "$ENV_FILE" ]] || { echo "ERRO: .env ausente. Execute scripts/local-cluster.sh up." >&2; exit 2; }
SQL_PASSWORD="$(awk -F= '$1=="JORNADA_SQL_SA_PASSWORD"{sub(/^[^=]*=/,""); print; exit}' "$ENV_FILE")"
DB="$(awk -F= '$1=="JORNADA_SQL_DATABASE"{sub(/^[^=]*=/,""); print; exit}' "$ENV_FILE")"
DB="${DB:-JornadaLocal}"
[[ -n "$SQL_PASSWORD" ]] || { echo 'ERRO: JORNADA_SQL_SA_PASSWORD ausente do .env.' >&2; exit 2; }

compose() {
  echo "# docker compose --env-file $ENV_FILE $*"
  (cd "$ROOT" && docker compose --env-file "$ENV_FILE" "$@")
}

sql_lines() {
  local query="$1"
  echo "# docker compose --env-file $ENV_FILE exec -T -e SQLCMDPASSWORD=<redacted> sqlserver sqlcmd -S localhost -U sa -C -b -d $DB -W -h -1 -s '|' -Q '<query>'" >&2
  (cd "$ROOT" && docker compose --env-file "$ENV_FILE" exec -T -e "SQLCMDPASSWORD=$SQL_PASSWORD" sqlserver \
    /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d "$DB" -W -h -1 -s '|' -Q "SET NOCOUNT ON; $query") \
    | tr -d '\r' | sed '/^[[:space:]]*$/d;/^([0-9][0-9]* rows\{0,1\} affected)$/d'
}

scalar() {
  sql_lines "$1" | tail -n 1 | xargs
}

complete_validation_run_id() {
  scalar "SELECT TOP(1) CONVERT(varchar(36),lr.linkage_run_id) FROM identidade.linkage_run lr CROSS APPLY (SELECT SUM(CASE WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$model_short-POS-%' THEN 1 ELSE 0 END) AS pos_count,SUM(CASE WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$model_short-NEG-%' THEN 1 ELSE 0 END) AS neg_count,SUM(CASE WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-$model_short-CONFLICT-%' THEN 1 ELSE 0 END) AS conflict_count FROM identidade.linkage_resultado r JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id WHERE r.linkage_run_id=lr.linkage_run_id) c WHERE lr.status='PUBLICADO' AND lr.tipo_run='ON_DEMAND' AND lr.modelo_id='$active_model_id' AND c.pos_count=40 AND c.neg_count=40 AND c.conflict_count=10 ORDER BY lr.publicado_em DESC,lr.iniciado_em DESC,lr.linkage_run_id DESC;"
}

active_model_id="$(scalar "SELECT TOP(1) CONVERT(varchar(36),modelo_id) FROM identidade.modelo_linkage WHERE status='ATIVO' AND ISNULL(amostra_metodo,'')<>'SEED_DEV_FIXO_NAO_TREINADO' ORDER BY versao DESC;")"
[[ -n "$active_model_id" ]] || { echo "ERRO: nenhum modelo calibrado ATIVO. Execute scripts/local-cluster.sh calibrate." >&2; exit 3; }
model_short="${active_model_id:0:8}"
model_version="$(scalar "SELECT versao FROM identidade.modelo_linkage WHERE modelo_id='$active_model_id';")"
algorithm_version="$(scalar "SELECT algoritmo_versao FROM identidade.modelo_linkage WHERE modelo_id='$active_model_id';")"
echo "Validação independente: modelo v$model_version / $active_model_id / $algorithm_version"

# Injeta o corpus somente depois de confirmar o modelo ativo.
echo "# docker compose --env-file $ENV_FILE exec -T -e SQLCMDPASSWORD=<redacted> sqlserver sqlcmd -S localhost -U sa -C -b -d $DB -i $FIXTURE"
(cd "$ROOT" && docker compose --env-file "$ENV_FILE" exec -T -e "SQLCMDPASSWORD=$SQL_PASSWORD" sqlserver \
  /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d "$DB" -i "$FIXTURE")

# Reutiliza evidência completa já publicada para o mesmo modelo. Isso torna a validação idempotente:
# observações resolvidas deixam de entrar no próximo ON_DEMAND e um segundo run isolado seria parcial.
run_id="$(complete_validation_run_id)"
if [[ -n "$run_id" ]]; then
  echo "Reutilizando run completo já publicado para este modelo: $run_id"
else
  echo '# bash scripts/local-cluster.sh linkage'
  (cd "$ROOT" && bash scripts/local-cluster.sh linkage)
  run_id="$(complete_validation_run_id)"
fi
[[ -n "$run_id" ]] || { echo 'ERRO: linkage completo de validação (40 positivos, 40 negativos, 10 conflitos) não foi publicado.' >&2; exit 4; }
echo "Run de validação: $run_id"

cat > "$LABELS" <<'CSV'
pessoa_observacao_id,pessoa_uuid_verdade
CSV
sql_lines "WITH pos AS (SELECT po.pessoa_observacao_id,TRY_CONVERT(int,RIGHT(po.codigo_pessoa_origem,6)) AS n FROM silver.pessoa_observacao po WHERE po.codigo_pessoa_origem LIKE N'SCALE-VAL-$model_short-POS-%'), truth AS (SELECT p.pessoa_observacao_id,vc.pessoa_uuid FROM pos p JOIN silver.pessoa_observacao tpo ON tpo.codigo_pessoa_origem=CONCAT(N'SCALE-SEHAB-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),p.n),10)) JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=tpo.pessoa_observacao_id AND vc.status=N'RESOLVIDO' AND vc.pessoa_uuid IS NOT NULL) SELECT CONCAT(pessoa_observacao_id,',',CONVERT(varchar(36),pessoa_uuid)) FROM truth ORDER BY pessoa_observacao_id;" >> "$LABELS"
label_count="$(( $(wc -l < "$LABELS") - 1 ))"
[[ "$label_count" -eq 40 ]] || { echo "ERRO: esperados 40 rótulos positivos; obtidos=$label_count" >&2; exit 5; }

compose cp "$LABELS" jornada-node2:/tmp/jornada-linkage-validation-labels.csv
compose exec -T jornada-node2 dotnet /opt/jornada/apps/Jornada.Linkage.Runner/Jornada.Linkage.Runner.dll \
  --blocking-pass-audit-labels /tmp/jornada-linkage-validation-labels.csv \
  --blocking-pass-audit-output /tmp/jornada-linkage-validation-blocking.json \
  --ProbabilisticLinkage:CommandTimeoutSeconds 300
compose cp jornada-node2:/tmp/jornada-linkage-validation-blocking.json "$AUDIT"

jq -e '.summary.sampleSize == 40 and .summary.truthInsideUnion == 40 and .summary.unionRecallPct == 100' "$AUDIT" >/dev/null || {
  echo 'ERRO: blocking do corpus positivo não recuperou 100% das verdades do fixture.' >&2
  jq '.summary' "$AUDIT" >&2
  exit 6
}

negative_metrics="$(scalar "SELECT CONCAT(COUNT_BIG(*),'|',SUM(CASE WHEN r.status='RESOLVIDO' THEN 1 ELSE 0 END),'|',SUM(CASE WHEN r.status<>'RESOLVIDO' THEN 1 ELSE 0 END),'|',SUM(CASE WHEN r.melhor_candidato_uuid IS NOT NULL THEN 1 ELSE 0 END)) FROM identidade.linkage_resultado r JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id WHERE r.linkage_run_id='$run_id' AND po.codigo_pessoa_origem LIKE N'SCALE-VAL-$model_short-NEG-%';")"
IFS='|' read -r neg_total neg_resolved neg_rejected neg_candidate_exposure <<< "$negative_metrics"
[[ "$neg_total" -eq 40 ]] || { echo "ERRO: negativos avaliados=$neg_total; esperado=40" >&2; exit 7; }
[[ "$neg_candidate_exposure" -ge 30 ]] || { echo "ERRO: exposição a candidato insuficiente nos negativos: $neg_candidate_exposure/40" >&2; exit 7; }

threshold="$(scalar "SELECT CONVERT(varchar(40),valor) FROM identidade.parametro_linkage WHERE modelo_id='$active_model_id' AND nome='T_LINKAGE';")"
conflict_metrics="$(scalar "SELECT CONCAT(COUNT_BIG(*),'|',SUM(CASE WHEN r.status='CONFLITO' THEN 1 ELSE 0 END),'|',SUM(CASE WHEN r.margem=0 THEN 1 ELSE 0 END),'|',SUM(CASE WHEN r.score_melhor>=$threshold THEN 1 ELSE 0 END),'|',SUM(CASE WHEN r.status='RESOLVIDO' THEN 1 ELSE 0 END)) FROM identidade.linkage_resultado r JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id WHERE r.linkage_run_id='$run_id' AND po.codigo_pessoa_origem LIKE N'SCALE-VAL-$model_short-CONFLICT-%';")"
IFS='|' read -r conf_total conf_status conf_margin0 conf_above conf_resolved <<< "$conflict_metrics"
if [[ "$conf_total" -ne 10 || "$conf_status" -ne 10 || "$conf_margin0" -ne 10 || "$conf_above" -ne 10 || "$conf_resolved" -ne 0 ]]; then
  echo "ERRO: probe de conflito incompleto: total=$conf_total conflito=$conf_status margem0=$conf_margin0 acimaT=$conf_above resolvidos=$conf_resolved" >&2
  exit 8
fi

positive_metrics="$(scalar "WITH truth AS (SELECT r.*,vc.pessoa_uuid AS truth_uuid FROM identidade.linkage_resultado r JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id JOIN silver.pessoa_observacao tpo ON tpo.codigo_pessoa_origem=CONCAT(N'SCALE-SEHAB-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),TRY_CONVERT(int,RIGHT(po.codigo_pessoa_origem,6))),10)) JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=tpo.pessoa_observacao_id AND vc.status=N'RESOLVIDO' AND vc.pessoa_uuid IS NOT NULL WHERE r.linkage_run_id='$run_id' AND po.codigo_pessoa_origem LIKE N'SCALE-VAL-$model_short-POS-%') SELECT CONCAT(COUNT_BIG(*),'|',SUM(CASE WHEN status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END),'|',SUM(CASE WHEN status='RESOLVIDO' AND (pessoa_uuid_resolvido IS NULL OR pessoa_uuid_resolvido<>truth_uuid) THEN 1 ELSE 0 END),'|',SUM(CASE WHEN status<>'RESOLVIDO' THEN 1 ELSE 0 END)) FROM truth;")"
IFS='|' read -r pos_total pos_correct pos_wrong pos_unresolved <<< "$positive_metrics"

frontier="$(scalar "WITH v AS (SELECT r.score_melhor FROM identidade.linkage_resultado r JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id WHERE r.linkage_run_id='$run_id' AND (po.codigo_pessoa_origem LIKE N'SCALE-VAL-$model_short-POS-%' OR po.codigo_pessoa_origem LIKE N'SCALE-VAL-$model_short-NEG-%' OR po.codigo_pessoa_origem LIKE N'SCALE-VAL-$model_short-CONFLICT-%')) SELECT CONCAT(SUM(CASE WHEN ABS(score_melhor-$threshold)<=0.02 THEN 1 ELSE 0 END),'|',COALESCE(CONVERT(varchar(40),MAX(CASE WHEN score_melhor<$threshold THEN score_melhor END)),'NULL'),'|',COALESCE(CONVERT(varchar(40),MIN(CASE WHEN score_melhor>=$threshold THEN score_melhor END)),'NULL')) FROM v;")"
IFS='|' read -r frontier_count max_below min_above <<< "$frontier"

echo '=== VALIDAÇÃO INDEPENDENTE DO LINKAGE (DEV SINTÉTICO) ==='
echo "blocking: 40/40 verdades recuperadas no conjunto candidato"
echo "positivos: corretos=$pos_correct/$pos_total errados=$pos_wrong não_resolvidos_ou_conflitos=$pos_unresolved"
echo "negativos: falsos_vínculos=$neg_resolved/$neg_total rejeitados_ou_conflitos=$neg_rejected candidatos_expostos=$neg_candidate_exposure"
echo "conflito forçado: conflito=$conf_status/$conf_total margem_zero=$conf_margin0 acima_threshold=$conf_above"
echo "fronteira T=$threshold: casos ±0,02=$frontier_count max_abaixo=$max_below min_acima=$min_above"
echo "auditoria blocking: $AUDIT"
echo 'LINKAGE INDEPENDENT VALIDATION STRUCTURAL GATES: OK'
