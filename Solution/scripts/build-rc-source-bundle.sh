#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
REPO="${1:-$ROOT}"
OUT_BUNDLE="${2:-$ROOT/Solution/.local/rc-source/Jornada_RC_Source.bundle}"
OUT_PROV="${3:-$ROOT/Solution/.local/rc-source/RC_SOURCE_PROVENANCE.json}"
CANDIDATE="${4:-$ROOT/CANDIDATE_INFO.json}"
EXPECTED_TAG="${5:-${GITHUB_REF_NAME:-}}"

command -v git >/dev/null 2>&1 || { echo "ERRO: git não encontrado." >&2; exit 2; }
command -v python3 >/dev/null 2>&1 || { echo "ERRO: python3 não encontrado." >&2; exit 2; }
[[ -n "$EXPECTED_TAG" ]] || { echo "ERRO: tag RC esperada não informada." >&2; exit 2; }

python3 "$ROOT/Solution/scripts/technical-rc-gate.py"   --repo "$REPO"   --candidate-info "$CANDIDATE"   --release-info "$ROOT/RELEASE_INFO.txt"   --expected-tag "$EXPECTED_TAG"   --require-clean

SEALED_TAG="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8"))["sealed_release"]["source_git_tag"])' "$CANDIDATE")"
mkdir -p "$(dirname "$OUT_BUNDLE")" "$(dirname "$OUT_PROV")"
git -C "$REPO" bundle create "$OUT_BUNDLE" "refs/tags/$SEALED_TAG" "refs/tags/$EXPECTED_TAG"
git bundle verify "$OUT_BUNDLE" >/dev/null

python3 - "$REPO" "$OUT_BUNDLE" "$OUT_PROV" "$CANDIDATE" "$SEALED_TAG" "$EXPECTED_TAG" <<'PY'
import hashlib
import json
import subprocess
import sys
from pathlib import Path

repo=Path(sys.argv[1]); bundle=Path(sys.argv[2]); out=Path(sys.argv[3])
candidate_path=Path(sys.argv[4]); predecessor=sys.argv[5]; current=sys.argv[6]
candidate=json.loads(candidate_path.read_text(encoding="utf-8"))

def git(*args):
    return subprocess.check_output(["git","-C",str(repo),*args], text=True).strip()

def obj(tag):
    commit=git("rev-parse",f"refs/tags/{tag}^{{commit}}")
    tree=git("rev-parse",f"{commit}^{{tree}}")
    count=len([x for x in git("ls-tree","-r","--name-only",commit).splitlines() if x.strip()])
    return {"tag":tag,"commit":commit,"tree":tree,"trackedFileCount":count}

data={
    "schemaVersion":1,
    "nature":"TECHNICAL_RC_SOURCE_PROVENANCE",
    "releaseEffect":"NONE",
    "candidateInfoSha256":hashlib.sha256(candidate_path.read_bytes()).hexdigest(),
    "bundleSha256":hashlib.sha256(bundle.read_bytes()).hexdigest(),
    "externalGates":candidate.get("external_gates",[]),
    "predecessorSealedRelease":obj(predecessor),
    "current":obj(current),
}
out.write_text(json.dumps(data,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
PY

echo "Technical RC source bundle OK: $OUT_BUNDLE"
