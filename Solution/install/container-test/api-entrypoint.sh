#!/usr/bin/env bash
# C3.2b: a single API process, only in the explicit disposable DEV E2E project.
# No sibling workers, no shared NODE shutdown; external restart supervision.
set -euo pipefail

deny() { echo "ERRO: API isolada recusada: $*" >&2; exit 2; }

[[ "$#" == 1 || "$#" == 2 ]] || deny "uso: api-entrypoint.sh Api|ResultadoApi [--check]"
component="${1:-}"
mode="${2:-}"
[[ "$mode" == "" || "$mode" == "--check" ]] || deny "opção inválida"

[[ "${JORNADA_WORKER_ISOLATED_PROFILE:-}" == "true" \
    && "${JORNADA_RUNTIME_MODE:-}" == "DEV" \
    && "${DOTNET_ENVIRONMENT:-}" == "Development" \
    && "${ASPNETCORE_ENVIRONMENT:-}" == "Development" ]] \
  || deny "exige opt-in explícito DEV descartável"
[[ "${JORNADA_E2E_SQL_DATABASE:-}" == "JornadaE2E" \
    && "${JORNADA_SQL_DATABASE_OVERRIDE:-}" == "JornadaE2E" ]] \
  || deny "banco de teste deve ser exatamente JornadaE2E"

connection="${ConnectionStrings__Jornada:-}"
[[ -n "$connection" ]] || deny "connection string ausente"
normalized="${connection//[[:space:]]/}"
shopt -s nocasematch
[[ "$normalized" =~ (^|\;)Server=sqlserver,1433(\;|$) \
    && "$normalized" =~ (^|\;)Database=JornadaE2E(\;|$) ]] \
  || deny "Server/Database devem ser o SQL privado do projeto JornadaE2E"
shopt -u nocasematch

case "$component" in
  Api)
    [[ "${JORNADA_NODE_ID:-}" == "NODE1" \
        && "${ASPNETCORE_URLS:-}" == "http://0.0.0.0:5080" ]] \
      || deny "configuração do processo Api inválida"
    dll="/opt/jornada/apps/Jornada.Api/Jornada.Api.dll"
    ;;
  ResultadoApi)
    [[ "${JORNADA_NODE_ID:-}" == "NODE2" \
        && "${ASPNETCORE_URLS:-}" == "http://0.0.0.0:5081" \
        && "${JornadaApiBaseUrl:-}" == "http://api:5080" ]] \
      || deny "ResultadoApi exige URL interna da API isolada"
    dll="/opt/jornada/apps/Jornada.Resultado.Api/Jornada.Resultado.Api.dll"
    ;;
  *) deny "componente fora da allowlist Api|ResultadoApi" ;;
esac

# Validation mode must not touch containers, SQL, dotnet or the disk.
if [[ "$mode" == "--check" ]]; then
  printf 'component=%s;mode=ISOLATED_API;database=JornadaE2E;restart=external\n' "$component"
  exit 0
fi

[[ -f "$dll" ]] || deny "executável publicado ausente"
# Replace the shell: PID 1 is exclusively this API executable.
exec dotnet "$dll"
