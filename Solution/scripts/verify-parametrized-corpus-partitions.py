#!/usr/bin/env python3
"""Read-only verification of a disposable parametrized synthetic corpus.

Not a scientific TRAIN/TEST certification. Does not alter the corpus.
"""
import argparse
import csv
import hashlib
import json
from collections import Counter
from pathlib import Path


def verify(root: Path) -> dict:
    manifest = json.loads((root / "generation-manifest.json").read_text(encoding="utf-8"))
    percentages = manifest["partition_percentages"]
    population = manifest["synthetic_population"]
    if not isinstance(population, int) or isinstance(population, bool) or population <= 0:
        raise ValueError("Invalid synthetic_population")
    for key in ("train", "validation", "test"):
        value = percentages[key]
        if not isinstance(value, (int, float)) or isinstance(value, bool):
            raise ValueError(f"Invalid percentage: {key}")
    if any(not (0 <= percentages[k] <= 100) for k in ("train", "validation", "test")):
        raise ValueError("Percentage outside [0,100]")
    if abs(sum(percentages[k] for k in ("train", "validation", "test")) - 100) > 1e-9:
        raise ValueError("Partition percentages do not sum to 100")
    expected_files = {"pessoas_verdade.csv", "observacoes.csv", "gabarito.json"}
    outputs = manifest["outputs"]
    if not isinstance(outputs, list):
        raise ValueError("Manifest outputs must be a list")
    declared = {}
    for entry in outputs:
        name, digest = entry["path"], entry["sha256"]
        if name in declared or name not in expected_files:
            raise ValueError(f"Unexpected or duplicate output: {name}")
        declared[name] = digest.lower()
    if set(declared) != expected_files:
        raise ValueError("Manifest output set mismatch")
    for name, digest in declared.items():
        actual = hashlib.sha256((root / name).read_bytes()).hexdigest()
        if actual != digest:
            raise ValueError(f"SHA256 mismatch: {name}")
    with (root / "pessoas_verdade.csv").open(newline="", encoding="utf-8-sig") as f:
        people = list(csv.DictReader(f))
    if len(people) != population:
        raise ValueError("Population count mismatch")
    partition_by_person = {}
    counts = Counter()
    for row in people:
        identity, partition = row["base_person_id"], row["particao"]
        if not identity or identity in partition_by_person:
            raise ValueError("Duplicate or empty person ID")
        if partition not in ("TRAIN", "VALIDATION", "TEST"):
            raise ValueError("Unknown person partition")
        partition_by_person[identity] = partition
        counts[partition] += 1
    with (root / "observacoes.csv").open(newline="", encoding="utf-8-sig") as f:
        observations = csv.DictReader(f)
        count_observations = 0
        for row in observations:
            identity = row["base_person_id"]
            if identity not in partition_by_person:
                raise ValueError("Orphan observation")
            if row["particao"] != partition_by_person[identity]:
                raise ValueError("Observation partition mismatch")
            count_observations += 1
    for key, label in (("train", "TRAIN"), ("validation", "VALIDATION"), ("test", "TEST")):
        if percentages[key] == 0 and counts[label]:
            raise ValueError(f"Nonempty zero-percent partition: {label}")
        if percentages[key] == 100 and counts[label] != population:
            raise ValueError(f"Incomplete 100-percent partition: {label}")
    return {"population": population, "observations": count_observations,
            "partitions": dict(sorted(counts.items()))}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("corpus", type=Path)
    args = parser.parse_args()
    print(json.dumps(verify(args.corpus), sort_keys=True))


if __name__ == "__main__":
    main()
