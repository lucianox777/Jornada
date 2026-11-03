#!/usr/bin/env python3
"""Fast tests for the immutable V9 synthetic reference registry comparator."""
import importlib.util
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("v9_ref_gate", ROOT / "scripts/v9-tf-ref-provenance-gate.py")
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)
REGISTRY = ROOT / "data/reference/v9-synthetic-seed42-30000/manifest.json"


class V9SyntheticRefRegistryTests(unittest.TestCase):
    def setUp(self):
        self.r = json.loads(REGISTRY.read_text())
        pub = self.r["ibge_reference"]
        self.bootstrap = {
            "Provenance": "SINTETICO_IBGE",
            "People": 30000,
            "CorpusFingerprint": self.r["generated_files"][0]["sha256"].lower(),
            "GeneratorVersion": self.r["generator_version"],
            "GeneratorSeed": 42,
            "GenerationHypothesesFingerprint": self.r["generation_manifest_sha256"].lower()
        }
        self.p = {
            "PinnedIbgeReferenceVersionId": pub["version_id_in_disposable_harness"],
            "PinnedIbgePublishedSha256": pub["published_sha256"],
            "PinnedIbgeMaterializedFrequencySha256": pub["materialized_tf_sha256"],
            "PinnedIbgeBootstrap": self.bootstrap,
        }
        self.m = {
            "seed":42, "reference_code":pub["code"], "error_profile":"clean",
            "input_fingerprint_sha256":self.r["generator_input_sha256"],
            "outputs":[{"path":x["file"],"sha256":x["sha256"]} for x in self.r["generated_files"]]
        }

    def test_exact_reference_is_accepted(self):
        gate.validate(self.r,self.p,self.m,self.bootstrap)

    def test_changed_observations_are_rejected(self):
        broken = dict(self.m)
        broken["outputs"] = [dict(x) for x in self.m["outputs"]]
        broken["outputs"][1]["sha256"] = "D"*64
        with self.assertRaises(ValueError):
            gate.validate(self.r,self.p,broken,self.bootstrap)

    def test_modified_pin_and_changed_bootstrap_are_rejected(self):
        broken = dict(self.p, PinnedIbgePublishedSha256="E"*64)
        with self.assertRaises(ValueError):
            gate.validate(self.r,broken,self.m,self.bootstrap)
        b = dict(self.bootstrap, CorpusFingerprint="D"*64)
        with self.assertRaises(ValueError):
            gate.validate(self.r,self.p,self.m,b)


if __name__ == "__main__":
    unittest.main()
