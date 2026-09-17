#!/usr/bin/env bash
set -euo pipefail

CONFIG_PATH="${JORNADA_CLUSTER_CONFIG:-/etc/jornada/Jornada.Cluster.Test.json}"
BUNDLE_PATH="/opt/jornada/config/release/configuration-bundle.json"
NODE_ID="${JORNADA_NODE_ID:?JORNADA_NODE_ID deve identificar NODE1/NODE2}"

[[ -f "$CONFIG_PATH" ]] || { echo "Configuração não encontrada: $CONFIG_PATH" >&2; exit 2; }
[[ -f "$BUNDLE_PATH" ]] || { echo "Bundle de configuração não encontrado: $BUNDLE_PATH" >&2; exit 2; }

environment_name="$(jq -er '.environment' "$CONFIG_PATH")"
[[ "$environment_name" == "Test" || "$environment_name" == "Production" ]] || {
  echo "environment inválido: $environment_name" >&2
  exit 2
}

config_bundle_version="$(jq -er '.configurationBundleVersion' "$CONFIG_PATH")"
config_solution_schema="$(jq -er '.solutionSchema' "$CONFIG_PATH")"
expected_bundle_version="$(jq -er '.bundleVersion' "$BUNDLE_PATH")"
expected_solution_schema="$(jq -er '.solutionSchema' "$BUNDLE_PATH")"
expected_cluster_schema="$(jq -er '.clusterConfigSchemaVersion' "$BUNDLE_PATH")"
actual_cluster_schema="$(jq -er '.schemaVersion' "$CONFIG_PATH")"

[[ "$config_bundle_version" == "$expected_bundle_version" ]] || {
  echo "configurationBundleVersion divergente: config=$config_bundle_version bundle=$expected_bundle_version" >&2
  exit 2
}
[[ "$config_solution_schema" == "$expected_solution_schema" ]] || {
  echo "solutionSchema divergente: config=$config_solution_schema bundle=$expected_solution_schema" >&2
  exit 2
}
[[ "$actual_cluster_schema" == "$expected_cluster_schema" ]] || {
  echo "schemaVersion do cluster incompatível: config=$actual_cluster_schema bundle=$expected_cluster_schema" >&2
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
export JORNADA_CONFIGURATION_BUNDLE_VERSION="$config_bundle_version"
export JORNADA_SOLUTION_SCHEMA_VERSION="$config_solution_schema"

if [[ "$environment_name" == "Test" ]]; then
  # O monitor local usa a mesma credencial sintética do perfil Test e continua
  # exercitando a autenticação/autorização real de /api/v1/monitor/status.
  export OperationalMonitor__LocalAutoGestor="$(jq -er '.integrator.gestor' "$CONFIG_PATH")"
  export OperationalMonitor__LocalAutoAccessKey="$(jq -er '.integrator.accessKey' "$CONFIG_PATH")"
fi

connection_string="${JORNADA_SQL_CONNECTION_STRING:-$(jq -er '.sql.connectionString' "$CONFIG_PATH")}" 
export ConnectionStrings__Jornada="$connection_string"

while IFS=$'\t' read -r key value; do
  [[ -z "$key" ]] && continue
  export "$key=$value"
done < <(jq -r '.runtime.environment // {} | to_entries[] | [.key, (.value|tostring)] | @tsv' "$CONFIG_PATH")

# A referência IBGE é dado de referência do ambiente, não uma etapa conceitual da
# calibração. NODE2 garante sua materialização antes de anunciar os processos
# residentes. A operação ENSURE faz apenas uma consulta quando o snapshot já existe;
# a carga completa ocorre somente em banco novo ou explicitamente recriado.
if [[ "$JORNADA_NODE_ID" == "NODE2" ]]; then
  parameters_dll="/opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll"
  [[ -f "$parameters_dll" ]] || { echo "Linkage Parameters publicado ausente: $parameters_dll" >&2; exit 3; }
  echo "[$JORNADA_NODE_ID] garantindo referência IBGE 2022 canônica antes de iniciar os processos residentes..."
  (
    cd "$(dirname "$parameters_dll")"
    exec env \
      LinkageParameters__Operation=ENSURE_NAME_FREQUENCY_SNAPSHOT \
      LinkageParameters__RunOnce=true \
      dotnet "$parameters_dll"
  )
  echo "[$JORNADA_NODE_ID] referência IBGE 2022 pronta."
fi

mkdir -p \
  "$BronzeStorage__RootPath" \
  "$IngestionStaging__RootPath" \
  "$(jq -er '.logsRoot' <<<"$node_json")" \
  "$(jq -er '.dataRoot' <<<"$node_json")"

echo "[$JORNADA_NODE_ID] config bundle=$JORNADA_CONFIGURATION_BUNDLE_VERSION schema=$JORNADA_SOLUTION_SCHEMA_VERSION"

declare -a child_pids=()
declare -a child_names=()

component_dll() {
  case "$1" in
    Api) echo "/opt/jornada/apps/Jornada.Api/Jornada.Api.dll" ;;
    ResultadoApi) echo "/opt/jornada/apps/Jornada.Resultado.Api/Jornada.Resultado.Api.dll" ;;
    Processor) echo "/opt/jornada/apps/Jornada.Processor.Worker/Jornada.Processor.Worker.dll" ;;
    OperationsMaintenance) echo "/opt/jornada/apps/Jornada.Operations.Maintenance.Worker/Jornada.Operations.Maintenance.Worker.dll" ;;
    BronzeMaintenance) echo "/opt/jornada/apps/Jornada.Bronze.Maintenance.Worker/Jornada.Bronze.Maintenance.Worker.dll" ;;
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
    echo "[$JORNADA_NODE_ID] tarefa não residente ignorada pelo supervisor: $(jq -r '.name' <<<"$task_json")"
    continue
  fi

  name="$(jq -er '.name' <<<"$task_json")"
  component="$(jq -er '.component' <<<"$task_json")"
  dll="$(component_dll "$component")" || {
    echo "Componente residente desconhecido: $component" >&2
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
