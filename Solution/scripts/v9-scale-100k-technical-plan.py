#!/usr/bin/env python3
"""Bind immutable official J V9 parameters to an independently hashed 100k TRAIN corpus.

This changes ONLY corpus-bound provenance, never scientific options, TF pin,
historical plans/filenames, risk ceilings, or selection policies.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import re
import zipfile

ARTIFACT_SHA = "5bdbce7ffd5fed55fc085fde42faf9793cb39e6e79c3db4c69990a818bf330d7"
ARTIFACT_ID = 13654549483
ORIGINAL_SOURCE_SHA = "3da5e9327e86b5837f69a4da2236fab385ec930f"
EXPECTED_FAMILY = "V9_OFFICIAL_MULTIPARAM_V2_WITNESS_ANCHOR_V1"
SCHEMA = "V9_100K_MULTIPARAM_TECHNICAL_REBIND_V1"
OPTIONS = ("A0.5_T0.95_TF0", "TF_ONE_MF_W1_M0_A0.5_T0.95")
METHODS = ("DEFAULT", "CLASSWISE_SUPPORT_BACKOFF", "CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE")


def require(ok, msg):
    if not ok:
        raise ValueError("V9_100K_FAIL_CLOSED: " + msg)


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def file_sha(path):
    h = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(1048576), b""):
            h.update(chunk)
    return h.hexdigest()


def json_unique(raw):
    def pairs(values):
        out = {}
        for k, v in values:
            require(k not in out, "duplicate JSON member")
            out[k] = v
        return out
    def fail_const(value):
        raise ValueError("nonfinite JSON value")
    return json.loads(raw, object_pairs_hook=pairs, parse_constant=fail_const)


def artifact_file(archive, suffix):
    matches = [x for x in archive.namelist()
               if (x == suffix or x.endswith("/" + suffix))
               and "__MACOSX/" not in x and not x.startswith("/")]
    require(len(matches) == 1, "historical J artifact absent/ambiguous " + suffix)
    require(archive.getinfo(matches[0]).file_size < 8_000_000,
            "historical J aggregate unreasonably large")
    return archive.read(matches[0])


def verified_official_plan(zip_path):
    zip_path = Path(zip_path)
    require(file_sha(zip_path) == ARTIFACT_SHA, "historical J artifact ZIP drift")
    with zipfile.ZipFile(zip_path) as z:
        raw = artifact_file(z, "plan.json")
        receipt = json_unique(artifact_file(z, "receipt.json"))
        original = json_unique(artifact_file(z, "plan-receipt.json"))
    plan = json_unique(raw)
    require(receipt.get("sourceSha") == ORIGINAL_SOURCE_SHA
            and receipt.get("role") == "PRIMARY"
            and receipt.get("physicalEvaluations") == 24
            and receipt.get("witnessAndDiagnostics") == 16
            and receipt.get("cpCandidates") == 8
            and receipt.get("testUsed") is False
            and receipt.get("activated") is False
            and receipt.get("fileSha256", {}).get("plan") == digest(raw),
            "historical SHA-bound proof does not match")
    require(original.get("schema") == EXPECTED_FAMILY
            and original.get("marginRole") == "PRIMARY"
            and original.get("officialPlanSha256") == digest(raw)
            and original.get("legacyHistoricalParametersMutated") is False
            and original.get("testUsed") is False,
            "historical role-tagged official plan receipt invalid")
    c = plan["Calibration"]
    require([m["Method"] for m in plan["MuEstimationMethods"]] == list(METHODS)
            and plan["MuEstimationMethods"][0].get("Role") == "WITNESS"
            and plan["MuEstimationMethods"][0].get("Anchor") is True
            and all(m.get("Role") == "CANDIDATE" for m in plan["MuEstimationMethods"][1:])
            and [o["OptionId"] for o in c["ModelOptions"]] == list(OPTIONS)
            and plan["PredeclaredCandidateCpMultiplicity"] == 8
            and plan["MaximumBatches"] == 1
            and plan["MaximumSearchConfigurations"] == 24
            and c["MaximumComparedConfigurations"] == 24
            and c["ConflictMargins"] == [0.5]
            and len(c["BaselineRuleSets"]) == 1
            and c["Budget"]["MaximumObservedAcceptedFdrPoint"] == 0.01
            and c["SelectionPolicy"]["MaximumAcceptedFdrUpper"] == 0.01
            and plan["PinnedIbgeBootstrap"]["People"] == 30000
            and plan["PinnedIbgeBootstrap"]["GeneratorSeed"] == 42,
            "unrecognized official scientific configuration")
    return plan, digest(raw)


def corpus_provenance(root):
    root = Path(root)
    manifest_raw = (root / "generation-manifest.json").read_bytes()
    m = json_unique(manifest_raw)
    require(m.get("seed") == 42 and m.get("error_profile") == "clean"
            and m.get("population_profile") == "DEMOGRAPHIC_PRIMARY_V1"
            and m.get("generator_version") == "JORNADA_SYNTH_CORPUS_CSHARP_V1",
            "100k technical corpus profile or known TRAIN seed drift")
    cap = m.get("observation_multiplicity", {})
    require(cap.get("contract_version") == "SYNTHETIC_BASE_OBSERVATION_MULTIPLICITY_V1"
            and cap.get("maximum_observations_per_base_identity") == 4
            and cap.get("scope") == "BASE_CORPUS_ONLY_NOT_INGESTION_WAVES",
            "synthetic multiplicity cap differs from official V9")
    manifest_outputs = m.get("outputs")
    require(isinstance(manifest_outputs, list) and len(manifest_outputs) == 3,
            "required three generated files not declared")
    expected_names = {"pessoas_verdade.csv", "observacoes.csv", "gabarito.json"}
    outputs = {}
    for obj in manifest_outputs:
        require(isinstance(obj, dict) and set(obj) == {"path", "sha256"}
                and obj["path"] in expected_names and obj["path"] not in outputs
                and isinstance(obj["sha256"], str)
                and re.fullmatch("[a-fA-F0-9]{64}", obj["sha256"]),
                "generated file identity invalid")
        require(file_sha(root / obj["path"]) == obj["sha256"].lower(),
                "generated file SHA mismatch")
        outputs[obj["path"]] = obj["sha256"].lower()
    require(set(outputs) == expected_names, "generated output set incomplete")
    # Only aggregate declared people/seed are consumed; no TEST outcomes.
    truth = json_unique((root / "gabarito.json").read_bytes())
    require(type(truth.get("people")) is int and truth["people"] == 100000
            and truth.get("seed") == 42
            and truth.get("generator_version") == m["generator_version"]
            and truth.get("error_profile") == "clean",
            "100k population, known seed, or synthetic truth declaration drift")
    return m, digest(manifest_raw), outputs["observacoes.csv"]


def bind(zip_path, corpus_root, output, receipt_path, parallelism_mode="preserve"):
    output, receipt_path = Path(output), Path(receipt_path)
    require(output.name == "plan.json", "only official-format plan.json is executable")
    require(not output.exists() and not receipt_path.exists(),
            "refuse to overwrite existing plan or receipt")
    original, official_sha = verified_official_plan(zip_path)
    manifest, manifest_sha, obs_sha = corpus_provenance(corpus_root)
    plan = copy.deepcopy(original)
    require(parallelism_mode in ("preserve", "auto"), "unsupported runtime parallelism option")
    historical_parallelism = original["Calibration"].get("MaximumParallelRuleSets")
    require(type(historical_parallelism) is int and historical_parallelism >= 0,
            "historical parallelism must be an unsigned integer")
    # Runtime-only: V9 CSharp governor chooses workers from CPUs and live RAM.
    # The model candidates, TF/MU, FDR, reference and 24 hypotheses do not change.
    if parallelism_mode == "auto":
        plan["Calibration"]["MaximumParallelRuleSets"] = 0
    # Explicit, audited operational policy correction: missing published TF
    # abstains the affected candidate pair, never the entire TF+ hypothesis.
    # TF0 is the unchanged paired control; no fabricated IBGE frequencies.
    policy_changes = []
    for option in plan["Calibration"]["ModelOptions"]:
        option_id = option["OptionId"]
        if option_id == OPTIONS[1]:
            previous = option.get("UnpublishedTermPolicy", 0)
            require(previous in (0, 1), "unknown TF+ unpublished term policy")
            option["UnpublishedTermPolicy"] = 1
            policy_changes.append({"optionId": option_id, "previous": previous,
                                   "effective": 1})
        else:
            require(option_id == OPTIONS[0], "unexpected TF0 option")
            require(option.get("UnpublishedTermPolicy", 0) == 0,
                    "TF0 must remain unchanged")
    require(len(policy_changes) == 1, "exactly one TF+ model option required")
    old = plan["Calibration"]["ObservationCapDeclaration"]
    require(old["MaximumObservationsPerBaseIdentity"] == 4
            and old["GeneratorVersion"] == manifest["generator_version"]
            and old["ContractVersion"] == manifest["observation_multiplicity"]["contract_version"],
            "original observation contract cannot support 100k safely")
    proposal = {
        "schema": SCHEMA, "historicalOfficialPlanSha256": official_sha,
        "technicalCorpusManifestSha256": manifest_sha,
        "technicalCorpusObservationsSha256": obs_sha,
        "people": 100000, "knownTrainSeed": 42,
        "historicalPinnedIbgeBootstrapPeople": 30000,\n        "tfUnpublishedTermPolicyChanges": policy_changes,
    }
    if parallelism_mode == "auto":
        proposal.update(runtimeParallelismMode="AUTO_0",
                        historicalMaximumParallelRuleSets=historical_parallelism)
    fingerprint = digest(json.dumps(proposal, sort_keys=True,
                                    separators=(",", ":"), allow_nan=False).encode())
    cap = plan["Calibration"]["ObservationCapDeclaration"]
    cap["ManifestSha256"] = manifest_sha
    cap["ObservationsSha256"] = obs_sha
    plan["Calibration"]["StudyFingerprintSha256"] = fingerprint
    plan["Calibration"]["Budget"]["StudyFingerprintSha256"] = fingerprint
    # Strict diff proof: only the declared TF+ pair-abstention correction and\n    # corpus/runtime binding are permitted; all other science is immutable.
    # CPU setting, SQL rule, selection setting or historical value changed.
    restored = copy.deepcopy(plan)
    for change in policy_changes:\n        restored_option = next(o for o in restored["Calibration"]["ModelOptions"]\n                               if o["OptionId"] == change["optionId"])\n        original_option = next(o for o in original["Calibration"]["ModelOptions"]\n                               if o["OptionId"] == change["optionId"])\n        if "UnpublishedTermPolicy" in original_option:\n            restored_option["UnpublishedTermPolicy"] = original_option["UnpublishedTermPolicy"]\n        else:\n            restored_option.pop("UnpublishedTermPolicy", None)\n    if parallelism_mode == "auto":\n        restored["Calibration"]["MaximumParallelRuleSets"] = historical_parallelism
    restored["Calibration"]["ObservationCapDeclaration"] = copy.deepcopy(
        original["Calibration"]["ObservationCapDeclaration"])
    restored["Calibration"]["StudyFingerprintSha256"] = original["Calibration"]["StudyFingerprintSha256"]
    restored["Calibration"]["Budget"]["StudyFingerprintSha256"] = original["Calibration"]["Budget"]["StudyFingerprintSha256"]
    require(restored == original, "unexpected parameter beyond corpus provenance changed")
    raw = (json.dumps(plan, indent=2, allow_nan=False) + "\n").encode()
    receipt = {**proposal, "studyFingerprintSha256": fingerprint,
               "technicalPlanSha256": digest(raw), "physicalEvaluations": 24,
               "witnessAndDiagnostics": 16, "candidateCpMultiplicity": 8,
               "sourceArtifactId": ARTIFACT_ID, "sourceArtifactZipSha256": ARTIFACT_SHA,
               "testUsed": False, "certified": False, "activated": False,
               "physicalExecution": "NOT_STARTED"}
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("xb") as f:
        f.write(raw)
    with receipt_path.open("xb") as f:
        f.write((json.dumps(receipt, indent=2, allow_nan=False) + "\n").encode())
    return receipt


if __name__ == "__main__":
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("--artifact-zip", type=Path, required=True)
    cli.add_argument("--corpus-root", type=Path, required=True)
    cli.add_argument("--plan", type=Path, required=True)
    cli.add_argument("--receipt", type=Path, required=True)
    cli.add_argument("--parallelism", choices=("preserve", "auto"), default="preserve",
                     help="Only change runtime MaximumParallelRuleSets to 0 for the 100k technical fit")
    args = cli.parse_args()
    print(json.dumps(bind(args.artifact_zip, args.corpus_root,
                          args.plan, args.receipt, args.parallelism), sort_keys=True))
