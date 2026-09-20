#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$ROOT/.env"
EXAMPLE="$ROOT/.env.example"
CONFIG="$ROOT/install/windows-production/Jornada.Cluster.Test.json"

command -v docker >/dev/null 2>&1 || { echo "Docker não encontrado no PATH." >&2; exit 2; }
[[ -f "$ENV_FILE" ]] || cp "$EXAMPLE" "$ENV_FILE"

compose() { (cd "$ROOT" && docker compose --env-file "$ENV_FILE" "$@"); }
env_value() { local name="$1"; sed -nE "s/^${name}=(.*)$/\1/p" "$ENV_FILE" | tail -n1; }
sql_scalar() {
  local query="$1" password
  password="$(env_value JORNADA_SQL_SA_PASSWORD)"
  [[ -n "$password" ]] || { echo "JORNADA_SQL_SA_PASSWORD ausente do .env" >&2; return 2; }
  compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$password" -C -d JornadaLocal -W -h -1 -b -Q "SET NOCOUNT ON; $query" \
    | awk 'NF{last=$0} END{gsub(/^[[:space:]]+|[[:space:]]+$/, "", last); print last}'
}
sql_report() {
  local query="$1" password
  password="$(env_value JORNADA_SQL_SA_PASSWORD)"
  [[ -n "$password" ]] || { echo "JORNADA_SQL_SA_PASSWORD ausente do .env" >&2; return 2; }
  compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$password" -C -d JornadaLocal -W -s '|' -b -Q "SET NOCOUNT ON; $query"
}

wait_node() {
  local name="$1" url="$2"
  for _ in $(seq 1 120); do
    if curl -fsS "$url" >/dev/null 2>&1; then echo "$name ready: $url"; return 0; fi
    sleep 1
  done
  compose logs --tail 120 "$name" || true
  echo "$name não ficou ready: $url" >&2
  return 1
}

ensure_local_blocking_projection() {
  echo "Verificando projeção de blocking da massa sintética local (contadores do worker mostram apenas reconstruções/chaves novas desta chamada)..."
  compose exec -T jornada-node2 env Processor__Operation=REBUILD_LOCAL_BLOCKING dotnet /opt/jornada/apps/Jornada.Processor.Worker/Jornada.Processor.Worker.dll
}

show_endpoints() {
  local bundle schema
  bundle="$(jq -r '.configurationBundleVersion' "$CONFIG")"
  schema="$(jq -r '.solutionSchema' "$CONFIG")"
  cat <<EOF

Cluster local pronto (4 containers canônicos):
  NODE1: http://127.0.0.1:5080
  NODE2: http://127.0.0.1:5180
  SQL:   localhost:14333
  NAS:   jornada-nas:445 / share bronze (host: localhost:1445)
  Config bundle: $bundle / SolutionSchema $schema

Monitor operacional:
  NODE1: http://127.0.0.1:5080/monitor
  NODE2: http://127.0.0.1:5180/monitor
  Com VIP/LB externo, use /monitor no endereço do balanceador.

Execuções únicas recomendadas no NODE2 (não agendadas):
  Calibrador completo: ./scripts/local-cluster.sh calibrate
    /opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll
  Linkage:             ./scripts/local-cluster.sh linkage
    /opt/jornada/apps/Jornada.Linkage.Runner/Jornada.Linkage.Runner.dll
  Diagnóstico Linkage: ./scripts/local-cluster.sh linkage-diagnose
  Bronze Verify:
    docker compose exec jornada-node2 dotnet /opt/jornada/tools/Jornada.Bronze.Verify/Jornada.Bronze.Verify.dll --help
  Linkage Evaluation (DEV/HML only):
    docker compose exec jornada-node2 dotnet /opt/jornada/tools/Jornada.Linkage.Evaluation/Jornada.Linkage.Evaluation.dll --help
EOF
}

start_nodes() {
  local build="${1:-false}"
  if [[ "$build" == "true" ]]; then compose up -d --build jornada-node1 jornada-node2; else compose up -d jornada-node1 jornada-node2; fi
  wait_node jornada-node1 http://127.0.0.1:5080/health/ready
  wait_node jornada-node2 http://127.0.0.1:5180/health/ready
  ensure_local_blocking_projection
  show_endpoints
}

calibrate() {
  local before count version active model_id
  ensure_local_blocking_projection
  before="$(sql_scalar "SELECT ISNULL(MAX(versao),0) FROM identidade.modelo_linkage;")"
  echo "Calibração iniciando após modelo v$before."
  echo 'Referência IBGE canônica é materializada no bootstrap do ambiente; GENERATE_DRAFT só usa o fallback de carga em banco criado fora do fluxo oficial.'
  compose exec -T jornada-node2 env LinkageParameters__Operation=GENERATE_DRAFT LinkageParameters__RunOnce=true LinkageParameters__MinimumIndependentMatchedPairs="${JORNADA_LINKAGE_MIN_MATCHED_PAIRS:-2500}" dotnet /opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll
  count="$(sql_scalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE versao>$before AND status='RASCUNHO';")"
  [[ "$count" == "1" ]] || { echo "Esperado exatamente um novo RASCUNHO; encontrados=$count" >&2; return 3; }
  version="$(sql_scalar "SELECT MAX(versao) FROM identidade.modelo_linkage WHERE versao>$before AND status='RASCUNHO';")"
  compose exec -T jornada-node2 env LinkageParameters__Operation=VALIDATE LinkageParameters__TargetVersion="$version" LinkageParameters__RunOnce=true dotnet /opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll
  compose exec -T jornada-node2 env LinkageParameters__Operation=ACTIVATE LinkageParameters__TargetVersion="$version" LinkageParameters__RunOnce=true dotnet /opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll
  active="$(sql_scalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE versao=$version AND status='ATIVO' AND ISNULL(amostra_metodo,'') <> 'SEED_DEV_FIXO_NAO_TREINADO';")"
  [[ "$active" == "1" ]] || { echo "Modelo v$version não ficou ATIVO como modelo calibrado." >&2; return 4; }
  model_id="$(sql_scalar "SELECT CONVERT(varchar(36),modelo_id) FROM identidade.modelo_linkage WHERE versao=$version;")"
  echo "Calibração concluída: modelo calibrado v$version / ModeloId=$model_id ATIVO."
}

run_linkage() {
  local active version model_id
  ensure_local_blocking_projection
  active="$(sql_scalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE status='ATIVO' AND ISNULL(amostra_metodo,'') <> 'SEED_DEV_FIXO_NAO_TREINADO';")"
  [[ "$active" == "1" ]] || { echo "Linkage bloqueado: encontrados $active modelos calibrados ATIVOS. O seed sintético não libera execução. Execute primeiro '$0 calibrate'." >&2; return 5; }
  version="$(sql_scalar "SELECT TOP(1) versao FROM identidade.modelo_linkage WHERE status='ATIVO' AND ISNULL(amostra_metodo,'') <> 'SEED_DEV_FIXO_NAO_TREINADO' ORDER BY versao DESC;")"
  model_id="$(sql_scalar "SELECT TOP(1) CONVERT(varchar(36),modelo_id) FROM identidade.modelo_linkage WHERE status='ATIVO' AND ISNULL(amostra_metodo,'') <> 'SEED_DEV_FIXO_NAO_TREINADO' ORDER BY versao DESC;")"
  echo "Executando linkage com modelo calibrado ATIVO v$version / ModeloId=$model_id."
  compose exec -T jornada-node2 dotnet /opt/jornada/apps/Jornada.Linkage.Runner/Jornada.Linkage.Runner.dll --mode ON_DEMAND --publish true --requested-by LOCAL_CLUSTER --reason manual-local-cluster
}

diagnose_linkage() {
  local active_model_id run_id
  active_model_id="$(sql_scalar "SELECT TOP(1) CONVERT(varchar(36),modelo_id) FROM identidade.modelo_linkage WHERE status='ATIVO' AND ISNULL(amostra_metodo,'') <> 'SEED_DEV_FIXO_NAO_TREINADO' ORDER BY versao DESC;")"
  [[ -n "$active_model_id" ]] || { echo "Diagnóstico bloqueado: nenhum modelo calibrado ATIVO. Execute primeiro '$0 calibrate'." >&2; return 6; }

  run_id="$(sql_scalar "SELECT TOP(1) CONVERT(varchar(36),linkage_run_id) FROM identidade.linkage_run WHERE status='PUBLICADO' AND tipo_run='ON_DEMAND' AND modelo_id='$active_model_id' ORDER BY publicado_em DESC,iniciado_em DESC,linkage_run_id DESC;")"
  [[ -n "$run_id" ]] || { echo "Nenhum linkage ON_DEMAND PUBLICADO para o modelo calibrado ATIVO $active_model_id. Execute primeiro '$0 linkage'." >&2; return 6; }

  echo "Diagnóstico do último linkage ON_DEMAND PUBLICADO do modelo ATIVO $active_model_id: $run_id"
  echo 'Nota: modelo_versao é monotônica somente dentro da base corrente; clean/reset recria a base. Para A/B entre bases, compare modelo_id + fingerprints.'
  echo
  echo 'Resumo do run:'
  sql_report "SELECT CONVERT(varchar(36),linkage_run_id) AS run_id,CONVERT(varchar(36),modelo_id) AS modelo_id,modelo_versao,tipo_run,status,registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,sem_candidato_no_bloco,publicado_em FROM identidade.linkage_run WHERE linkage_run_id='$run_id';"
  echo
  echo 'Proveniência do modelo, ruleset, projeção e thresholds efetivos:'
  sql_report "SELECT CONVERT(varchar(36),lr.modelo_id) AS modelo_id,lr.modelo_versao,m.algoritmo_versao,m.status AS modelo_status,m.amostra_metodo,m.amostra_pool_tamanho,m.amostra_m_tamanho,m.amostra_u_tamanho,rs.ruleset_versao,rs.fingerprint_sha256 AS ruleset_fingerprint,rs.projection_schema_version,rs.projection_fingerprint_sha256 AS projection_fingerprint,MAX(CASE WHEN p.nome='T_LINKAGE' THEN p.valor END) AS t_linkage,COALESCE(MAX(CASE WHEN p.nome='CONFLICT_MARGIN_LOG_ODDS' THEN p.valor END),MAX(CASE WHEN p.nome='CONFLICT_MARGIN' THEN p.valor END)) AS t_margem_efetivo,CASE WHEN m.algoritmo_versao='FELLEGI_SUNTER_DECISION_EVIDENCE_V6' THEN 'LOG_ODDS' ELSE 'POSTERIOR' END AS margem_espaco FROM identidade.linkage_run lr JOIN identidade.modelo_linkage m ON m.modelo_id=lr.modelo_id LEFT JOIN identidade.linkage_ruleset rs ON rs.modelo_id=lr.modelo_id LEFT JOIN identidade.parametro_linkage p ON p.modelo_id=lr.modelo_id WHERE lr.linkage_run_id='$run_id' GROUP BY lr.modelo_id,lr.modelo_versao,m.algoritmo_versao,m.status,m.amostra_metodo,m.amostra_pool_tamanho,m.amostra_m_tamanho,m.amostra_u_tamanho,rs.ruleset_versao,rs.fingerprint_sha256,rs.projection_schema_version,rs.projection_fingerprint_sha256;"
  echo
  echo 'Cobertura da fronteira de decisão (mostra se os thresholds foram realmente exercitados):'
  sql_report "DECLARE @modelo_id uniqueidentifier=(SELECT modelo_id FROM identidade.linkage_run WHERE linkage_run_id='$run_id'); DECLARE @alg nvarchar(100)=(SELECT algoritmo_versao FROM identidade.modelo_linkage WHERE modelo_id=@modelo_id); DECLARE @t decimal(18,8)=(SELECT valor FROM identidade.parametro_linkage WHERE modelo_id=@modelo_id AND nome='T_LINKAGE'); DECLARE @tm decimal(18,8)=COALESCE((SELECT valor FROM identidade.parametro_linkage WHERE modelo_id=@modelo_id AND nome=CASE WHEN @alg='FELLEGI_SUNTER_DECISION_EVIDENCE_V6' THEN 'CONFLICT_MARGIN_LOG_ODDS' ELSE 'CONFLICT_MARGIN' END),0); SELECT @t AS t_linkage,MAX(CASE WHEN score_melhor<@t THEN score_melhor END) AS maior_score_abaixo,MIN(CASE WHEN score_melhor>=@t THEN score_melhor END) AS menor_score_acima,SUM(CASE WHEN ABS(score_melhor-@t)<=0.02 THEN 1 ELSE 0 END) AS qtd_score_em_mais_menos_002,@tm AS t_margem,MAX(CASE WHEN margem IS NOT NULL AND margem<@tm THEN margem END) AS maior_margem_abaixo,MIN(CASE WHEN margem IS NOT NULL AND margem>=@tm THEN margem END) AS menor_margem_acima,SUM(CASE WHEN margem IS NOT NULL AND margem<@tm THEN 1 ELSE 0 END) AS qtd_margem_abaixo FROM identidade.linkage_resultado WHERE linkage_run_id='$run_id';"
  echo
  echo 'Empates e saturação de apresentação:'
  sql_report "SELECT status,COUNT_BIG(*) AS qtd,SUM(CASE WHEN score_segundo IS NOT NULL AND margem=0 THEN 1 ELSE 0 END) AS empate_log_odds_exato,SUM(CASE WHEN score_segundo IS NOT NULL AND score_melhor=score_segundo AND ISNULL(margem,0)<>0 THEN 1 ELSE 0 END) AS posterior_igual_mas_log_odds_distinto,SUM(CASE WHEN score_segundo IS NOT NULL AND score_melhor=score_segundo AND margem=0 THEN 1 ELSE 0 END) AS posterior_e_log_odds_empatados FROM identidade.linkage_resultado WHERE linkage_run_id='$run_id' GROUP BY status ORDER BY status;"
  echo 'Em empate_log_odds_exato, UUID ordena apenas a representação determinística do empate; não constitui evidência de desempate.'
  echo
  echo 'Cobertura empírica da amostra u persistida no modelo:'
  sql_report "SELECT COUNT(*) AS estados_u_com_suporte,MIN(valor) AS suporte_min,MAX(valor) AS suporte_max,SUM(CASE WHEN valor=0 THEN 1 ELSE 0 END) AS estados_zero,SUM(CASE WHEN valor>0 AND valor<5 THEN 1 ELSE 0 END) AS estados_entre_1_e_4 FROM identidade.parametro_linkage WHERE modelo_id=(SELECT modelo_id FROM identidade.linkage_run WHERE linkage_run_id='$run_id') AND nome LIKE 'SUPPORT_U_%'; SELECT nome,valor AS suporte FROM identidade.parametro_linkage WHERE modelo_id=(SELECT modelo_id FROM identidade.linkage_run WHERE linkage_run_id='$run_id') AND nome LIKE 'SUPPORT_U_%' ORDER BY nome;"
  echo
  echo 'Composição atual do corpus Gold (explica SCALE versus seed/outros):'
  sql_report "WITH scale_gold AS (SELECT DISTINCT vc.pessoa_uuid FROM silver.pessoa_observacao po JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id WHERE vc.status='RESOLVIDO' AND po.codigo_pessoa_origem LIKE 'SCALE-SEHAB-%') SELECT COUNT_BIG(*) AS gold_total,SUM(CASE WHEN sg.pessoa_uuid IS NOT NULL THEN CAST(1 AS bigint) ELSE CAST(0 AS bigint) END) AS gold_scale,SUM(CASE WHEN sg.pessoa_uuid IS NULL THEN CAST(1 AS bigint) ELSE CAST(0 AS bigint) END) AS gold_seed_ou_outros FROM gold.pessoa g LEFT JOIN scale_gold sg ON sg.pessoa_uuid=g.pessoa_uuid;"
  echo
  echo 'Qualidade contra ground truth sintético SCALE (verdade derivada do vínculo CPF da observação SEHAB correspondente):'
  sql_report "WITH truth AS (SELECT r.*,po.codigo_pessoa_origem,tv.pessoa_uuid AS truth_uuid FROM identidade.linkage_resultado r JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id JOIN silver.pessoa_observacao tpo ON tpo.codigo_pessoa_origem=REPLACE(po.codigo_pessoa_origem,'SCALE-PEND-','SCALE-SEHAB-') JOIN ref.gestor tg ON tg.gestor_id=tpo.gestor_id AND tg.codigo='SEHAB' JOIN identidade.v_vinculo_corrente tv ON tv.pessoa_observacao_id=tpo.pessoa_observacao_id AND tv.status='RESOLVIDO' WHERE r.linkage_run_id='$run_id' AND po.codigo_pessoa_origem LIKE 'SCALE-PEND-%') SELECT COUNT_BIG(*) AS total_scale,SUM(CASE WHEN status='RESOLVIDO' THEN 1 ELSE 0 END) AS resolvidos,SUM(CASE WHEN status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END) AS resolvidos_corretos,SUM(CASE WHEN status='RESOLVIDO' AND (pessoa_uuid_resolvido IS NULL OR pessoa_uuid_resolvido<>truth_uuid) THEN 1 ELSE 0 END) AS falsos_positivos,SUM(CASE WHEN status='CONFLITO' THEN 1 ELSE 0 END) AS conflitos,SUM(CASE WHEN status='CONFLITO' AND (melhor_candidato_uuid=truth_uuid OR segundo_candidato_uuid=truth_uuid) THEN 1 ELSE 0 END) AS conflitos_verdade_top2,SUM(CASE WHEN status='NAO_RESOLVIDO' THEN 1 ELSE 0 END) AS nao_resolvidos,SUM(CASE WHEN status='NAO_RESOLVIDO' AND NOT(segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001) AND melhor_candidato_uuid=truth_uuid THEN 1 ELSE 0 END) AS nao_resolvidos_verdade_primeiro_sem_empate,SUM(CASE WHEN status='NAO_RESOLVIDO' AND segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001 AND (melhor_candidato_uuid=truth_uuid OR segundo_candidato_uuid=truth_uuid) THEN 1 ELSE 0 END) AS nao_resolvidos_verdade_empate_top2,SUM(CASE WHEN status='NAO_RESOLVIDO' AND NOT(segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001) AND segundo_candidato_uuid=truth_uuid AND (melhor_candidato_uuid IS NULL OR melhor_candidato_uuid<>truth_uuid) THEN 1 ELSE 0 END) AS nao_resolvidos_verdade_segundo_sem_empate,SUM(CASE WHEN status='NAO_RESOLVIDO' AND ISNULL(melhor_candidato_uuid,'00000000-0000-0000-0000-000000000000')<>truth_uuid AND ISNULL(segundo_candidato_uuid,'00000000-0000-0000-0000-000000000000')<>truth_uuid THEN 1 ELSE 0 END) AS nao_resolvidos_verdade_fora_top2,SUM(CASE WHEN status='NAO_RESOLVIDO' AND segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001 THEN 1 ELSE 0 END) AS nao_resolvidos_empate_top2,CAST(100.0*SUM(CASE WHEN status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END)/NULLIF(SUM(CASE WHEN status='RESOLVIDO' THEN 1 ELSE 0 END),0) AS decimal(9,4)) AS ppv_sintetico_pct,CAST(100.0*SUM(CASE WHEN status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END)/NULLIF(COUNT_BIG(*),0) AS decimal(9,4)) AS sensibilidade_sintetica_pct FROM truth;"
  echo
  echo 'Diagnóstico dos conflitos: posição da verdade, saturação e coortes sintéticas:'
  sql_report "WITH truth AS (
  SELECT r.*,po.codigo_pessoa_origem,
         TRY_CONVERT(bigint,RIGHT(po.codigo_pessoa_origem,10)) AS scale_n,
         tv.pessoa_uuid AS truth_uuid
  FROM identidade.linkage_resultado r
  JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id
  JOIN silver.pessoa_observacao tpo ON tpo.codigo_pessoa_origem=REPLACE(po.codigo_pessoa_origem,'SCALE-PEND-','SCALE-SEHAB-')
  JOIN ref.gestor tg ON tg.gestor_id=tpo.gestor_id AND tg.codigo='SEHAB'
  JOIN identidade.v_vinculo_corrente tv ON tv.pessoa_observacao_id=tpo.pessoa_observacao_id AND tv.status='RESOLVIDO'
  WHERE r.linkage_run_id='$run_id' AND po.codigo_pessoa_origem LIKE 'SCALE-PEND-%'
)
SELECT
  SUM(CASE WHEN status='CONFLITO' THEN 1 ELSE 0 END) AS conflitos,
  SUM(CASE WHEN status='CONFLITO' AND NOT(segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001) AND melhor_candidato_uuid=truth_uuid THEN 1 ELSE 0 END) AS verdade_primeiro_sem_empate,
  SUM(CASE WHEN status='CONFLITO' AND segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001 AND (melhor_candidato_uuid=truth_uuid OR segundo_candidato_uuid=truth_uuid) THEN 1 ELSE 0 END) AS verdade_empate_top2,
  SUM(CASE WHEN status='CONFLITO' AND NOT(segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001) AND segundo_candidato_uuid=truth_uuid AND (melhor_candidato_uuid IS NULL OR melhor_candidato_uuid<>truth_uuid) THEN 1 ELSE 0 END) AS verdade_segundo_sem_empate,
  SUM(CASE WHEN status='CONFLITO' AND ISNULL(melhor_candidato_uuid,'00000000-0000-0000-0000-000000000000')<>truth_uuid AND ISNULL(segundo_candidato_uuid,'00000000-0000-0000-0000-000000000000')<>truth_uuid THEN 1 ELSE 0 END) AS verdade_fora_top2,
  SUM(CASE WHEN status='CONFLITO' AND motivo='DOIS_CANDIDATOS_ACIMA_T_LINKAGE' THEN 1 ELSE 0 END) AS conflitos_duplo_threshold,
  SUM(CASE WHEN status='CONFLITO' AND score_melhor>=0.9999 THEN 1 ELSE 0 END) AS melhor_posterior_ge_09999,
  SUM(CASE WHEN status='CONFLITO' AND score_segundo>=0.999 THEN 1 ELSE 0 END) AS segundo_posterior_ge_0999,
  SUM(CASE WHEN status='CONFLITO' AND score_segundo>=0.9999 THEN 1 ELSE 0 END) AS segundo_posterior_ge_09999,
  CAST(MIN(CASE WHEN status='CONFLITO' THEN margem END) AS decimal(30,12)) AS margem_min,
  CAST(AVG(CASE WHEN status='CONFLITO' THEN CONVERT(decimal(30,12),margem) END) AS decimal(30,12)) AS margem_media,
  CAST(MAX(CASE WHEN status='CONFLITO' THEN margem END) AS decimal(30,12)) AS margem_max,
  SUM(CASE WHEN scale_n%10=0 THEN 1 ELSE 0 END) AS coorte_cada_decimo_total,
  SUM(CASE WHEN scale_n%10=0 AND status='RESOLVIDO' THEN 1 ELSE 0 END) AS coorte_cada_decimo_resolvidos,
  SUM(CASE WHEN scale_n%10<>0 THEN 1 ELSE 0 END) AS coorte_demais_total,
  SUM(CASE WHEN scale_n%10<>0 AND status='CONFLITO' THEN 1 ELSE 0 END) AS coorte_demais_conflitos,
  CAST(CASE
    WHEN SUM(CASE WHEN status='RESOLVIDO' AND (pessoa_uuid_resolvido IS NULL OR pessoa_uuid_resolvido<>truth_uuid) THEN 1 ELSE 0 END)=0
     AND SUM(CASE WHEN status='RESOLVIDO' THEN 1 ELSE 0 END)>0
    THEN 300.0/SUM(CASE WHEN status='RESOLVIDO' THEN 1 ELSE 0 END)
  END AS decimal(9,4)) AS limite_superior_fp_95_regra_tres_pct
FROM truth;"

  echo
  echo 'Falsos positivos resolvidos no corpus SCALE (deve ficar vazio em um ensaio conservador):'
  sql_report "WITH truth AS (SELECT r.*,po.codigo_pessoa_origem,tv.pessoa_uuid AS truth_uuid FROM identidade.linkage_resultado r JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id JOIN silver.pessoa_observacao tpo ON tpo.codigo_pessoa_origem=REPLACE(po.codigo_pessoa_origem,'SCALE-PEND-','SCALE-SEHAB-') JOIN ref.gestor tg ON tg.gestor_id=tpo.gestor_id AND tg.codigo='SEHAB' JOIN identidade.v_vinculo_corrente tv ON tv.pessoa_observacao_id=tpo.pessoa_observacao_id AND tv.status='RESOLVIDO' WHERE r.linkage_run_id='$run_id' AND po.codigo_pessoa_origem LIKE 'SCALE-PEND-%') SELECT pessoa_observacao_id,codigo_pessoa_origem,score_melhor,score_segundo,margem,CONVERT(varchar(36),truth_uuid) AS truth_uuid,CONVERT(varchar(36),pessoa_uuid_resolvido) AS resolvido_uuid,CONVERT(varchar(36),melhor_candidato_uuid) AS melhor_candidato_uuid,CONVERT(varchar(36),segundo_candidato_uuid) AS segundo_candidato_uuid FROM truth WHERE status='RESOLVIDO' AND (pessoa_uuid_resolvido IS NULL OR pessoa_uuid_resolvido<>truth_uuid) ORDER BY codigo_pessoa_origem;"
  echo
  echo 'Não resolvidos/conflitos por motivo:'
  sql_report "SELECT r.status,COALESCE(r.motivo,'SEM_MOTIVO') AS motivo,COUNT_BIG(*) AS qtd,MIN(r.score_melhor) AS score_min,AVG(r.score_melhor) AS score_medio,MAX(r.score_melhor) AS score_max FROM identidade.linkage_resultado r WHERE r.linkage_run_id='$run_id' AND r.status<>'RESOLVIDO' GROUP BY r.status,r.motivo ORDER BY qtd DESC,r.status,r.motivo;"
  echo
  echo 'Detalhe dos não resolvidos/conflitos:'
  sql_report "SELECT r.pessoa_observacao_id,po.codigo_pessoa_origem,g.codigo AS gestor,r.status,COALESCE(r.motivo,'SEM_MOTIVO') AS motivo,r.score_melhor,r.score_segundo,r.margem,CASE WHEN r.score_segundo IS NOT NULL AND r.margem=0 THEN 'EMPATE_EVIDENCIAL_UUID_APENAS_DETERMINISTICO' ELSE 'ORDEM_EVIDENCIAL' END AS ranking_interpretacao,CONVERT(varchar(36),r.melhor_candidato_uuid) AS melhor_candidato_uuid,CONVERT(varchar(36),r.segundo_candidato_uuid) AS segundo_candidato_uuid FROM identidade.linkage_resultado r JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id JOIN ref.gestor g ON g.gestor_id=po.gestor_id WHERE r.linkage_run_id='$run_id' AND r.status<>'RESOLVIDO' ORDER BY po.codigo_pessoa_origem,r.pessoa_observacao_id;"
  echo
  echo 'Itens do universo fora de SCALE-PEND-* (explicam avaliados adicionais ao corpus de 1000 pendentes):'
  sql_report "SELECT ri.pessoa_observacao_id,po.codigo_pessoa_origem,g.codigo AS gestor,COALESCE(r.status,'SEM_RESULTADO') AS status,COALESCE(r.motivo,'SEM_MOTIVO') AS motivo,r.score_melhor FROM identidade.linkage_run_item ri JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=ri.pessoa_observacao_id JOIN ref.gestor g ON g.gestor_id=po.gestor_id LEFT JOIN identidade.linkage_resultado r ON r.linkage_run_id=ri.linkage_run_id AND r.pessoa_observacao_id=ri.pessoa_observacao_id WHERE ri.linkage_run_id='$run_id' AND po.codigo_pessoa_origem NOT LIKE 'SCALE-PEND-%' ORDER BY po.codigo_pessoa_origem,ri.pessoa_observacao_id;"
}

action="${1:-up}"
case "$action" in
  up) bash "$ROOT/scripts/local-db.sh" up; start_nodes true ;;
  reset) compose stop jornada-node1 jornada-node2 || true; bash "$ROOT/scripts/local-db.sh" reset; start_nodes false ;;
  down) compose down ;;
  clean) compose down -v --remove-orphans ;;
  status) compose ps ;;
  logs) compose logs -f jornada-node1 jornada-node2 jornada-nas ;;
  calibrate) calibrate ;;
  linkage) run_linkage ;;
  linkage-diagnose) diagnose_linkage ;;
  *) echo "Uso: $0 {up|reset|down|clean|status|logs|calibrate|linkage|linkage-diagnose}" >&2; exit 2 ;;
esac