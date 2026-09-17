#!/usr/bin/env bash
set -euo pipefail

PAIR_COUNT="${1:-250000}"
SEED="${2:-20260917}"

if ! [[ "$PAIR_COUNT" =~ ^[0-9]+$ ]] || (( PAIR_COUNT < 10000 || PAIR_COUNT > 5000000 )); then
  echo "PairCount deve estar entre 10000 e 5000000." >&2
  exit 2
fi
if ! [[ "$SEED" =~ ^-?[0-9]+$ ]]; then
  echo "Seed deve ser inteiro." >&2
  exit 2
fi

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$ROOT/.env"
OUT_DIR="$ROOT/.local/calibrador-ibge-u"
REPORT_PATH="$OUT_DIR/ibge-u-bootstrap.json"
CONTAINER_REPORT="/tmp/jornada-ibge-u-bootstrap.json"

cd "$ROOT"
echo "# cd '$ROOT'"

echo "# ./scripts/local-cluster.sh up"
bash "$SCRIPT_DIR/local-cluster.sh" up

mkdir -p "$OUT_DIR"

echo "# docker compose --env-file '$ENV_FILE' exec -T jornada-node2 env LinkageParameters__Operation=REPORT_IBGE_U_BOOTSTRAP LinkageParameters__IbgeUBootstrap__Seed=$SEED LinkageParameters__IbgeUBootstrap__PairCount=$PAIR_COUNT LinkageParameters__IbgeUBootstrap__OutputPath=$CONTAINER_REPORT dotnet /opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll"
docker compose --env-file "$ENV_FILE" exec -T jornada-node2 env   "LinkageParameters__Operation=REPORT_IBGE_U_BOOTSTRAP"   "LinkageParameters__IbgeUBootstrap__Seed=$SEED"   "LinkageParameters__IbgeUBootstrap__PairCount=$PAIR_COUNT"   "LinkageParameters__IbgeUBootstrap__OutputPath=$CONTAINER_REPORT"   dotnet /opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll

echo "# docker compose --env-file '$ENV_FILE' cp jornada-node2:$CONTAINER_REPORT '$REPORT_PATH'"
docker compose --env-file "$ENV_FILE" cp "jornada-node2:$CONTAINER_REPORT" "$REPORT_PATH"

echo "IBGE NOMINAL U BOOTSTRAP REPORT: OK"
echo "Relatório: $REPORT_PATH"
