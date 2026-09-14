#!/usr/bin/env bash
set -euo pipefail

CONFIG_PATH="${JORNADA_CLUSTER_CONFIG:-/etc/jornada/Jornada.Cluster.Test.json}"
NODE_ID="${JORNADA_NODE_ID:?JORNADA_NODE_ID deve identificar NODE1/NODE2}"

[[ -f "$CONFIG_PATH" ]] || { echo "Configuração não encontrada: $CONFIG_PATH" >&2; exit 2; }

environment_name="$(jq -er '.environment' "$CONFIG_PATH")"
[[ "$environment_name" == "Test" || "$environment_name" == "Production" ]] || {
  echo "environment inválido: $environment_name" >&2
  exit 2
}

node_json="$(jq -cer --arg id "$NODE_ID" '.nodes[] | select((.id|ascii_downcase) == ($id|ascii_downcase))' "$CONFIG_PATH")" || {
  echo "Nó não encontrado na configuração: $NODE_ID" >&2
  exit 2
}

runtime_environment="Production"
[[ "$environment_name" == "Test" ]] && runtime_environment="Development"

export DOTNET_ENVIRONMENT="$runtime_environment"
export ASPNETCORE_ENVIRONMENT="$runtime_environment"
export Contracts__RepositoryRoot="/opt/jornada"
export BronzeStorage__Provider="FileSystem"
export BronzeStorage__RootPath="$(jq -er '.storage.bronzeRoot' "$CONFIG_PATH")"
export IngestionStaging__RootPath="$(jq -er '.stagingRoot' <<<"$node_json")"
export JORNADA_NODE_ID="$(jq -er '.id' <<<"$node_json")"

connection_string="${JORNADA_SQL_CONNECTION_STRING:-$(jq -er '.sql.connectionString' "$CONFIG_PATH")}" 
export ConnectionStrings__Jornada="$connection_string"

while IFS=$'\t' read -r key value; do
  [[ -z "$key" ]] && continue
  export "$key=$value"
done < <(jq -r '.runtime.environment // {} | to_entries[] | [.key, (.value|tostring)] | @tsv' "$CONFIG_PATH")

mkdir -p \
  "$BronzeStorage__RootPath" \
  "$IngestionStaging__RootPath" \
  "$(jq -er '.logsRoot' <<<"$node_json")" \
  "$(jq -er '.dataRoot' <<<"$node_json")"

declare -a child_pids=()
declare -a child_names=()

component_dll() {
  case "$1" in
    Api) echo "/opt/jornada/apps/Jornada.Api/Jornada.Api.dll" ;;
    ResultadoApi) echo "/opt/jornada/apps/Jornada.Resultado.Api/Jornada.Resultado.Api.dll" ;;
    Processor) echo "/opt/jornada/apps/Jornada.Processor.Worker/Jornada.Processor.Worker.dll" ;;
    OperationsMaintenance) echo "/opt/jornada/apps/Jornada.Operations.Maintenance.Worker/Jornada.Operations.Maintenance.Worker.dll" ;;
    BronzeMaintenance) echo "/opt/jornada/apps/Jornada.Bronze.Maintenance.Worker/Jornada.Bronze.Maintenance.Worker.dll" ;;
    LinkageParameters) echo "/opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll" ;;
    LinkageRunner) echo "/opt/jornada/apps/Jornada.Linkage.Runner/Jornada.Linkage.Runner.dll" ;;
    Integrator) echo "/opt/jornada/clients/Jornada.Integrador/Jornada.Integrador.CSharp.dll" ;;
    *) return 1 ;;
  esac
}

stop_children() {
  local signal="${1:-TERM}"
  for pid in "${child_pids[@]:-}"; do
    kill "-$signal" "$pid" 2>/dev/null || true
  done
}

shutdown() {
  echo "[$JORNADA_NODE_ID] encerrando processos..."
  stop_children TERM
  sleep 1
  stop_children KILL
  wait || true
}
trap shutdown INT TERM

api_urls="$(jq -er '.http.apiUrls' "$CONFIG_PATH")"
resultado_urls="$(jq -er '.http.resultadoUrls' "$CONFIG_PATH")"
jornada_api_base_url="$(jq -er '.http.jornadaApiBaseUrl' "$CONFIG_PATH")"

while IFS= read -r task_json; do
  enabled="$(jq -r '.enabled' <<<"$task_json")"
  [[ "$enabled" == "true" ]] || continue

  trigger_type="$(jq -r '.trigger.type' <<<"$task_json")"
  if [[ "$trigger_type" != "AtStartup" ]]; then
    echo "[$JORNADA_NODE_ID] tarefa habilitada '$trigger_type' não é iniciada pelo supervisor contínuo: $(jq -r '.name' <<<"$task_json")"
    continue
  fi

  name="$(jq -er '.name' <<<"$task_json")"
  component="$(jq -er '.component' <<<"$task_json")"
  dll="$(component_dll "$component")" || {
    echo "Componente desconhecido: $component" >&2
    exit 3
  }
  [[ -f "$dll" ]] || { echo "Executável publicado ausente: $dll" >&2; exit 3; }

  declare -a env_args=()
  while IFS=$'\t' read -r key value; do
    [[ -z "$key" ]] && continue
    env_args+=("$key=$value")
  done < <(jq -r '.environment // {} | to_entries[] | [.key, (.value|tostring)] | @tsv' <<<"$task_json")

  case "$component" in
    Api) env_args+=("ASPNETCORE_URLS=$api_urls") ;;
    ResultadoApi)
      env_args+=("ASPNETCORE_URLS=$resultado_urls")
      env_args+=("JornadaApiBaseUrl=$jornada_api_base_url")
      ;;
  esac

  mapfile -t task_args < <(jq -r '.arguments[]?' <<<"$task_json")
  workdir="$(dirname "$dll")"

  echo "[$JORNADA_NODE_ID] START $name ($component)"
  (
    cd "$workdir"
    exec env "${env_args[@]}" dotnet "$dll" "${task_args[@]}"
  ) &
  child_pids+=("$!")
  child_names+=("$name")
done < <(jq -c '.tasks[]' "$CONFIG_PATH")

if [[ "${#child_pids[@]}" -eq 0 ]]; then
  echo "Nenhuma tarefa AtStartup habilitada para $JORNADA_NODE_ID." >&2
  exit 4
fi

set +e
wait -n "${child_pids[@]}"
status=$?
set -e

echo "[$JORNADA_NODE_ID] um processo filho encerrou (exit=$status); encerrando o nó para reinício coordenado." >&2
shutdown
exit "$status"
