#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
REPO="${1:-$ROOT}"
OUT_BUNDLE="${2:-$ROOT/Solution/.local/release-source/Jornada_Source.bundle}"
OUT_PROV="${3:-$ROOT/Solution/.local/release-source/SOURCE_PROVENANCE.json}"
INFO="${4:-$ROOT/RELEASE_INFO.txt}"
command -v git >/dev/null 2>&1 || { echo "ERRO: git não encontrado." >&2; exit 2; }
command -v python3 >/dev/null 2>&1 || { echo "ERRO: python3 não encontrado." >&2; exit 2; }
CURRENT_TAG="$(awk -F= '$1=="source_git_tag"{print substr($0,index($0,"=")+1)}' "$INFO")"
PREV_TAG="$(awk -F= '$1=="source_git_predecessor_tag"{print substr($0,index($0,"=")+1)}' "$INFO")"
[[ -n "$CURRENT_TAG" && -n "$PREV_TAG" ]] || { echo "ERRO: tags Git ausentes em RELEASE_INFO." >&2; exit 3; }
python3 "$ROOT/Solution/scripts/release-source-gate.py" --repo "$REPO" --release-info "$INFO" --expected-tag "$CURRENT_TAG" --require-clean
mkdir -p "$(dirname "$OUT_BUNDLE")" "$(dirname "$OUT_PROV")"
git -C "$REPO" bundle create "$OUT_BUNDLE" "refs/tags/$PREV_TAG" "refs/tags/$CURRENT_TAG"
python3 - "$REPO" "$OUT_BUNDLE" "$OUT_PROV" "$INFO" "$PREV_TAG" "$CURRENT_TAG" <<'PY'
import hashlib,json,subprocess,sys
from pathlib import Path
repo=Path(sys.argv[1]); bundle=Path(sys.argv[2]); out=Path(sys.argv[3]); info=Path(sys.argv[4]); prev=sys.argv[5]; current=sys.argv[6]
def git(*a): return subprocess.check_output(['git','-C',str(repo),*a],text=True).strip()
def obj(tag):
    commit=git('rev-parse',f'refs/tags/{tag}^{{commit}}')
    tree=git('rev-parse',f'{commit}^{{tree}}')
    count=len([x for x in git('ls-tree','-r','--name-only',commit).splitlines() if x.strip()])
    return {'tag':tag,'commit':commit,'tree':tree,'trackedFileCount':count}
h=hashlib.sha256(bundle.read_bytes()).hexdigest()
rows={}
for line in info.read_text(encoding='utf-8').splitlines():
    if '=' in line and not line.lstrip().startswith('#'):
        k,v=line.split('=',1); rows[k.strip()]=v.strip()
data={'schemaVersion':1,'release':rows.get('release'),'baseNormativa':rows.get('base_normativa'),'solutionEngenharia':rows.get('solution_engenharia'),'sourceScope':rows.get('source_git_scope'),'bundlePath':rows.get('source_git_bundle'),'bundleSha256':h,'predecessor':obj(prev),'current':obj(current)}
out.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
PY
python3 "$ROOT/Solution/scripts/release-source-gate.py" --bundle "$OUT_BUNDLE" --provenance "$OUT_PROV"
echo "Release source bundle OK: $OUT_BUNDLE"
