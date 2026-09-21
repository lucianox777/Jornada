#!/usr/bin/env bash
set -uo pipefail

current_tree_only=0
if [[ "${1:-}" == "--current-tree-only" ]]; then
  current_tree_only=1
elif [[ $# -gt 0 ]]; then
  echo "uso: $0 [--current-tree-only]" >&2
  exit 64
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
out_dir="$repo_root/Solution/.local/gitleaks"
config="$repo_root/.gitleaks.toml"

if ! command -v gitleaks >/dev/null 2>&1; then
  echo "gitleaks não encontrado no PATH. Instale uma versão atual antes da varredura." >&2
  exit 127
fi

mkdir -p "$out_dir"

scan() {
  local label="$1"
  shift
  set +e
  gitleaks "$@"
  local rc=$?
  set -e
  if [[ $rc -eq 0 ]]; then
    echo "$label: OK"
    return 0
  fi
  if [[ $rc -eq 1 ]]; then
    echo "$label: ACHADOS — consulte o relatório local redigido em Solution/.local/gitleaks." >&2
    return 1
  fi
  echo "gitleaks falhou em '$label' com exit code $rc." >&2
  exit "$rc"
}

cd "$repo_root"
gitleaks version

set +e
scan "Árvore corrente" dir   --config "$config"   --redact=100   --report-format json   --report-path "$out_dir/current-tree.json"   .
tree_rc=$?

history_rc=0
if [[ $current_tree_only -eq 0 ]]; then
  scan "Histórico Git completo" git     --config "$config"     --redact=100     --report-format json     --report-path "$out_dir/git-history.json"     --log-opts="--all"     .
  history_rc=$?
fi
set -e

if [[ $tree_rc -ne 0 || $history_rc -ne 0 ]]; then
  echo "Gitleaks encontrou ocorrências. Classifique cada achado; não reescreva o histórico e não crie baseline/allowlist automaticamente." >&2
  exit 1
fi

echo "SECRET SCAN: OK"
