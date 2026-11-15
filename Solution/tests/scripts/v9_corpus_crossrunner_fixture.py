#!/usr/bin/env python3
"""Tiny public-only corpus fixture for PR cross-runner transport tests.

NOT the physical 100k synthetic generator, not a train/validation/test sample,
and never an alternative to a scientific evidence cohort.
"""
import argparse
import json
from pathlib import Path

NAMES = ("pessoas_verdade.csv", "observacoes.csv",
         "gabarito.json", "generation-manifest.json")

def write_fixture(destination: Path):
    if destination.exists() or destination.is_symlink():
        raise ValueError("Fixture generation target must not exist")
    destination.mkdir(parents=True)
    (destination / "pessoas_verdade.csv").write_text(
        "id,nome\nP1,MARIA\nP2,ANA\n", encoding="utf-8")
    (destination / "observacoes.csv").write_text(
        "id,pessoa,nome\nO1,P1,MARIA\nO2,P2,ANA\n", encoding="utf-8")
    (destination / "gabarito.json").write_text(
        json.dumps({"synthetic_probe_only": True, "no_scientific_test": True},
                   sort_keys=True) + "\n", encoding="utf-8")
    (destination / "generation-manifest.json").write_text(
        json.dumps({"schema": "V9_CROSSRUNNER_2_ROWS_PROBE_ONLY",
                    "people": 2, "no_calibration": True},
                   sort_keys=True) + "\n", encoding="utf-8")

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--out", type=Path, required=True)
    args = p.parse_args()
    write_fixture(args.out)
    print("V9_CROSSRUNNER_NONSCIENTIFIC_FIXTURE_CREATED")

if __name__ == "__main__":
    main()
