#!/usr/bin/env python3
"""Execute the A/B/C/D diagnostic against an immutable canonical CSV snapshot."""
import argparse
import hashlib
import json
import subprocess
import sys
from pathlib import Path

def execute(snapshot_dir, report_path):
    root = Path(snapshot_dir)
    manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
    source = root / "canonical_observations.csv"
    digest = hashlib.sha256(source.read_bytes()).hexdigest()
    if not manifest.get("frozen") or manifest.get("test_used_for_frequency") is not False:
        raise ValueError("invalid frozen TRAIN-only snapshot")
    if digest != manifest.get("frozen_source_sha256"):
        raise ValueError("canonical observations hash mismatch")
    for filename, key in (("train_name_frequencies.json", "frequency_sha256"), ("train_joint_name_frequencies.json", "joint_frequency_sha256")):
        if hashlib.sha256((root / filename).read_bytes()).hexdigest() != manifest.get(key):
            raise ValueError("frozen TRAIN frequency hash mismatch")
    runner = Path(__file__).with_name("v9-abc-paired-blocking.py")
    if not runner.exists():
        raise FileNotFoundError("A/B/C/D diagnostic not merged yet")
    subprocess.run([sys.executable, str(runner), "--canonical-csv", str(source),
                    "--out", str(report_path)], check=True)
    result = json.loads(Path(report_path).read_text(encoding="utf-8"))
    expected = {a + suffix for a in ("A0", "A1", "B0", "B1", "C0", "C1", "D0", "D1")
                for suffix in ("", "+E0")}
    if result.get("source_sha256") != digest or result.get("diagnostic_only") is not True:
        raise ValueError("diagnostic provenance mismatch")
    for split in ("TRAIN", "VALIDATION", "TEST"):
        if set(result["results"][split]["arms"]) != expected:
            raise ValueError("missing experimental arms")
    return result

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--snapshot-dir", required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args()
    execute(args.snapshot_dir, args.out)
