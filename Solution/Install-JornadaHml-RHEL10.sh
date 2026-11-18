#!/usr/bin/env bash
# HML single-node RHEL 10 x64 — adaptação não destrutiva do instalador local Windows.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"
ENV_FILE="$ROOT/.env"
OVERRIDE="$ROOT/install/linux-hml/compose.single-node.yml"
CONFIG="$ROOT/install/linux-hml/Jornada.Cluster.Hml.SingleNode.json"
ACTION="${1:-install}"
compose() { docker compose --env-file "$ENV_FILE" -f "$ROOT/docker-compose.yml" -f "$OVERRIDE" "$@"; }
die() { echo "ERRO: $*" >&2; exit 2; }
need() { command -v "$1" >/dev/null 2>&1 || die "Comando obrigatório ausente: $1"; }
check_platform() {
  [[ "$(uname -s)" == Linux ]] || die "Este instalador exige Linux."
  [[ "$(uname -m)" == x86_64 ]] || die "Arquitetura não suportada: $(uname -m); esperado x86_64."
  [[ -r /etc/os-release ]] || die "Não foi possível identificar o sistema operacional."
  # shellcheck disable=SC1091
  . /etc/os-release
  [[ "${ID:-}" == rhel && "${VERSION_ID%%.*}" == 10 ]] || die "Exigido RHEL 10; encontrado ${PRETTY_NAME:-desconhecido}."
}
preflight() {
  check_platform
  for c in docker dotnet python3 curl; do need "$c"; done
  docker info >/dev/null 2>&1 || die "Docker Engine indisponível ou sem permissão."
  docker compose version >/dev/null || die "Docker Compose plugin indisponível."
  [[ -f "$ROOT/Jornada.sln" && -f "$OVERRIDE" ]] || die "Arquivos da Solution ausentes."
  [[ "$(dotnet --version | cut -d. -f1)" == 10 ]] || die "Exigido .NET SDK 10."
}
prepare() {
  if [[ ! -f "$ENV_FILE" ]]; then
    python3 "$ROOT/scripts/local_env_bootstrap.py" --check-docker-volume
  fi
  [[ -s "$ENV_FILE" ]] || die ".env ausente ou vazio."
  # O schema e o bundle são copiados do perfil de teste existente; apenas o nó muda.
  python3 - "$ROOT/install/windows-production/Jornada.Cluster.Test.json" "$CONFIG" <<'PY'
import json, pathlib, sys
src, dst = map(pathlib.Path, sys.argv[1:])
data = json.loads(src.read_text(encoding="utf-8-sig"))
nodes = [node for node in data["nodes"] if node["id"] == "NODE1"]
if len(nodes) != 1:
    raise SystemExit("Configuração de origem sem NODE1 único")
data["nodes"] = nodes
data["environment"] = "Test"
dst.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
PY
  compose config --quiet
}
wait_http() {
  local url="$1" i
  for i in $(seq 1 120); do
    if curl --fail --silent --show-error --max-time 3 "$url" >/dev/null 2>&1; then return 0; fi
    sleep 2
  done
  compose logs --tail 100 jornada-node1 >&2 || true
  die "Serviço indisponível: $url"
}
install() {
  echo "[1/9] Plataforma e dependências"; preflight
  echo "[2/9] Configuração de nó único"; prepare
  echo "[3/9] Restore bloqueado"; dotnet restore Jornada.sln --locked-mode
  echo "[4/9] Build Release"; dotnet build Jornada.sln --configuration Release --no-restore -warnaserror
  echo "[5/9] Imagem NODE1"; compose build jornada-node1
  echo "[6/9] SQL e bootstrap idempotente (harness local DEV/HML)"
  # local-db.sh respeita COMPOSE_FILE, incluindo override single-node.
  COMPOSE_FILE="$ROOT/docker-compose.yml:$OVERRIDE" bash "$ROOT/scripts/local-db.sh" up
  echo "[7/9] Inicialização NODE1 e referência IBGE"
  compose up -d --no-deps jornada-node1
  # O bootstrap IBGE existente roda uma vez e não é serviço residente.
  compose run --rm jornada-reference-bootstrap
  echo "[8/9] Saúde NODE1"; wait_http "http://127.0.0.1:5080/health/ready"
  echo "[9/9] Monitor"; wait_http "http://127.0.0.1:5080/monitor"
  if compose ps --status running --services | grep -qx 'jornada-node2'; then
    die "NODE2 inesperadamente ativo: interrompa o cluster anterior antes da homologação single-node."
  fi
  echo "HML single-node iniciada: http://127.0.0.1:5080/monitor"
  echo "SQL: porta configurada em .env; persistência em volumes Docker."
}
case "$ACTION" in
  install) install ;;
  preflight) preflight; prepare; echo "Preflight OK" ;;
  status|ps) preflight; prepare; compose ps ;;
  logs) preflight; prepare; compose logs --tail 200 jornada-node1 sqlserver ;;
  down) preflight; prepare; compose stop jornada-node1 sqlserver ;;
  *) die "Uso: $0 {install|preflight|status|logs|down}" ;;
esac
