#!/usr/bin/env python3
"""Read-only SHA-backed schema inventory of the six archived V9 TF frozen fits.

Does not train, recalibrate, read TEST, query SQL, publish or activate models.
Only emits sanitized metadata (no CSV, names, TF dictionaries, or connection data).
"""
import argparse
import hashlib
import json
from pathlib import Path

SOURCE_VERSION = "V9_TF_SEED43_FROZEN_TRANSFER_EXECUTOR_V1"
OUTPUT_VERSION = "V9_TF_ARCHIVED_SOURCE_SCHEMA_AUDIT_V1"


def digest(path):
    h = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(1048576), b""):
            h.update(chunk)
    return h.hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def valid_sha(value):
    return isinstance(value, str) and len(value) == 64 and all(c in "0123456789abcdef" for c in value)


def structure(snapshot):
    require(isinstance(snapshot, dict), "Malformed snapshot object")
    schema = snapshot.get("SchemaVersion")
    require(schema in {"V9_FITTED_SNAPSHOT_V2", "V9_FITTED_SNAPSHOT_V3"}, "Unexpected snapshot schema")
    passes = snapshot.get("BlockingPasses")
    cells = snapshot.get("JointCells")
    require(isinstance(passes, list) and passes and all(isinstance(p, dict)
            and isinstance(p.get("PassId"), str) and isinstance(p.get("Fields"), list)
            for p in passes), "Invalid frozen blocking passes")
    require(isinstance(cells, list), "Missing joint cells")
    support = snapshot.get("JointSupport")
    require((schema.endswith("_V3") and isinstance(support, dict))
            or (schema.endswith("_V2") and support is None), "V2/V3 joint-support contract mismatch")
    units = snapshot.get("UnitTfCalibrations")
    require(units is None or isinstance(units, list), "Invalid frozen TF units")
    active = []
    for item in units or []:
        require(isinstance(item, dict), "Invalid TF entry")
        # .NET V9SnapshotTfFitState.Active = enum ZERO (not one).
        if item.get("State") in (0, "Active"):
            fit = item.get("Calibration")
            require(isinstance(fit, dict), "Active TF has no frozen calibration")
            active.append({"side": item.get("Side"), "unit": item.get("Unit"),
                           "weight": fit.get("Weight"), "mode": fit.get("Mode")})
    grouping = snapshot.get("CandidateGrouping")
    group = grouping.get("CandidateGrouping") if isinstance(grouping, dict) else None
    eligible = (schema.endswith("_V3") and len(cells) == 81 and len(active) > 0
                and group == "BLIND_FS_COMPLETE_LINK_CANDIDATES")
    return {
        "schemaVersion": schema, "jointSupportPresent": support is not None,
        "jointCellCount": len(cells), "blockingPassCount": len(passes),
        "blockingPassIdsInOrder": [p["PassId"] for p in passes],
        "activeUnitTfCount": len(active), "activeUnitTfMetadata": active,
        "legacyTfWeight": snapshot.get("TfWeight"),
        "candidateGrouping": group, "goldTfV3DiagnosticStructurallyEligible": eligible,
        "ineligibleReason": None if eligible else (
            "V2_WITHOUT_JOINT_SUPPORT" if schema.endswith("_V2") else
            "NO_ACTIVE_UNIT_TF" if not active else
            "NOT_BLIND" if group != "BLIND_FS_COMPLETE_LINK_CANDIDATES" else
            "NOT_81_JOINT_CELLS"),
    }




def archived_train_joint_support(study, sha):
    """Audit observed TRAIN pair counts; do NOT infer counts from smoothed m/u.

    The original physical runner stores V9FittedJointSupportAudit.Report alongside
    every fitted snapshot in study.json. Its distinct M/U *pairs* are not iid
    people and cannot establish clinical or population FDR certification.
    """
    rows = study.get("FrozenConfigurations")
    require(isinstance(rows, list) and rows, "Missing archived frozen configurations")
    found = [r for r in rows if isinstance(r, dict) and isinstance(r.get("Snapshot"), dict)
             and r["Snapshot"].get("ContentSha256") == sha]
    require(len(found) == 1, "Pinned TF arm has no unique archived support receipt")
    support = found[0].get("JointSupport")
    if support is None:
        return {"status": "NO_TRAIN_SUPPORT_RECEIPT", "rawObserved81Available": False}
    require(isinstance(support, dict) and support.get("SnapshotSha256") == sha,
            "TRAIN support snapshot binding mismatch")
    alpha, tm, tu = (support.get("SmoothingAlpha"),
                     support.get("TrainMPairs"), support.get("TrainUPairs"))
    require(type(alpha) in (float, int) and alpha > 0
            and type(tm) is int and tm >= 2 and type(tu) is int and tu >= 2,
            "Invalid recorded TRAIN pair counts or smoothing")
    cells = support.get("Cells")
    require(isinstance(cells, list) and len(cells) == 81,
            "Observed TRAIN joint table does not have 81 cells")
    pairs, m_total, u_total = set(), 0, 0
    for cell in cells:
        require(isinstance(cell, dict), "Invalid observed TRAIN cell")
        person, mother = cell.get("Person"), cell.get("Mother")
        # Both .NET enum values and their explicit JSON string forms are accepted.
        require(isinstance(person, (int, str)) and not isinstance(person, bool)
                and isinstance(mother, (int, str)) and not isinstance(mother, bool),
                "Invalid class-conditional nominal pair")
        key = (person, mother)
        require(key not in pairs, "Duplicated observed cell")
        pairs.add(key)
        m, u = cell.get("ObservedMPairs"), cell.get("ObservedUPairs")
        require(type(m) is int and type(u) is int and m >= 0 and u >= 0,
                "Negative or missing TRAIN cell support")
        m_total += m
        u_total += u
    require(m_total == tm and u_total == tu, "Archived TRAIN support pair totals disagree")
    require(support.get("IndependentIdentitySupportEstablished") is not True,
            "Archived TRAIN support incorrectly asserts independent identities")
    normalized = sorted([(str(c["Person"]), str(c["Mother"]),
                          c["ObservedMPairs"], c["ObservedUPairs"]) for c in cells])
    canonical = json.dumps(normalized, separators=(",", ":"), ensure_ascii=True).encode("ascii")
    return {
        "status": "RAW_TRAIN_81_PAIR_COUNTS_AUDITED_NOT_CERTIFIED",
        "rawObserved81Available": True, "trainMPairs": tm, "trainUPairs": tu,
        "smoothingAlpha": alpha, "trainJointCountsSha256": hashlib.sha256(canonical).hexdigest(),
        "independentIdentitySupportEstablished": False,
        "newJointFitComputed": False,
        "thresholdOrTfAdjusted": False,
        "requiresNewV3FitAndIndependentValidation": True,
    }



def audit(executor, root):
    require(executor.get("Version") == SOURCE_VERSION, "Unknown source executor version")
    require(executor.get("SourceRunId") == 41900357031, "Source run is not the pre-registered run")
    source_sha = executor.get("SourceCommitSha")
    require(isinstance(source_sha, str) and len(source_sha) == 40
            and all(c in "0123456789abcdef" for c in source_sha), "Bad source commit SHA")
    require(executor.get("SourceArtifactName") ==
            "v9-tf-45-physical-" + str(executor["SourceRunId"]) + "-" + source_sha,
            "Artifact name is not bound to pinned source run")
    require(digest(root / "plan-tf.json") == executor.get("SourcePlanTfSha256"),
            "Source calibration plan SHA differs")
    require(digest(root / "study-tf" / "study.json") == executor.get("SourceStudySha256"),
            "Source study SHA differs")
    source_study = json.loads((root / "study-tf" / "study.json").read_bytes())
    require(source_study.get("PlanSha256") == executor.get("SourcePlanTfSha256"),
            "Source study does not belong to the pinned calibration plan")
    arms = executor.get("Arms")
    require(isinstance(arms, list) and len(arms) == 6, "Exactly six frozen arms required")
    records = []
    for arm in arms:
        sha = arm.get("SnapshotSha256")
        require(valid_sha(sha), "Missing frozen SHA")
        path = root / "study-tf" / (sha + ".fitted.json")
        require(digest(path) == sha, "Frozen model content failed SHA verification")
        snap = json.loads(path.read_bytes())
        option = snap.get("OptionId")
        require(isinstance(option, str), "Missing option ID")
        bare = option[65:] if len(option) > 65 and option[64] == ":" else option
        require(bare == arm["OptionId"], "Archived fit option differs from pinned plan")
        require(str(snap.get("ConflictMargin")) == str(arm["ConflictMargin"]),
                "Conflict margin differs from pinned plan")
        require(str(snap.get("Threshold")) == "0.95", "Upper threshold differs from pinned plan")
        require(snap.get("TrainCorpusSha256") == source_study.get("TrainInputSha256")
                and snap.get("ValidationCorpusSha256") == source_study.get("ValidationInputSha256"),
                "Archived fit's TRAIN/VALIDATION input SHA does not match study provenance")
        records.append({"role": arm["Role"], "optionId": arm["OptionId"],
                        "snapshotSha256": sha, "margin": arm["ConflictMargin"],
                        **structure(snap),
                        "recordedTrainJointSupport": archived_train_joint_support(source_study, sha)})
    primary = [x for x in records if x["role"] == "PRIMARY_TREATMENT"]
    require(len(primary) == 1 and primary[0]["optionId"] == "TF_ONE_MF_W1_M0_A0.5_T0.95"
            and primary[0]["margin"] == 0.5, "Missing predeclared primary ONE_MF")
    controls = [x for x in records if x["role"] == "PRIMARY_CONTROL"]
    require(len(controls) == 1 and controls[0]["optionId"] == "T0.95_TF0"
            and controls[0]["margin"] == 0.5, "Missing primary TF0 control")
    return {
        "schema": OUTPUT_VERSION, "sourceRunId": executor["SourceRunId"],
        "sourceCommitSha": source_sha, "sourcePlanSha256": executor["SourcePlanTfSha256"],
        "sourceStudySha256": executor["SourceStudySha256"], "models": records,
        "oneMfGoldTfV3DiagnosticStructurallyEligible":
            primary[0]["goldTfV3DiagnosticStructurallyEligible"],
        "scope": "READ_ONLY_ARCHIVED_FIT_SCHEMA_ONLY",
        "recalibrated": False, "goldPhysicalExecuted": False,
        "sqlReferenceInspected": False, "operationalBindingVerified": False,
        "certified": False, "dt15Authorized": False,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-dir", required=True, type=Path)
    parser.add_argument("--executor", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    result = audit(json.loads(args.executor.read_bytes()), args.source_dir)
    # Output must be new; a prior audit receipt cannot be overwritten.
    with args.output.open("x", encoding="utf-8") as file:
        json.dump(result, file, indent=2, ensure_ascii=False)
        file.write("\n")
    print("PASS: all six archived fitted snapshot bytes match pinned SHA-256.")
    print("ONE_MF:", json.dumps(next(x for x in result["models"]
                                    if x["role"] == "PRIMARY_TREATMENT"), ensure_ascii=False))
    print("GOLD_V3_TF_STRUCTURAL_ELIGIBILITY:",
          result["oneMfGoldTfV3DiagnosticStructurallyEligible"])
    print("ONE_MF_TRAIN_JOINT_81:", json.dumps(next(x for x in result["models"]
          if x["role"] == "PRIMARY_TREATMENT")["recordedTrainJointSupport"], ensure_ascii=False))
    print("NO_REFIT_NO_SQL_NO_TEST_NO_CERTIFICATION")


if __name__ == "__main__":
    main()
