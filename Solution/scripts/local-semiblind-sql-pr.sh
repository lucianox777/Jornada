#!/usr/bin/env bash
# PR-only synthetic SQL component proof for #612.
# Source-sanity-policy allows DEV localhost SQL connection exceptions in local-*.sh.
# Never use against citizen data, HML/PROD, an external SQL host or a persistent DB.
set -euo pipefail
umask 077
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
[[ "$PWD" == "$ROOT" ]] || { echo 'ERRO: somente a partir de Solution.' >&2; exit 64; }
[[ "${GITHUB_EVENT_NAME:-}" == pull_request
   && "${GITHUB_REPOSITORY:-}" == lucianox777/Jornada
   && "${GITHUB_REF:-}" =~ ^refs/pull/[0-9]+/merge$
   && "${GITHUB_RUN_ID:-}" =~ ^[0-9]+$
   && "${GITHUB_SHA:-}" =~ ^[0-9a-f]{40}$ ]] || {
  echo 'ERRO: execução exclusiva na PR interna, SHA de merge obrigatório.' >&2; exit 65;
}
[[ "${JORNADA_RUNTIME_MODE:-}" == DEV
   && "${DOTNET_ENVIRONMENT:-}" == Development
   && "${ASPNETCORE_ENVIRONMENT:-}" == Development ]] || {
  echo 'ERRO: somente no perfil de Development.' >&2; exit 66;
}
[[ "${JORNADA_SQL_SA_PASSWORD:-}" == "Jornada_${GITHUB_RUN_ID}!Sql" ]] || {
  echo 'ERRO: credencial SQL efêmera desta tentativa não comprovada.' >&2; exit 67;
}
[[ -z "${JORNADA_TEST_SQL_CONNECTION:-}" ]] || {
  echo 'ERRO: conexão SQL externa/residual recusada.' >&2; exit 68;
}
readonly database="JornadaSemiblind_Test_${GITHUB_RUN_ID}"
[[ ! -e .local/semiblind-sql/semiblind.trx && ! -L .local/semiblind-sql/semiblind.trx ]] || {
  echo 'ERRO: recibo TRX preexistente.' >&2; exit 69;
}
mkdir -p .local/semiblind-sql
# Explicitly isolated to loopback + unique newly created test database.
# SQL server has only a disposable self-signed development certificate:
# the TLS trust exception is NEVER passed to HML/PROD or a configured host.
export JORNADA_TEST_SQL_CONNECTION="Server=127.0.0.1,1433;Database=${database};User Id=sa;Password=${JORNADA_SQL_SA_PASSWORD};TrustServerCertificate=true;Encrypt=false"

dotnet test tests/Jornada.Integration.Tests/Jornada.Integration.Tests.csproj \
  --configuration Release --no-restore \
  --filter 'FullyQualifiedName~BlockingProjectionSqlServerTests.PersistValidated_publishes_blocking_projection_in_same_processor_path' \
  --results-directory "$PWD/.local/semiblind-sql" \
  --logger 'trx;LogFileName=semiblind.trx'
