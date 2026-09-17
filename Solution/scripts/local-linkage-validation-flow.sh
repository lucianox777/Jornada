#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
CLUSTER="$SCRIPT_DIR/local-cluster.sh"
VALIDATION="$SCRIPT_DIR/local-linkage-validation.sh"
ENV_FILE="$ROOT/.env"

cd "$ROOT"
echo "# cd '$ROOT'"

echo "# ./scripts/local-cluster.sh up"
bash "$CLUSTER" up

if [[ ! -f "$ENV_FILE" ]]; then
  echo ".env ausente em $ENV_FILE. Execute o bootstrap local antes de continuar." >&2
  exit 1
fi

SQL_PASSWORD="$(awk -F= '$1=="JORNADA_SQL_SA_PASSWORD"{sub(/^[^=]*=/,""); print; exit}' "$ENV_FILE")"
if [[ -z "$SQL_PASSWORD" ]]; then
  echo 'JORNADA_SQL_SA_PASSWORD ausente no .env.' >&2
  exit 1
fi

sql_scalar() {
  local query="$1"
  echo "# docker compose --env-file $ENV_FILE exec -T -e SQLCMDPASSWORD=<redacted> sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d JornadaLocal -W -h -1 -Q '<query>'"
  docker compose --env-file "$ENV_FILE" exec -T -e "SQLCMDPASSWORD=$SQL_PASSWORD" sqlserver /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -C -b -d JornadaLocal -W -h -1 -Q "$query" \
    | tr -d '\r' \
    | awk 'NF { last=$0 } END { gsub(/^[ \t]+|[ \t]+$/, "", last); print last }'
}

active_model="$(sql_scalar "SET NOCOUNT ON; SELECT COUNT(*) FROM identidade.modelo_linkage WHERE status='ATIVO' AND ISNULL(amostra_metodo,'')<>'SEED_DEV_FIXO_NAO_TREINADO';")"

if [[ "$active_model" == "0" ]]; then
  validation_rows="$(sql_scalar "SET NOCOUNT ON; SELECT COUNT(*) FROM silver.pessoa_observacao WHERE codigo_pessoa_origem LIKE 'SCALE-VAL-%';")"
  if [[ "$validation_rows" != "0" ]]; then
    echo 'Há corpus SCALE-VAL persistido, mas não existe modelo calibrado ATIVO. Não é seguro recalibrar sobre o corpus de validação. Use um ambiente limpo ou restaure o modelo esperado.' >&2
    exit 1
  fi

  echo "# ./scripts/local-cluster.sh calibrate"
  bash "$CLUSTER" calibrate
else
  echo 'Modelo calibrado ATIVO já existe; preservando-o para manter a validação independente.'
fi

echo "# ./scripts/local-linkage-validation.sh"
bash "$VALIDATION"
