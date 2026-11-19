#!/usr/bin/env bash
# Isolated seed-42 regression of the historical generator against current HEAD.
# No SQL, model evaluation, calibration, publication or TEST scoring.
set -euo pipefail
repo="$(git rev-parse --show-toplevel)"
cd "$repo"
historical_ref="b98a01180737909edf7cbd0bee4ba2757d52fe0f"
tmp="$(mktemp -d)"
cleanup() {
  git worktree remove --force "$tmp/historical" >/dev/null 2>&1 || true
  rm -rf "$tmp"
}
trap cleanup EXIT
git worktree add --detach "$tmp/historical" "$historical_ref"
mkdir -p "$tmp/current" "$tmp/historical-output"
generate() {
  local checkout="$1" output="$2"
  cd "$checkout/Solution"
  dotnet restore src/Jornada.Linkage.SyntheticCorpus/Jornada.Linkage.SyntheticCorpus.csproj --locked-mode
  dotnet run --project src/Jornada.Linkage.SyntheticCorpus --configuration Release -- generate \
    --reference-root "$checkout/Solution/data/reference/ibge-nomes-2022" \
    --population-profile demographic-primary \
    --birth-daily-source "$checkout/Solution/data/reference/synthetic-birth-sp/birth_daily_sp_projection2024_2026.json" \
    --error-profile clean --people 30000 --seed 42 --out "$output" > "$output.log"
}
generate "$tmp/historical" "$tmp/historical-output"
generate "$repo" "$tmp/current"
cd "$repo"
python3 - "$tmp/historical-output" "$tmp/current" <<'PY'
import hashlib, json, pathlib, sys
old, new = map(pathlib.Path, sys.argv[1:])
names = ("pessoas_verdade.csv", "observacoes.csv", "gabarito.json")
report = {"seed": 42, "population": 30000, "status": "PHYSICAL_COMPARISON",
          "scope": "historical generator vs current HEAD; no TEST scoring",
          "files": {}}
for name in names:
    a = hashlib.sha256((old / name).read_bytes()).hexdigest()
    b = hashlib.sha256((new / name).read_bytes()).hexdigest()
    report["files"][name] = {"historical_sha256": a, "current_sha256": b, "equal": a == b}
report["corpus_bytes_equal"] = all(x["equal"] for x in report["files"].values())
old_manifest = json.loads((old / "generation-manifest.json").read_text())
new_manifest = json.loads((new / "generation-manifest.json").read_text())
report["manifest_metadata_equal"] = old_manifest == new_manifest
report["manifest_changed_fields"] = sorted(set(old_manifest) ^ set(new_manifest))
pathlib.Path("v9-seed42-generator-byte-comparison.json").write_text(
    json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report, indent=2))
if not report["corpus_bytes_equal"]:
    sys.exit("Corpus bytes diverge; do not certify historical compatibility")
PY
