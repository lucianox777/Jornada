#!/usr/bin/env bash
# Single-worker process boundary for the OPT-IN disposable Console DEV profile.
# This is not the legacy multi-process NODE entrypoint. The container runtime
# (in a subsequent, isolated Compose profile) owns restart policy.
set -euo pipefail

deny() { echo "ERRO: worker isolado recusado: $*" >&2; exit 2; }

[[ "${JORNADA_WORKER_ISOLATED_PROFILE:-}" == "true" ]] \
  || deny "opt-in explícito JORNADA_WORKER_ISOLATED_PROFILE=true obrigatório"
[[ "${JORNADA_RUNTIME_MODE:-}" == "DEV" && "${DOTNET_ENVIRONMENT:-}" == "Development" ]] \
  || deny "somente Console DEV/Development"
[[ "${JORNADA_E2E_SQL_DATABASE:-}" == "JornadaE2E" \
   && "${JORNADA_SQL_DATABASE_OVERRIDE:-}" == "JornadaE2E" ]] \
  || deny "somente banco descartável JornadaE2E"
[[ "${JORNADA_NODE_ID:-}" == "NODE1" || "${JORNADA_NODE_ID:-}" == "NODE2" ]] \
  || deny "JORNADA_NODE_ID deve ser NODE1 ou NODE2"

# Validate the ACTUAL worker connection string, not only auxiliary labels.
connection="${ConnectionStrings__Jornada:-}"
[[ -n "$connection" ]] || deny "ConnectionStrings__Jornada ausente"
normalized="${connection//[[:space:]]/}"
shopt -s nocasematch
[[ "$normalized" =~ (^|\;)Database=JornadaE2E(\;|$) ]] \
  || deny "connection string não está vinculada a JornadaE2E"
shopt -u nocasematch

[[ "$#" == 1 || "$#" == 2 ]] || deny "uso: worker-entrypoint.sh WORKER [--check]"
worker="${1:-}"
mode="${2:-}"
[[ "$mode" == "" || "$mode" == "--check" ]] || deny "opção não reconhecida"

case "$worker" in
  Processor)
    dll="/opt/jornada/apps/Jornada.Processor.Worker/Jornada.Processor.Worker.dll"
    export Processor__RunOnce=false
    ;;
  OperationsMaintenance)
    dll="/opt/jornada/apps/Jornada.Operations.Maintenance.Worker/Jornada.Operations.Maintenance.Worker.dll"
    export MaintenanceExecution__RunOnce=false
    ;;
  BronzeMaintenance)
    dll="/opt/jornada/apps/Jornada.Bronze.Maintenance.Worker/Jornada.Bronze.Maintenance.Worker.dll"
    export BronzeMaintenance__RunOnce=false
    ;;
  *) deny "worker fora da allowlist" ;;
esac

# --check is a side-effect-free validation used by the CI contract test.
if [[ "$mode" == "--check" ]]; then
  printf 'worker=%s;mode=CONTINUOUS;restart=external;database=JornadaE2E\n' "$worker"
  exit 0
fi

[[ -f "$dll" ]] || deny "executável publicado ausente"
# PID 1 must be the worker itself: never launch siblings, never wait -n.
exec dotnet "$dll"
