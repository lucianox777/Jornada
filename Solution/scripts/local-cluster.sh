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
  compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$password" -C -d JornadaLocal -W -h -1 -Q "SET NOCOUNT ON; $query" \
    | awk 'NF{last=$0} END{gsub(/^[[:space:]]+|[[:space:]]+$/, "", last); print last}'
}
sql_report() {
  local query="$1" password
  password="$(env_value JORNADA_SQL_SA_PASSWORD)"
  [[ -n "$password" ]] || { echo "JORNADA_SQL_SA_PASSWORD ausente do .env" >&2; return 2; }
  compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$password" -C -d JornadaLocal -W -s '|' -Q "SET NOCOUNT ON; $query"
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
  echo "Verificando projeção de blocking da massa sintética local..."
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
  local before count version active
  ensure_local_blocking_projection
  before="$(sql_scalar "SELECT ISNULL(MAX(versao),0) FROM identidade.modelo_linkage;")"
  echo "Calibração iniciando após modelo v$before."
  compose exec -T jornada-node2 env LinkageParameters__Operation=GENERATE_DRAFT LinkageParameters__RunOnce=true dotnet /opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll
  count="$(sql_scalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE versao>$before AND status='RASCUNHO';")"
  [[ "$count" == "1" ]] || { echo "Esperado exatamente um novo RASCUNHO; encontrados=$count" >&2; return 3; }
  version="$(sql_scalar "SELECT MAX(versao) FROM identidade.modelo_linkage WHERE versao>$before AND status='RASCUNHO';")"
  compose exec -T jornada-node2 env LinkageParameters__Operation=VALIDATE LinkageParameters__TargetVersion="$version" LinkageParameters__RunOnce=true dotnet /opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll
  compose exec -T jornada-node2 env LinkageParameters__Operation=ACTIVATE LinkageParameters__TargetVersion="$version" LinkageParameters__RunOnce=true dotnet /opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll
  active="$(sql_scalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE versao=$version AND status='ATIVO' AND ISNULL(amostra_metodo,'') <> 'SEED_DEV_FIXO_NAO_TREINADO';")"
  [[ "$active" == "1" ]] || { echo "Modelo v$version não ficou ATIVO como modelo calibrado." >&2; return 4; }
  echo "Calibração concluída: modelo calibrado v$version ATIVO."
}

run_linkage() {
  local active version
  ensure_local_blocking_projection
  active="$(sql_scalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE status='ATIVO' AND ISNULL(amostra_metodo,'') <> 'SEED_DEV_FIXO_NAO_TREINADO';")"
  [[ "$active" == "1" ]] || { echo "Linkage bloqueado: encontrados $active modelos calibrados ATIVOS. O seed sintético não libera execução. Execute primeiro '$0 calibrate'." >&2; return 5; }
  version="$(sql_scalar "SELECT TOP(1) versao FROM identidade.modelo_linkage WHERE status='ATIVO' AND ISNULL(amostra_metodo,'') <> 'SEED_DEV_FIXO_NAO_TREINADO' ORDER BY versao DESC;")"
  echo "Executando linkage com modelo calibrado ATIVO v$version."
  compose exec -T jornada-node2 dotnet /opt/jornada/apps/Jornada.Linkage.Runner/Jornada.Linkage.Runner.dll --mode ON_DEMAND --publish true --requested-by LOCAL_CLUSTER --reason manual-local-cluster
}

diagnose_linkage() {
  local run_id
  run_id="$(sql_scalar "SELECT TOP(1) CONVERT(varchar(36),linkage_run_id) FROM identidade.linkage_run WHERE status='PUBLICADO' AND tipo_run='ON_DEMAND' ORDER BY publicado_em DESC,iniciado_em DESC,linkage_run_id DESC;")"
  [[ -n "$run_id" ]] || { echo "Nenhum linkage ON_DEMAND PUBLICADO encontrado." >&2; return 6; }

  echo "Diagnóstico do último linkage ON_DEMAND PUBLICADO: $run_id"
  echo
  echo 'Resumo do run:'
  sql_report "SELECT CONVERT(varchar(36),linkage_run_id) AS run_id,modelo_versao,tipo_run,status,registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,sem_candidato_no_bloco,publicado_em FROM identidade.linkage_run WHERE linkage_run_id='$run_id';"
  echo
  echo 'Não resolvidos/conflitos por motivo:'
  sql_report "SELECT r.status,COALESCE(r.motivo,'SEM_MOTIVO') AS motivo,COUNT_BIG(*) AS qtd,MIN(r.score_melhor) AS score_min,AVG(r.score_melhor) AS score_medio,MAX(r.score_melhor) AS score_max FROM identidade.linkage_resultado r WHERE r.linkage_run_id='$run_id' AND r.status<>'RESOLVIDO' GROUP BY r.status,r.motivo ORDER BY qtd DESC,r.status,r.motivo;"
  echo
  echo 'Detalhe dos não resolvidos/conflitos:'
  sql_report "SELECT r.pessoa_observacao_id,po.codigo_pessoa_origem,g.codigo AS gestor,r.status,COALESCE(r.motivo,'SEM_MOTIVO') AS motivo,r.score_melhor,r.score_segundo,r.margem,CONVERT(varchar(36),r.melhor_candidato_uuid) AS melhor_candidato_uuid,CONVERT(varchar(36),r.segundo_candidato_uuid) AS segundo_candidato_uuid FROM identidade.linkage_resultado r JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id JOIN ref.gestor g ON g.gestor_id=po.gestor_id WHERE r.linkage_run_id='$run_id' AND r.status<>'RESOLVIDO' ORDER BY po.codigo_pessoa_origem,r.pessoa_observacao_id;"
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