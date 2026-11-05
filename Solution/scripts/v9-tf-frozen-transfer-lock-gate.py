#!/usr/bin/env python3
"""Fail-closed gate binding a frozen-transfer run to its LOCKED manifest (seeds 44-47, #1275; 3-sigma seeds 48-57).

Checks, before any corpus is generated: manifest digest, LOCKED status, authorized
seed, per-seed pre-registered plans, frozen snapshot identities, and that the
checked-out generator and reference inputs are byte-identical to the seed-43 run.
Never reads corpora, TEST, SQL or results.
"""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

LOCK = Path("Solution/docs/V9_TF_SEEDS_44_47_LOCK.json")
LOCK_3SIGMA = Path("Solution/docs/V9_TF_SEEDS_48_57_LOCK.json")
EXPERIMENTS = {LOCK: "V9-TF-ONE-MF-SEEDS-44-47", LOCK_3SIGMA: "V9-TF-ONE-MF-3SIGMA-SEEDS-48-57"}


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def tree_digest(repo, relative):
    files = subprocess.run(["git", "-C", str(repo), "ls-files", "--", relative],
                           check=True, text=True, capture_output=True).stdout.split()
    if not files:
        raise ValueError("Tracked tree missing: " + relative)
    lines = "".join(f"{sha256(Path(repo) / f)}  {f}\n" for f in sorted(files))
    return hashlib.sha256(lines.encode("utf-8")).hexdigest(), len(files)


def check(repo, seed, expected_lock_sha256, lock_relative=LOCK):
    repo = Path(repo)
    lock_relative = Path(lock_relative)
    if lock_relative not in EXPERIMENTS:
        raise ValueError("Unknown LOCK manifest")
    lock_path = repo / lock_relative
    if sha256(lock_path) != expected_lock_sha256:
        raise ValueError("LOCK manifest digest differs from the workflow reference")
    lock = json.loads(lock_path.read_bytes())
    if lock.get("status") != "LOCKED" or lock.get("experiment_id") != EXPERIMENTS[lock_relative]:
        raise ValueError("Manifest is not the expected LOCKED contract")
    if lock_relative == LOCK_3SIGMA:
        stat = lock.get("statistics") or {}
        if (stat.get("primary_test") != "EXACT_TWO_SIDED_SIGN_TEST_CAMPAIGN_UNIT"
                or stat.get("alpha_two_sided") != 0.0026998 or stat.get("planned_campaigns") != 10
                or sorted(lock["execution"]["seeds"]) != list(range(48, 58))):
            raise ValueError("3-sigma statistical pre-registration incomplete")
    if seed not in lock["execution"]["seeds"]:
        raise ValueError("Seed not authorized by the LOCKED manifest")
    gen = lock["generation"]
    digest, count = tree_digest(repo, "Solution/src/Jornada.Linkage.SyntheticCorpus")
    if digest != gen["generator_sha256"] or count != 25:
        raise ValueError("Generator source differs from the seed-43 run")
    ibge, _ = tree_digest(repo, "Solution/data/reference/ibge-nomes-2022")
    refs = gen["reference_inputs"]
    if (ibge != refs["ibge_nomes_2022_tree_sha256"]
            or sha256(repo / "Solution/data/reference/ibge-nomes-2022/manifest.json") != refs["ibge_nomes_2022_manifest_sha256"]
            or sha256(repo / "Solution/data/reference/synthetic-birth-sp/birth_daily_sp_projection2024_2026.json")
            != refs["birth_daily_sp_sha256"]):
        raise ValueError("Generator reference inputs differ from the seed-43 run")
    if sha256(repo / "Solution/src/Jornada.Linkage.SyntheticCorpus/SyntheticCorpusGenerator.cs") \
            != gen["partition_contract"]["generator_file_sha256"]:
        raise ValueError("Partition contract source differs")
    plan_dir = repo / f"Solution/data/reference/v9-tf-frozen-transfer-seed{seed}"
    expected = lock["execution"]["per_seed_plans"][str(seed)]
    if (sha256(plan_dir / "executor.json") != expected["executor_sha256"]
            or sha256(plan_dir / "frozen-transfer-plan.json") != expected["preflight_plan_sha256"]):
        raise ValueError("Per-seed pre-registered plan differs from the LOCKED manifest")
    executor = json.loads((plan_dir / "executor.json").read_bytes())
    plan = json.loads((plan_dir / "frozen-transfer-plan.json").read_bytes())
    primary = {a["OptionId"]: a["SnapshotSha256"] for a in executor["Arms"] if a["Role"].startswith("PRIMARY_")}
    models = lock["models"]
    if (executor["TargetSeed"] != seed or plan["target_seed"] != seed
            or primary != {models["control"]["name"]: models["control"]["sha256"],
                           models["treatment"]["name"]: models["treatment"]["sha256"]}
            or executor["SourcePlanTfSha256"] != models["source_plan_tf_sha256"]
            or executor["SourceStudySha256"] != models["source_study_sha256"]
            or executor["SourceArtifactName"] != models["source_artifact"]
            or executor["EndToEndRepetitions"] != lock["evaluation"]["end_to_end_repetitions"]
            or executor["PrimaryConflictMargin"] != lock["evaluation"]["primary_margin"]):
        raise ValueError("Frozen models or execution parameters differ from the LOCKED manifest")
    return {"lock_sha256": expected_lock_sha256, "experiment_id": lock["experiment_id"], "seed": seed, "generator_sha256": digest,
            "executor_sha256": expected["executor_sha256"], "status": "LOCK_GATE_PASS_NO_CORPUS_YET",
            "exposure": lock["seed_exposure"][str(seed)]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path("."))
    parser.add_argument("--seed", type=int, required=True)
    parser.add_argument("--expected-lock-sha256", required=True)
    parser.add_argument("--lock", type=Path, default=LOCK, choices=list(EXPERIMENTS))
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    report = check(args.repo, args.seed, args.expected_lock_sha256, args.lock)
    with args.output.open("x", encoding="utf-8") as out:
        json.dump(report, out, indent=2)
        out.write("\n")
    print("PASS: LOCKED manifest, seed, generator, references and frozen plan verified; no corpus generated yet.")


if __name__ == "__main__":
    main()
