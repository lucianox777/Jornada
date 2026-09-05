#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="tests/Jornada.Integration.Tests/Jornada.Integration.Tests.csproj"
EVIDENCE_DIR="$ROOT/.local/fabric-sql-compatibility"
LOCAL_CONNECTION_FILE="$EVIDENCE_DIR/connection.txt"
TRX="$EVIDENCE_DIR/fabric-integration.trx"
SUMMARY="$EVIDENCE_DIR/summary.json"
DEVICE_CODE_FILE="$EVIDENCE_DIR/device-code.txt"

connection="${1:-}"
if [[ -z "$connection" && -f "$LOCAL_CONNECTION_FILE" ]]; then
  connection="$(cat "$LOCAL_CONNECTION_FILE")"
fi
if [[ -z "$connection" ]]; then
  connection="${JORNADA_FABRIC_SQL_CONNECTION:-}"
fi
if [[ -z "$connection" ]]; then
  cat >&2 <<MSG
Connection string Fabric não encontrada. O Fabric não é requisito para a validação local.
Para homologação Fabric, passe a connection string como primeiro argumento, defina
JORNADA_FABRIC_SQL_CONNECTION ou crie localmente:
  $LOCAL_CONNECTION_FILE
MSG
  exit 2
fi

command -v dotnet >/dev/null 2>&1 || { echo '.NET SDK 8 é necessário.' >&2; exit 2; }

mkdir -p "$EVIDENCE_DIR"
rm -f "$TRX" "$SUMMARY" "$DEVICE_CODE_FILE"

export JORNADA_FABRIC_SQL_CONNECTION="$connection"
export JORNADA_TEST_SQL_CONNECTION="$connection"
export JORNADA_TEST_SQL_USE_EXISTING_DATABASE=true
export JORNADA_TEST_SQL_RESET_EXISTING_DATABASE=true
export JORNADA_TEST_SQL_TARGET=FABRIC_SQL_DATABASE
export JORNADA_FABRIC_DEVICE_CODE_FILE="$DEVICE_CODE_FILE"

cd "$ROOT"

printf '%s\n' '==================================================='
printf '%s\n' 'JORNADA - SQL DATABASE IN MICROSOFT FABRIC'
printf '%s\n' 'Autenticação: Microsoft Entra Device Code Flow'
printf '%s\n' 'Banco Test/Dev/Local: reset automático antes da suíte'
printf '%s\n' 'Python: NÃO utilizado'
printf '%s\n' '==================================================='

printf '%s\n' 'Fabric SQL: restore Solution --locked-mode'
dotnet restore Jornada.sln --locked-mode
printf '%s\n' 'Fabric SQL: restore Integration --locked-mode'
dotnet restore "$PROJECT" --locked-mode
printf '%s\n' 'Fabric SQL: build Solution Release'
dotnet build Jornada.sln --configuration Release --no-restore -warnaserror
printf '%s\n' 'Fabric SQL: build Integration Release'
dotnet build "$PROJECT" --configuration Release --no-restore -warnaserror

printf '%s\n' 'Fabric SQL: Integration sem skips'
set +e
dotnet test "$PROJECT" --configuration Release --no-build --logger "trx;LogFileName=$TRX" &
test_pid=$!
set -e

device_code_shown=false
while kill -0 "$test_pid" 2>/dev/null; do
  if [[ "$device_code_shown" == false && -s "$DEVICE_CODE_FILE" ]]; then
    printf '\n%s\n' '==================================================='
    printf '%s\n' 'MICROSOFT ENTRA - DEVICE CODE'
    cat "$DEVICE_CODE_FILE"
    printf '\n%s\n' 'Conclua o login com a identidade autorizada no banco Fabric de compatibilidade.'
    printf '%s\n' 'O teste continuará automaticamente após a autenticação.'
    printf '%s\n\n' '==================================================='
    device_code_shown=true

    url="$(grep -Eo 'https?://[^[:space:]]+' "$DEVICE_CODE_FILE" | head -n1 || true)"
    if [[ -n "$url" ]]; then
      if command -v xdg-open >/dev/null 2>&1; then xdg-open "$url" >/dev/null 2>&1 || true
      elif command -v open >/dev/null 2>&1; then open "$url" >/dev/null 2>&1 || true
      fi
    fi
  fi
  sleep 0.25
done

set +e
wait "$test_pid"
test_exit=$?
set -e

[[ -f "$TRX" ]] || { echo "TRX não foi gerado: $TRX" >&2; exit 1; }
counters="$(grep -m1 '<Counters ' "$TRX" || true)"
[[ -n "$counters" ]] || { echo 'TRX inválido: Counters não encontrado.' >&2; exit 1; }

attr() {
  local name="$1"
  printf '%s' "$counters" | sed -nE "s/.* ${name}=\"([0-9]+)\".*/\1/p"
}

total="$(attr total)"
executed="$(attr executed)"
passed="$(attr passed)"
failed="$(attr failed)"
not_executed="$(attr notExecuted)"

if [[ -z "$total" || -z "$executed" || -z "$passed" || -z "$failed" || -z "$not_executed" ]]; then
  echo 'TRX inválido: não foi possível ler os contadores.' >&2
  exit 1
fi

printf '{\n  "target": "FABRIC_SQL_DATABASE",\n  "total": %s,\n  "executed": %s,\n  "passed": %s,\n  "failed": %s,\n  "notExecuted": %s,\n  "trx": "%s"\n}\n' \
  "$total" "$executed" "$passed" "$failed" "$not_executed" "${TRX//\\/\\\\}" > "$SUMMARY"

printf 'Fabric Integration: total=%s, passed=%s, failed=%s, notExecuted=%s\n' \
  "$total" "$passed" "$failed" "$not_executed"

if [[ "$total" -lt 1 || "$executed" -ne "$total" || "$passed" -ne "$total" || "$failed" -ne 0 || "$not_executed" -ne 0 ]]; then
  echo 'A suíte Fabric não terminou com 100% dos testes executados e aprovados.' >&2
  exit 1
fi

if [[ "$test_exit" -ne 0 ]]; then
  echo "dotnet test retornou exit code $test_exit apesar do TRX aprovado; revise $TRX" >&2
  exit "$test_exit"
fi

printf '%s\n' '==================================================='
printf '%s\n' 'FABRIC SQL COMPATIBILITY: OK'
printf 'Evidência: %s\n' "$TRX"
printf 'Resumo:    %s\n' "$SUMMARY"
printf '%s\n' '==================================================='
