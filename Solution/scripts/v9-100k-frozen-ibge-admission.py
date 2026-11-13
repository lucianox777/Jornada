#!/usr/bin/env python3
"""Verify the already frozen IBGE+synthetic-30k provenance without regenerating 30k.

The source IBGE projection stays immutable in the repo. This check binds the
100k plan's historical synthetic 30k *metadata* to its committed SHA registry.
The separate SQL bootstrap rehydrates those IBGE bytes into one-use SQL and
exports its physical pinned digest, which this tool verifies against the plan.
No 30k rows, external API, TEST, model fit, or IBGE data rewriting occurs.
"""
import argparse
import json
from pathlib import Path
import re

SHA = re.compile(r"^[0-9A-Fa-f]{64}$")
IBGE_CODE = "CENSO2022_NOMES_BRASIL_V1"
FIELDS = {"PinnedIbgeReferenceVersionId", "PinnedIbgePublishedSha256",
          "PinnedIbgeMaterializedFrequencySha256", "PinnedIbgeBootstrap"}
BOOTSTRAP_FIELDS = {"Provenance", "People", "CorpusFingerprint",
                    "GeneratorVersion", "GeneratorSeed",
                    "GenerationHypothesesFingerprint"}


def require(yes, why):
    if not yes:
        raise ValueError("V9_100K_FROZEN_IBGE_FAIL_CLOSED: " + why)


def object_unique(items):
    result = {}
    for key, value in items:
        require(key not in result, "duplicate JSON property")
        result[key] = value
    return result


def read_json(path):
    path = Path(path)
    require(path.is_file() and not path.is_symlink(), "missing regular metadata file")
    require(path.stat().st_size < 32 * 1024 * 1024, "metadata file too large")
    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=object_unique,
                      parse_constant=lambda _x: require(False, "nonfinite JSON"))


def canonical(expected):
    require(isinstance(expected, dict) and set(expected) == FIELDS,
            "pinned reference must contain exactly four fields")
    require(type(expected["PinnedIbgeReferenceVersionId"]) is int
            and expected["PinnedIbgeReferenceVersionId"] > 0, "invalid version ID")
    for key in ("PinnedIbgePublishedSha256", "PinnedIbgeMaterializedFrequencySha256"):
        require(isinstance(expected[key], str) and SHA.fullmatch(expected[key]),
                "invalid frozen IBGE digest")
    bootstrap = expected["PinnedIbgeBootstrap"]
    require(isinstance(bootstrap, dict) and set(bootstrap) == BOOTSTRAP_FIELDS,
            "invalid frozen synthetic bootstrap shape")
    return expected


def pinned_from_plan(plan, frozen_registry, ibge_manifest):
    require(isinstance(plan, dict), "invalid 100k plan")
    pinned = canonical({key: plan.get(key) for key in FIELDS})
    require(isinstance(frozen_registry, dict)
            and frozen_registry.get("schema_version") == "JORNADA_V9_SYNTHETIC_30K_REF_EVIDENCE_V1"
            and frozen_registry.get("origin") == "SINTETICO_IBGE"
            and frozen_registry.get("population") == 30000
            and frozen_registry.get("seed") == 42
            and frozen_registry.get("generator_version") == "JORNADA_SYNTH_CORPUS_CSHARP_V1"
            and frozen_registry.get("restrictions", {}).get("not_operational") is True
            and frozen_registry.get("restrictions", {}).get("raw_files_in_git") is False,
            "30k frozen registry contract invalid")
    pub = frozen_registry.get("ibge_reference")
    require(isinstance(pub, dict)
            and pub.get("code") == IBGE_CODE
            and type(pub.get("version_id_in_disposable_harness")) is int
            and isinstance(pub.get("published_sha256"), str)
            and SHA.fullmatch(pub["published_sha256"])
            and isinstance(pub.get("materialized_tf_sha256"), str)
            and SHA.fullmatch(pub["materialized_tf_sha256"]), "frozen IBGE registry invalid")
    require(isinstance(ibge_manifest, dict)
            and ibge_manifest.get("referenceCode") == IBGE_CODE
            and ibge_manifest.get("policy", {}).get("operationalSource") == "LOCAL_IMMUTABLE_SNAPSHOT"
            and ibge_manifest.get("policy", {}).get("remoteApiRequiredAtRuntime") is False
            and ibge_manifest.get("policy", {}).get("replaceInPlace") is False,
            "IBGE local immutable snapshot contract invalid")
    require(pinned["PinnedIbgeReferenceVersionId"] == pub["version_id_in_disposable_harness"]
            and pinned["PinnedIbgePublishedSha256"].upper() == pub["published_sha256"].upper()
            and pinned["PinnedIbgeMaterializedFrequencySha256"].upper()
            == pub["materialized_tf_sha256"].upper(), "plan diverges from pinned IBGE publication")
    files = frozen_registry.get("generated_files")
    require(isinstance(files, list) and len(files) == 3, "historical 30k registry incomplete")
    by_name = {item.get("file"): item.get("sha256") for item in files if isinstance(item, dict)}
    require(len(by_name) == 3
            and set(by_name) == {"pessoas_verdade.csv", "observacoes.csv", "gabarito.json"}
            and all(isinstance(v, str) and SHA.fullmatch(v) for v in by_name.values()),
            "historical frozen corpus SHA declarations invalid")
    expected_bootstrap = {
        "Provenance": "SINTETICO_IBGE",
        "People": 30000,
        "CorpusFingerprint": by_name["pessoas_verdade.csv"],
        "GeneratorVersion": frozen_registry["generator_version"],
        "GeneratorSeed": 42,
        "GenerationHypothesesFingerprint": frozen_registry.get("generation_manifest_sha256"),
    }
    require(isinstance(expected_bootstrap["GenerationHypothesesFingerprint"], str)
            and SHA.fullmatch(expected_bootstrap["GenerationHypothesesFingerprint"]),
            "historical generation manifest SHA invalid")
    bootstrap = pinned["PinnedIbgeBootstrap"]
    require(type(bootstrap["People"]) is int and bootstrap["People"] == 30000
            and type(bootstrap["GeneratorSeed"]) is int and bootstrap["GeneratorSeed"] == 42
            and bootstrap["Provenance"] == expected_bootstrap["Provenance"]
            and bootstrap["GeneratorVersion"] == expected_bootstrap["GeneratorVersion"]
            and all(isinstance(bootstrap[k], str) and bootstrap[k].upper() == expected_bootstrap[k].upper()
                    for k in ("CorpusFingerprint", "GenerationHypothesesFingerprint")),
            "frozen 30k bootstrap diverges; do not regenerate it")
    return pinned


def check(plan_path, registry_path, ibge_manifest_path):
    return pinned_from_plan(read_json(plan_path), read_json(registry_path),
                            read_json(ibge_manifest_path))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("bootstrap", "verify"))
    parser.add_argument("--plan", type=Path, required=True)
    parser.add_argument("--frozen-registry", type=Path, required=True)
    parser.add_argument("--ibge-manifest", type=Path, required=True)
    parser.add_argument("--bootstrap", type=Path)
    parser.add_argument("--actual-pin", type=Path)
    args = parser.parse_args()
    pinned = check(args.plan, args.frozen_registry, args.ibge_manifest)
    if args.operation == "bootstrap":
        require(args.bootstrap is not None and args.actual_pin is None,
                "bootstrap output required, physical pin must not exist yet")
        with args.bootstrap.open("x", encoding="utf-8") as out:
            json.dump(pinned["PinnedIbgeBootstrap"], out, indent=2)
            out.write("\n")
        print("V9_100K_FROZEN_IBGE: BOOTSTRAP_PINNED_NO_30K_GENERATION")
    else:
        require(args.bootstrap is None and args.actual_pin is not None,
                "physical SQL pin required for verification")
        actual = canonical(read_json(args.actual_pin))
        require(actual["PinnedIbgeReferenceVersionId"] == pinned["PinnedIbgeReferenceVersionId"]
                and all(actual[k].upper() == pinned[k].upper()
                        for k in ("PinnedIbgePublishedSha256",
                                  "PinnedIbgeMaterializedFrequencySha256"))
                and actual["PinnedIbgeBootstrap"] == pinned["PinnedIbgeBootstrap"],
                "physical SQL IBGE digest/bootstrap differs from frozen 100k plan")
        print("V9_100K_FROZEN_IBGE: SQL_PHYSICAL_SHA_VERIFIED_NO_30K_GENERATION")


if __name__ == "__main__":
    main()
