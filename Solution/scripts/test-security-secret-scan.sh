#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
scanner="$script_dir/security-secret-scan.sh"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp/repo/Solution/scripts" "$tmp/bin"
cp "$scanner" "$tmp/repo/Solution/scripts/security-secret-scan.sh"
printf '[extend]\nuseDefault = true\n' > "$tmp/repo/.gitleaks.toml"
cat > "$tmp/bin/gitleaks" <<'MOCK'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$MOCK_LOG"
case "${1:-}" in
  version) printf '8.30.1\n'; exit 0 ;;
  dir)
    if [[ "${MOCK_TREE_WRITE_REPORT:-1}" == 1 ]]; then printf '[]\n' > "$MOCK_REPORT_DIR/current-tree.json"; fi
    exit "${MOCK_TREE_EXIT:-0}" ;;
  git)
    if [[ "${MOCK_HISTORY_WRITE_REPORT:-1}" == 1 ]]; then printf '[]\n' > "$MOCK_REPORT_DIR/git-history.json"; fi
    exit "${MOCK_HISTORY_EXIT:-0}" ;;
  *) exit 99 ;;
esac
MOCK
chmod +x "$tmp/bin/gitleaks"
export MOCK_LOG="$tmp/invocations.log"
export MOCK_REPORT_DIR="$tmp/repo/Solution/.local/gitleaks"
export PATH="$tmp/bin:$PATH"

check() {
  local name="$1" tree_rc="$2" history_rc="$3" only="$4" expected_rc="$5" expected_git="$6"
  local tree_report="${7:-1}" history_report="${8:-1}"
  : > "$MOCK_LOG"
  export MOCK_TREE_EXIT="$tree_rc" MOCK_HISTORY_EXIT="$history_rc"
  export MOCK_TREE_WRITE_REPORT="$tree_report" MOCK_HISTORY_WRITE_REPORT="$history_report"
  local actual_rc=0
  local -a args=()
  if [[ "$only" == 1 ]]; then args+=(--current-tree-only); fi
  "$tmp/repo/Solution/scripts/security-secret-scan.sh" "${args[@]}" > "$tmp/output.log" 2>&1 || actual_rc=$?
  if [[ "$actual_rc" -ne "$expected_rc" ]]; then
    echo "FALHA $name: rc=$actual_rc; esperado=$expected_rc" >&2
    cat "$tmp/output.log" >&2
    exit 1
  fi
  if [[ $(grep -c '^dir ' "$MOCK_LOG") -ne 1 ]]; then
    echo "FALHA $name: tree nao foi executada exatamente uma vez" >&2
    exit 1
  fi
  local git_count
  git_count=$(grep -c '^git ' "$MOCK_LOG" || true)
  if [[ "$git_count" -ne "$expected_git" ]]; then
    echo "FALHA $name: historico=$git_count; esperado=$expected_git" >&2
    exit 1
  fi
  if ! grep -q -- '--redact=100' "$MOCK_LOG"; then
    echo "FALHA $name: ausente redacao completa" >&2
    exit 1
  fi
  if [[ "$expected_git" -eq 1 ]] && ! grep -q -- '--log-opts=--all' "$MOCK_LOG"; then
    echo "FALHA $name: historico nao percorre todos os refs" >&2
    exit 1
  fi
  echo "PASS: $name"
}

check 'sem achados' 0 0 0 0 1
check 'achados na arvore nao pulam historico' 1 0 0 1 1
check 'achados no historico falham' 0 1 0 1 1
check 'erro da ferramenta aborta sem sucesso' 2 0 0 2 0
check 'modo current-tree-only' 1 0 1 1 0
# An empty report left by a prior scan must not mask the absence of a new report.
mkdir -p "$MOCK_REPORT_DIR"
printf '[]\n' > "$MOCK_REPORT_DIR/current-tree.json"
printf '[]\n' > "$MOCK_REPORT_DIR/git-history.json"
check 'missing current report refuses stale evidence' 0 0 0 70 0 0 1
check 'missing history report refuses stale evidence' 0 0 0 70 1 1 0
