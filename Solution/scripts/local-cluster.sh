#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$ROOT/.env"
EXAMPLE="$ROOT/.env.example"

command -v docker >/dev/null 2>&1 || { echo "Docker não encontrado no PATH." >&2; exit 2; }
[[ -f "$ENV_FILE" ]] || cp "$EXAMPLE" "$ENV_FILE"

compose() {
  (cd "$ROOT" && docker compose --env-file "$ENV_FILE" "$@")
}

wait_node() {
  local name="$1" url="$2"
  for _ in $(seq 1 120); do
    if curl -fsS "$url" >/dev/null 2>&1; then
      echo "$name ready: $url"
      return 0
    fi
    sleep 1
  done
  compose logs --tail 120 "$name" || true
  echo "$name não ficou ready: $url" >&2
  return 1
}

start_nodes() {
  local build="${1:-false}"
  if [[ "$build" == "true" ]]; then
    compose up -d --build jornada-node1 jornada-node2
  else
    compose up -d jornada-node1 jornada-node2
  fi
  wait_node jornada-node1 http://127.0.0.1:5080/health/ready
  wait_node jornada-node2 http://127.0.0.1:5180/health/ready
  echo "Cluster local pronto: NODE1=http://127.0.0.1:5080 NODE2=http://127.0.0.1:5180 SQL=localhost:14333"
}

action="${1:-up}"
case "$action" in
  up)
    bash "$ROOT/scripts/local-db.sh" up
    start_nodes true
    ;;
  reset)
    compose stop jornada-node1 jornada-node2 || true
    bash "$ROOT/scripts/local-db.sh" reset
    start_nodes false
    ;;
  down)
    compose down
    ;;
  clean)
    compose down -v --remove-orphans
    ;;
  status)
    compose ps
    ;;
  logs)
    compose logs -f jornada-node1 jornada-node2
    ;;
  *)
    echo "Uso: $0 {up|reset|down|clean|status|logs}" >&2
    exit 2
    ;;
esac
