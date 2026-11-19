#!/usr/bin/env python3
"""Prepare one guarded 100k TRAIN-only V3 exploration, leaving the C# algorithm unchanged.

Uses the already verified 24-arm 100k corpus-bound plan as a PROVENANCE SOURCE,
not as the executable scientific search: the new plan evaluates 81 symmetric
four-unit TF states x V3 U-mixture lambda {0.5,1} x margins {0.5,2} on one
unchanged three-pass SQL frame. Does not execute the calibration or read TEST.
"""
import argparse
import copy
import hashlib
import itertools
import json
from pathlib import Path

SCHEMA = "V9_V3_FOUR_NAME_TF_100K_REBIND_V1"
BASE_SCHEMA = "V9_100K_MULTIPARAM_TECHNICAL_REBIND_V1"
STATES = (0, 0.5, 1)
LAMBDAS = (0.5, 1)
MARGINS = (0.5, 2)
SLOTS = ((0, 2, "PF"), (0, 3, "PL"), (1, 2, "MF"), (1, 3, "ML"))
OPTIONS = 81
EVALUATIONS = OPTIONS * len(LAMBDAS) * len(MARGINS)
TF0 = "A0.5_T0.95_TF0"


def require(ok, message):
    if not ok:
        raise ValueError("V9_V3_TF4_PLAN_FAIL_CLOSED: " + message)


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def strict_json(raw):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, "duplicate JSON member")
            result[key] = value
        return result
    return json.loads(raw, object_pairs_hook=unique,
                      parse_constant=lambda _: (_ for _ in ()).throw(ValueError("nonfinite JSON")))


def grid(tf0, tf_policy=None):
    require(tf0["OptionId"] == TF0 and tf0.get("UnitTfOptions") is None
            and tf0.get("TfWeight") == 0 and tf0.get("UnpublishedTermPolicy", 0) == 0,
            "historical TF0 is not the neutral control")
    result = [copy.deepcopy(tf0)]
    if tf_policy is not None:
        result[0].update(ContextPopulation=tf_policy["contextPopulation"],
                         UnpublishedOccurrenceCount=tf_policy["unpublishedOccurrenceCount"],
                         UnpublishedTermPolicy=tf_policy["unpublishedTermPolicy"])
    for values in itertools.product(STATES, repeat=4):
        if values == (0, 0, 0, 0):
            continue
        option = copy.deepcopy(tf0)
        option["OptionId"] = "TF4_" + "_".join(
            name + str(int(weight * 10)) for (_, _, name), weight in zip(SLOTS, values)) + "_A0.5_T0.95"
        option["UnitTfOptions"] = [
            {"Side": side, "Unit": unit, "Weight": weight, "Mode": 0}
            for (side, unit, _), weight in zip(SLOTS, values)]
        option["TfWeight"] = 0
        option["SharedSurnameMode"] = 0
        option["UnpublishedTermPolicy"] = (tf_policy["unpublishedTermPolicy"]
                                             if tf_policy is not None else 1)
        if tf_policy is not None:
            option["ContextPopulation"] = tf_policy["contextPopulation"]
            option["UnpublishedOccurrenceCount"] = tf_policy["unpublishedOccurrenceCount"]
        option["EligibleForSelection"] = True
        result.append(option)
    require(len(result) == OPTIONS and len({x["OptionId"] for x in result}) == OPTIONS,
            "incomplete or duplicate four-unit grid")
    require(len({tuple(slot["Weight"] for slot in x["UnitTfOptions"]) for x in result[1:]})
            == OPTIONS - 1, "missing TF combinations")
    return result


def prepare(base_plan_path, base_receipt_path, output_path, receipt_path, tf_policy_path=None, expected_people=None):
    tf_policy_bytes = Path(tf_policy_path).read_bytes() if tf_policy_path else None
    tf_config = strict_json(tf_policy_bytes) if tf_policy_bytes else None
    tf_policy = (tf_config.get("tfNominalPolicy", tf_config)
                 if tf_config is not None else None)
    if tf_config is not None and "tfNominalPolicy" in tf_config:
        require(type(tf_config.get("syntheticPopulation")) is int
                and tf_config["syntheticPopulation"] > 0
                and isinstance(tf_config.get("sampling"), dict)
                and all(type(tf_config["sampling"].get(k)) in (int, float)
                        for k in ("trainPercent", "validationPercent", "testPercent"))
                and all(0 <= tf_config["sampling"][k] <= 100
                        for k in ("trainPercent", "validationPercent", "testPercent"))
                and sum(tf_config["sampling"][k]
                        for k in ("trainPercent", "validationPercent", "testPercent")) == 100,
                "invalid unified calibration population/sampling configuration")
    if tf_policy is not None:
        require(tf_policy.get("schema") == "V9_TF_NOMINAL_POLICY_V1"
                and type(tf_policy.get("contextPopulation")) is int
                and type(tf_policy.get("unpublishedOccurrenceCount")) is int
                and 1 <= tf_policy["unpublishedOccurrenceCount"] <= tf_policy["contextPopulation"]
                and type(tf_policy.get("unpublishedTermPolicy")) is int
                and tf_policy["unpublishedTermPolicy"] == 0
                and tf_policy.get("nominalUnits") == [
                    "PERSON_FIRST", "PERSON_LAST", "MOTHER_FIRST", "MOTHER_LAST"]
                and tf_policy.get("uEstimation") == "TRAIN_NONMATCH_LAPLACE_ALPHA",
                "invalid or incompatible versioned TF policy")
    output_path, receipt_path = Path(output_path), Path(receipt_path)
    require(output_path.name == "plan.json" and not output_path.exists()
            and not receipt_path.exists(), "never overwrite a frozen study")
    source_bytes = Path(base_plan_path).read_bytes()
    base = strict_json(source_bytes)
    source_receipt = strict_json(Path(base_receipt_path).read_bytes())
    source_sha = digest(source_bytes)
    configured_people = (tf_config["syntheticPopulation"]
                         if tf_config is not None and "tfNominalPolicy" in tf_config
                         else 100000)
    # Workflow --people overrides the configured default; the effective value
    # must match the physical source receipt and be frozen in the study evidence.
    require(expected_people is None or (type(expected_people) is int and expected_people > 0),
            "invalid effective population")
    expected_people = configured_people if expected_people is None else expected_people
    c = base["Calibration"]
    require(source_receipt.get("schema") == BASE_SCHEMA
            and source_receipt.get("technicalPlanSha256") == source_sha
            and source_receipt.get("physicalEvaluations") == 24
            and source_receipt.get("people") == expected_people
            and source_receipt.get("knownTrainSeed") == 42
            and source_receipt.get("testUsed") is False
            and source_receipt.get("certified") is False
            and source_receipt.get("activated") is False
            and c["MaximumParallelRuleSets"] == 0
            and source_receipt.get("runtimeParallelismMode") == "AUTO_0"
            and base["MaximumBatches"] == 1
            and c["ConflictMargins"] == [0.5]
            and [x["OptionId"] for x in c["ModelOptions"]]
                == [TF0, "TF_ONE_MF_W1_M0_A0.5_T0.95"]
            and [x["Method"] for x in base["MuEstimationMethods"]]
                == ["DEFAULT", "CLASSWISE_SUPPORT_BACKOFF",
                    "CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE"]
            and base["PinnedIbgeBootstrap"]["People"] == 30000
            and c["Budget"]["MaximumObservedAcceptedFdrPoint"] == 0.01
            and c["SelectionPolicy"]["MaximumAcceptedFdrUpper"] == 0.01
            and c["SelectionPolicy"]["MaximumP95Milliseconds"] == 60000,
            "100k source plan, provenance, policies or resource guard changed")

    plan = copy.deepcopy(base)
    cp = plan["Calibration"]
    cp["ModelOptions"] = grid(c["ModelOptions"][0], tf_policy)
    mixture = copy.deepcopy(base["MuEstimationMethods"][2])
    require(mixture["Method"] == "CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE"
            and mixture["Parameters"]["ClassMassCoverage"] == [0.99]
            and mixture["Parameters"]["MinimumObservedMPerCell"] == [5]
            and mixture["Parameters"]["MinimumObservedUPerCell"] == [5]
            and mixture["Parameters"]["RetainSupportedTail"] == [True],
            "V3 M/U baseline policy changed")
    mixture.pop("Role", None)  # no V2 witness/anchor in this independent V3-only study
    mixture["Parameters"]["UBackoffJointMixtureWeights"] = list(LAMBDAS)
    plan["MuEstimationMethods"] = [mixture]
    cp["ConflictMargins"] = list(MARGINS)
    require(len(cp["BaselineRuleSets"]) == 1
            and len(cp["BaselineRuleSets"][0]) == 3,
            "frozen 3-pass blocking control missing")
    three_pass = copy.deepcopy(cp["BaselineRuleSets"][0])
    cp["DeclaredSearchSpace"] = {
        "Catalogue": copy.deepcopy(three_pass),
        "MinimumPasses": 3, "MaximumPasses": 3}
    cp["BaselineRuleSets"] = [three_pass]
    cp["MaximumPhysicalRuleSets"] = 1
    cp["MaximumComparedConfigurations"] = EVALUATIONS
    plan["MaximumSearchConfigurations"] = EVALUATIONS
    plan["PredeclaredCandidateCpMultiplicity"] = EVALUATIONS
    plan["MaximumBatches"] = 1  # one physical campaign, no intervening selection/refinement
    # Keep alpha/threshold, risk, fanout, P95, TRAIN split and IBGE reference unchanged.
    snapshot = {
        "schema": SCHEMA, "baseTechnicalPlanSha256": source_sha,
        "baseStudyFingerprintSha256": c["StudyFingerprintSha256"],
        "technicalCorpusManifestSha256": source_receipt["technicalCorpusManifestSha256"],
        "technicalCorpusObservationsSha256": source_receipt["technicalCorpusObservationsSha256"],
        "runtimeParallelismMode": "AUTO_0",
        "configuredSyntheticPopulation": configured_people,
        "effectiveSyntheticPopulation": expected_people,
        "populationOverrideApplied": expected_people != configured_people,
        "configurations": [x["OptionId"] for x in cp["ModelOptions"]],
        "weights": list(STATES), "uMixtureLambdas": list(LAMBDAS),
        "conflictMargins": list(MARGINS), "blockingPasses": three_pass,
        "maximumBatches": 1, "physicalHypotheses": EVALUATIONS,
        "tfUnpublishedPolicy": ("IMPUTE_CONFIGURED" if tf_policy is not None
                                else "ABSTAIN_CANDIDATE_WHEN_POSITIVE"),
        **({"tfPolicySha256": digest(tf_policy_bytes), "tfPolicy": tf_policy,
            "calibratorConfiguration": tf_config}
           if tf_policy is not None else {}),
        "testUsed": False, "activated": False}
    fingerprint = digest(json.dumps(snapshot, sort_keys=True, separators=(",", ":"),
                                    allow_nan=False).encode())
    cp["StudyFingerprintSha256"] = fingerprint
    cp["Budget"]["StudyFingerprintSha256"] = fingerprint
    raw = (json.dumps(plan, indent=2, allow_nan=False) + "\n").encode()
    receipt = {
        **snapshot, "studyFingerprintSha256": fingerprint,
        "planSha256": digest(raw), "sourceArtifactId": source_receipt["sourceArtifactId"],
        "baseSourceArtifactZipSha256": source_receipt["sourceArtifactZipSha256"],
        "tfOptions": OPTIONS, "v3Variants": len(LAMBDAS),
        "margins": len(MARGINS), "selectionCandidates": EVALUATIONS,
        "certified": False, "physicalExecution": "NOT_STARTED"}
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("xb") as stream:
        stream.write(raw)
    with receipt_path.open("xb") as stream:
        stream.write((json.dumps(receipt, indent=2, allow_nan=False) + "\n").encode())
    return receipt


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for arg in ("base-plan", "base-receipt", "plan", "receipt"):
        parser.add_argument("--" + arg, type=Path, required=True)
    parser.add_argument("--tf-policy", type=Path)
    parser.add_argument("--expected-people", type=int)
    p = parser.parse_args()
    print(json.dumps(prepare(p.base_plan, p.base_receipt, p.plan, p.receipt, p.tf_policy,
                               p.expected_people),
                     sort_keys=True))
