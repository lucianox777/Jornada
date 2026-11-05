#!/usr/bin/env python3
"""Fail-closed exact-head gate for PR-auto or manually dispatched V9 30k.

Auto mode authenticates same-repo PR HEAD and requires the independent
immutable ref/corpus ledger gate in v9-tf-prepare-pin.sh before fitting.
Manual mode still requires an independently supplied pinned IBGE JSON.
This module NEVER opens or evaluates a 30k corpus.
"""
import argparse
import importlib.util
import json
import re
import subprocess
from pathlib import Path

SHA = re.compile(r"[a-f0-9]{40}\Z")
HERE = Path(__file__).resolve().parent


def _grid_helper():
    spec = importlib.util.spec_from_file_location("v9_tf_dispatch_grid", HERE / "v9-tf-option-grid.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON field in pinned IBGE reference")
        result[key] = value
    return result


def _reject_nonfinite(value):
    raise ValueError("Nonfinite JSON field in pinned IBGE reference")


def validate(mode, expected_sha, actual_sha, remote_sha, ref, pinned_json=""):
    if mode not in ("preflight", "execute_30k", "auto_30k"):
        raise ValueError("Unknown V9 physical execution mode")
    if not ref.startswith("refs/heads/") or len(ref) <= len("refs/heads/"):
        raise ValueError("Physical study requires a branch ref (no tags)")
    if any(not isinstance(s, str) or not SHA.fullmatch(s)
           for s in (expected_sha, actual_sha, remote_sha)):
        raise ValueError("Exact lowercase 40-hex Git commit SHA required")
    if expected_sha != actual_sha or expected_sha != remote_sha:
        raise ValueError("Expected SHA, checkout and live branch HEAD disagree")
    helper = _grid_helper()
    grid = helper.option_grid()
    if (len(grid) != 45
            or len([x for x in grid if x["OptionId"].startswith("TF_SHARED_PL_ML_")]) != 4):
        raise ValueError("TF option grid drifted from 45 physical / 4 diagnostic")
    pinned = None
    if mode == "auto_30k" and pinned_json:
        raise ValueError("Automatic physical run must derive its IBGE pin from verified immutable ref/corpus")
    if mode == "execute_30k":
        if not pinned_json:
            raise ValueError("Physical execution requires independently pinned IBGE reference JSON")
        pinned = helper.pinned_reference(json.loads(
            pinned_json, object_pairs_hook=_unique_object, parse_constant=_reject_nonfinite))
    return {"protocol": "V9_TF_AUTO_45_PHYSICAL_V1" if mode == "auto_30k" else "V9_TF_MANUAL_45_PHYSICAL_V1",
            "mode": mode, "authorized_exact_sha": expected_sha,
            "git_ref": ref, "base_people_required": 30000,
            "physical_arms": 45, "selectable_arms": 41,
            "diagnostic_arms": 4, "margins": 3,
            "physical_evaluations_expected": 135,
            "test_used": False, "physical_executed": False,
            "certified": False, "activated": False}, pinned


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mode", required=True, choices=("preflight", "execute_30k", "auto_30k"))
    parser.add_argument("--expected-sha", required=True)
    parser.add_argument("--ref", required=True)
    parser.add_argument("--pinned-json", default="")
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--pinned-output", type=Path, default=None)
    args = parser.parse_args()
    if args.mode != "execute_30k" and args.pinned_output is not None:
        parser.error("only manually pinned execution may write a reference")
    actual = subprocess.run(["git", "rev-parse", "HEAD"],
                            check=True, text=True, capture_output=True).stdout.strip()
    remote = subprocess.run(["git", "ls-remote", "--exit-code", "origin", args.ref],
                            check=True, text=True, capture_output=True).stdout.strip().split()
    if len(remote) != 2 or remote[1] != args.ref:
        raise ValueError("Live branch ref is missing or ambiguous")
    report, pinned = validate(args.mode, args.expected_sha, actual, remote[0],
                              args.ref, args.pinned_json)
    with args.output.open("x", encoding="utf-8") as out:
        json.dump(report, out, indent=2, allow_nan=False)
        out.write("\n")
    if pinned is not None:
        if args.pinned_output is None:
            raise ValueError("Physical execution requires a new pinned-reference file")
        with args.pinned_output.open("x", encoding="utf-8") as out:
            json.dump(pinned, out, indent=2, allow_nan=False)
            out.write("\n")
    print("PASS: bound V9 execution to exact checkout and live branch HEAD;"
          " this guard has NOT run the physical 30k study.")


if __name__ == "__main__":
    main()
