#!/usr/bin/env python3
"""Renew only the exact already-published campaign 38 winner. No fitting or SQL."""
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import sys
import time
import zipfile

def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(filename))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod

p = load("preserved", "v9-preserve-campaign38-artifacts.py")
pin = load("pin", "v9-e2e-1k-winner-pin.py")
ORIGINAL = "winner-original.zip"
WORKFLOW = ".github/workflows/jornada-v9-winner38-custody.yml"

def require(ok):
    if not ok:
        raise ValueError("V9_WINNER38_CUSTODY_REJECTED")

def api(suffix, token):
    return p.api_json(f"https://api.github.com/repos/{p.REPO}/actions/{suffix}", token)

def verify_original_run(token):
    run = api(f"runs/{p.RUN_ID}", token)
    require(run.get("id") == p.RUN_ID and run.get("head_sha") == p.CODE_SHA
        and run.get("run_attempt") == p.RUN_ATTEMPT and run.get("conclusion") == "success"
        and run.get("workflow_id") == p.WORKFLOW_ID and run.get("path") == p.WORKFLOW_PATH
        and run.get("event") == p.WORKFLOW_EVENT and run.get("head_branch") == p.WORKFLOW_BRANCH)

def prepare(root, token):
    verify_original_run(token)
    row = api(f"artifacts/{pin.ARTIFACT_ID}", token)
    require(row["id"] == pin.ARTIFACT_ID and not row["expired"]
        and row["workflow_run"]["id"] == p.RUN_ID and row["workflow_run"]["head_sha"] == p.CODE_SHA
        and row["digest"] == "sha256:" + pin.ZIP_SHA and row["size_in_bytes"] == pin.ZIP_BYTES)
    temp, _ = p.download_verified(pin.ARTIFACT_ID, pin.ZIP_SHA, root, token)
    Path(temp).rename(root / ORIGINAL)
    pin.verify(root / ORIGINAL, root / "initial-verification")
    print("V9_WINNER38_ORIGINAL_BOTH_HASHES_PASS")

def unpack_verified_envelope(outer, destination):
    # Exactly one ordinary file, bounded read, no archive path extraction.
    with zipfile.ZipFile(outer) as z:
        members = z.infolist()
        require(len(members) == 1)
        m = members[0]
        require(m.filename == ORIGINAL and not m.is_dir()
            and stat.S_IFMT(m.external_attr >> 16) in (0, stat.S_IFREG)
            and not (m.flag_bits & 1) and m.file_size == pin.ZIP_BYTES)
        with z.open(m) as f:
            raw = f.read(pin.ZIP_BYTES + 1)
        require(len(raw) == pin.ZIP_BYTES and hashlib.sha256(raw).hexdigest() == pin.ZIP_SHA)
    with destination.open("xb") as f:
        f.write(raw)

def verify_new(root, token):
    new_id = int(os.environ["NEW_ARTIFACT_ID"])
    outer_sha = os.environ["NEW_ARTIFACT_DIGEST"].removeprefix("sha256:")
    run_id = int(os.environ["GITHUB_RUN_ID"])
    attempt = int(os.environ["GITHUB_RUN_ATTEMPT"])
    sha = os.environ["EXPECTED"]
    require(new_id != pin.ARTIFACT_ID and re.fullmatch("[0-9a-f]{64}", outer_sha))
    run = api(f"runs/{run_id}", token)
    # For PR workflows head_sha may refer to the merge ref; the checkout
    # was separately bound to EXPECTED and the PR payload is checked below.
    event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text())
    event_name = os.environ["GITHUB_EVENT_NAME"]
    if event_name == "pull_request":
        require(event["pull_request"]["head"]["sha"] == sha
            and event["pull_request"]["head"]["repo"]["full_name"] == p.REPO)
    else:
        require(event_name == "workflow_dispatch" and run["head_sha"] == sha)
    require(run["id"] == run_id and run["run_attempt"] == attempt
        and run["path"].split("@")[0] == WORKFLOW and run["event"] == event_name)
    # Artifact metadata may be briefly eventually consistent.
    for i in range(8):
        try:
            row = api(f"artifacts/{new_id}", token)
            break
        except OSError:
            if i == 7:
                raise
            time.sleep(3)
    require(row["id"] == new_id and not row["expired"]
        and row["workflow_run"]["id"] == run_id
        and row["name"] == f"v9-winner38-original-zip-custody-{run_id}-{attempt}"
        and row["digest"] == "sha256:" + outer_sha
        and 0 < row["size_in_bytes"] <= pin.ZIP_BYTES + 65536)
    outer, _ = p.download_verified(new_id, outer_sha, root, token)
    unpack_verified_envelope(outer, root / "recovered-original.zip")
    pin.verify(root / "recovered-original.zip", root / "recovered-verification")
    created = dt.datetime.fromisoformat(row["created_at"].replace("Z", "+00:00"))
    expires = dt.datetime.fromisoformat(row["expires_at"].replace("Z", "+00:00"))
    days = int(os.environ["GITHUB_RETENTION_DAYS"])
    require(abs((expires-created).total_seconds()-days*86400) <= 120)
    receipt = dict(status="RENEWED_BOTH_SCIENTIFIC_HASHES_VERIFIED",
        originalArtifactId=pin.ARTIFACT_ID, originalRunId=p.RUN_ID,
        originalZipSha256=pin.ZIP_SHA, operationalSha256=pin.WINNER_SHA,
        custodyArtifactId=new_id, custodyRunId=run_id, custodyRunAttempt=attempt,
        custodyCodeSha=sha, custodyEnvelopeSha256=outer_sha, expiresAt=row["expires_at"],
        retentionDays=days, refitted=False, activated=False, scientificTest=False)
    # Metadata only: no model, filenames from the original ZIP, private path or token.
    print("V9_WINNER38_CUSTODY_RECEIPT=" + json.dumps(receipt, sort_keys=True))
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as stream:
        stream.write("### Verified custody receipt\n\n```json\n" + json.dumps(receipt, indent=2) + "\n```\n")

def main():
    require(os.environ["GITHUB_REPOSITORY"] == p.REPO and os.environ["GITHUB_ACTIONS"] == "true")
    root = Path(os.environ["RUNNER_TEMP"]) / "v9-winner38-custody"
    require(root.is_dir() and not root.is_symlink() and not root.stat().st_mode & 0o077)
    token = os.environ["GH_TOKEN"]
    require(len(sys.argv) == 2 and sys.argv[1] in ("prepare", "verify"))
    (prepare if sys.argv[1] == "prepare" else verify_new)(root, token)

if __name__ == "__main__":
    try:
        main()
    except Exception:
        raise SystemExit("V9_WINNER38_CUSTODY_REJECTED") from None
