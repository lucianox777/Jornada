#!/usr/bin/env python3
"""Freeze canonical C#-exported V9 projections and TRAIN-only frequencies.

This does not implement normalization or phonetics. It refuses raw-only input.
An existing snapshot is immutable unless explicitly written to a new directory.
"""
import argparse
import csv
import hashlib
import json
import shutil
import tempfile
from collections import Counter
from pathlib import Path

FEATURES = ("nome_normalizado", "nome_fonetico",
            "nome_mae_normalizado", "nome_mae_fonetico")
IDENTITY = ("base_person_id", "particao", "observacao_id")
VERSION = "V9_CANONICAL_NAME_FREQUENCY_SNAPSHOT_V1"


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def freeze(source, target, projection_version):
    source, target = Path(source), Path(target)
    if not projection_version or not projection_version.strip():
        raise ValueError("canonical C# projection version is required")
    if target.exists():
        raise FileExistsError("snapshot already exists: never overwrite frozen evidence")
    with source.open(encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        required = set(FEATURES + IDENTITY + ("data_nascimento",))
        if not required.issubset(reader.fieldnames or ()):
            raise ValueError("missing canonical columns: " + ", ".join(sorted(required - set(reader.fieldnames or ()))))
        rows = list(reader)
    if not rows:
        raise ValueError("empty corpus")
    ids = [r["observacao_id"] for r in rows]
    if len(ids) != len(set(ids)):
        raise ValueError("duplicate observation IDs")
    splits = {"TRAIN", "VALIDATION", "TEST"}
    if any(r["particao"] not in splits or not r["base_person_id"] for r in rows):
        raise ValueError("invalid split or identity")
    ownership = {}
    for row in rows:
        key = row["base_person_id"]
        if key in ownership and ownership[key] != row["particao"]:
            raise ValueError("identity leaked between partitions")
        ownership[key] = row["particao"]
    # Frequency = number of DISTINCT TRAIN identities containing the value,
    # not number of observations. Phonetic and normalized frequencies are separate.
    unique = {feature: set() for feature in FEATURES}
    for row in rows:
        if row["particao"] == "TRAIN":
            for feature in FEATURES:
                if row[feature]:
                    unique[feature].add((row[feature], row["base_person_id"]))
    frequencies = {feature: dict(sorted(Counter(v for v, _ in pairs).items()))
                   for feature, pairs in unique.items()}
    joint = {("nome_normalizado", "nome_mae_normalizado"): set(),
             ("nome_fonetico", "nome_mae_fonetico"): set(),
             ("nome_normalizado", "nome_mae_fonetico"): set(),
             ("nome_fonetico", "nome_mae_normalizado"): set(),
             ("nome_normalizado", "nome_mae_normalizado", "data_nascimento"): set()}
    for row in rows:
        if row["particao"] != "TRAIN":
            continue
        for fields, pairs in joint.items():
            if all(row[field] for field in fields):
                pairs.add(tuple(row[field] for field in fields) + (row["base_person_id"],))
    joint_counts = {"|".join(fields): [{"key": list(key), "identities": count}
                   for key, count in sorted(Counter(pair[:-1] for pair in pairs).items())]
                   for fields, pairs in joint.items()}
    target.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=".v9-freeze-", dir=target.parent))
    shutil.copyfile(source, staging / "canonical_observations.csv")
    freq_path = staging / "train_name_frequencies.json"
    freq_path.write_text(json.dumps(frequencies, ensure_ascii=False, sort_keys=True,
                                    indent=2) + "\n", encoding="utf-8")
    joint_path = staging / "train_joint_name_frequencies.json"
    joint_path.write_text(json.dumps(joint_counts, ensure_ascii=False, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    manifest = {
        "version": VERSION, "projection_version": projection_version,
        "source_sha256": digest(source), "frozen_source_sha256": digest(staging / "canonical_observations.csv"), "frequency_sha256": digest(freq_path), "joint_frequency_sha256": digest(joint_path),
        "observations": len(rows),
        "identities_by_split": {s: len({r["base_person_id"] for r in rows if r["particao"] == s})
                                for s in sorted(splits)},
        "features": list(FEATURES), "frequency_scope": "TRAIN_DISTINCT_BASE_PERSON_ID",
        "frozen": True, "test_used_for_frequency": False,
        "canonical_projection_parity_verified": False,
        "warning": "Source must be exported by canonical C# projector; parity verification is separate."
    }
    (staging / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, sort_keys=True, indent=2) + "\n",
        encoding="utf-8")
    if target.exists():
        shutil.rmtree(staging)
        raise FileExistsError("snapshot already exists")
    staging.rename(target)
    return manifest


if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("--canonical-csv", required=True)
    p.add_argument("--snapshot-dir", required=True)
    p.add_argument("--projection-version", required=True)
    a = p.parse_args()
    print(json.dumps(freeze(a.canonical_csv, a.snapshot_dir, a.projection_version),
                     ensure_ascii=False))
