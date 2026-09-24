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

# Uma descoberta (rc=1) nao deve impedir a outra varredura.
# Erros de execucao (rc>1) encerram a prova sem fingir sucesso.
scan() {
  local label="$1" rc=0
  shift
  gitleaks "$@" || rc=$?
  if [[ $rc -eq 0 ]]; then
    echo "$label: OK"
  elif [[ $rc -eq 1 ]]; then
    echo "$label: ACHADOS — consulte o relatório local redigido em Solution/.local/gitleaks." >&2
  else
    echo "gitleaks falhou em '$label' com exit code $rc." >&2
  fi
  return "$rc"
}

cd "$repo_root" || exit 70
gitleaks version || exit 127

tree_rc=0
scan "Árvore corrente" dir \
  --config "$config" --redact=100 --report-format json \
  --report-path "$out_dir/current-tree.json" . || tree_rc=$?
if [[ $tree_rc -gt 1 ]]; then exit "$tree_rc"; fi

history_rc=0
if [[ $current_tree_only -eq 0 ]]; then
  scan "Histórico Git completo" git \
    --config "$config" --redact=100 --report-format json \
    --report-path "$out_dir/git-history.json" --log-opts="--all" . || history_rc=$?
  if [[ $history_rc -gt 1 ]]; then exit "$history_rc"; fi
fi

if [[ $tree_rc -eq 1 || $history_rc -eq 1 ]]; then
  echo "Gitleaks encontrou ocorrências. Classifique cada achado; não reescreva o histórico e não crie baseline/allowlist automaticamente." >&2
  exit 1
fi

echo "SECRET SCAN: OK"
