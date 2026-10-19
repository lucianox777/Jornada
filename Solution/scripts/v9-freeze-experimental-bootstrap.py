#!/usr/bin/env python3
"""Preserve the explicitly requested v0 fit from verified CI bytes, without fitting or TEST."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import zipfile

ARCHIVE_SHA = "db3218f05f550dd651df381bbdf8291d2ae2f8c0bea4af81638b588e7d78f456"
SNAPSHOT_SHA = "613f914114073d4a9100e848a01a56321354ae9a7885a77462941ae86936953d"
SOURCE_HEAD = "b334f7d2fa99783df3c3d698a5a36055165fbffc"


def prepare(archive: Path):
    if hashlib.sha256(archive.read_bytes()).hexdigest() != ARCHIVE_SHA:
        raise ValueError("CI archive differs from the independently recorded SHA")
    with zipfile.ZipFile(archive) as bundle:
        # Read known entries only. No paths supplied by the ZIP are extracted.
        study_bytes = bundle.read("adaptive-30k-result/study.json")
        study = json.loads(study_bytes)
        fits = [item for item in study["FrozenConfigurations"]
                if item["Snapshot"]["ContentSha256"] == SNAPSHOT_SHA]
        if len(fits) != 1:
            raise ValueError("Expected exactly one requested v0 fit")
        fit = fits[0]
        payload = fit["Snapshot"]["Payload"]
        rows = [row for row in study["SourceMeasurements"]
                if row["OptionId"] == fit["OptionId"] and row["ConflictMargin"] == fit["ConflictMargin"]]
        if len(rows) != 1:
            raise ValueError("Requested fit has no unique source measurement")
        row = rows[0]
        if (payload["Threshold"], payload["ConflictMargin"], payload["TfWeight"],
                row["TrueAcceptedSourceObservations"], row["FalseAcceptedSourceObservations"],
                row["PreBlockingBaseIdentities"], row["BonferroniComparedConfigurations"]) != (.95, .5, 0, 7505, 0, 5556, 2493):
            raise ValueError("Requested v0 protocol or physical evidence differs")
        content = bundle.read("adaptive-30k-result/" + SNAPSHOT_SHA + ".fitted.json")
        if hashlib.sha256(content).hexdigest() != SNAPSHOT_SHA or json.loads(content) != payload:
            raise ValueError("Snapshot differs from the measured immutable payload")
    scenarios = []
    for comparisons in (1, 36, 2493):
        alpha_component = .05 / (2 * comparisons)
        cp = -math.expm1(math.log(alpha_component) / 5556)
        denominator = 7505 / 5556 - 4 * math.sqrt(math.log(1 / alpha_component) / (2 * 5556))
        scenarios.append({"comparisons": comparisons, "population_cap": 4,
                          "conditional_fdr_upper": 4 * cp / denominator,
                          "status": "RETROSPECTIVE_SENSITIVITY_NOT_NEW_VALIDATION"})
    receipt = {
        "schema": "V9_EXPERIMENTAL_BOOTSTRAP_REFERENCE_V1",
        "label": "bootstrap-v0-clean-T095-margin05-TF0",
        "snapshot_sha256": SNAPSHOT_SHA,
        "source_head": SOURCE_HEAD,
        "source_run": "https://github.com/lucianox777/Jornada/actions/runs/39338667251",
        "source_artifact_id": 12227823619,
        "source_archive_sha256": ARCHIVE_SHA,
        "source_study_sha256": hashlib.sha256(study_bytes).hexdigest(),
        "selection": "USER_REQUESTED_EXPERIMENTAL_REFERENCE_NOT_ELIGIBLE_SELECTOR_OUTPUT",
        "measurement_scope": "HISTORICAL_CLEAN_SEED42_VALIDATION_SOURCE_POST_RESOLVE",
        "source_measurement": row,
        "population_cap_four_sensitivity": scenarios,
        "decision_cluster_iid_validated": False,
        "operationally_certified": False,
        "test_reserved": False,
        "test_used": False,
        "published_to_sql_ref": False,
        "activated": False,
        "incremental_runner_gold_evaluated": False,
    }
    return content, receipt


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("new_directory", type=Path)
    args = parser.parse_args()
    snapshot, metadata = prepare(args.archive)
    args.new_directory.mkdir(parents=True, exist_ok=False)
    with (args.new_directory / (SNAPSHOT_SHA + ".fitted.json")).open("xb") as stream:
        stream.write(snapshot)
    with (args.new_directory / "experimental-reference.json").open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(metadata, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    print(json.dumps({"snapshot_sha256": SNAPSHOT_SHA, "approved": False, "test_used": False}))
