#!/usr/bin/env python3
"""SHA-verify generator-only 100k resource evidence; NEVER claims V9 physical proof."""
import argparse
import importlib.util
import json
from pathlib import Path
import shutil

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("v9_standard_100k_binder", HERE / "v9-scale-100k-technical-plan.py")
binder = importlib.util.module_from_spec(spec)
spec.loader.exec_module(binder)

def create_report(corpus, gnu_time_kib):
    _, manifest_sha, observations_sha = binder.corpus_provenance(corpus)
    sample = Path(gnu_time_kib).read_text(encoding="utf-8").strip()
    if not sample.isdigit() or int(sample) <= 0:
        raise ValueError("generator RSS sample missing/invalid")
    filenames = ["observacoes.csv", "pessoas_verdade.csv",
                 "gabarito.json", "generation-manifest.json"]
    generated_bytes = sum((Path(corpus) / name).stat().st_size for name in filenames)
    return {
        "schema": "V9_STANDARD_GITHUB_100K_GENERATOR_RESOURCE_PROFILE_V1",
        "scope": "GENERATOR_ONLY_NO_SQL_NO_V9_FIT_NO_TEST_NO_GOLD",
        "people": 100000, "knownTrainSeed": 42,
        "generatorPeakResidentBytes": int(sample) * 1024,
        "generatedCorpusLogicalBytes": generated_bytes,
        "hostFreeDiskAfterCorpusBytes": shutil.disk_usage(corpus).free,
        "producerManifestSha256": manifest_sha,
        "observationsSha256": observations_sha,
        "method": "GNU_TIME_LINUX_GENERATOR_MAX_RSS_AND_FILE_LENGTHS",
        "notice": ("NO SQL, 24 fitting options, FDR or end-to-end benchmark performed. "
                   "Free disk snapshot is not the peak and model scalability is not certified."),
        "testUsed": False, "certified": False, "activated": False,
    }

def main():
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("--corpus-root", type=Path, required=True)
    cli.add_argument("--gnu-time-kib-file", type=Path, required=True)
    cli.add_argument("--output", type=Path, required=True)
    args = cli.parse_args()
    result = create_report(args.corpus_root, args.gnu_time_kib_file)
    with args.output.open("x", encoding="utf-8") as stream:
        json.dump(result, stream, sort_keys=True, indent=2)
        stream.write("\n")
    print("PASS: 100k synthetic generator-only resource profile, NO V9 physical calibration")


if __name__ == "__main__":
    main()
