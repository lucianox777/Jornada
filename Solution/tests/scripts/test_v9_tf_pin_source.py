#!/usr/bin/env python3
"""Offline fail-closed checks for the preflight source/compare contract."""
import csv
import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SOURCE = Path(__file__).resolve().parents[2] / "scripts" / "v9-tf-pin-source.py"
spec = importlib.util.spec_from_file_location("v9_tf_pin_source", SOURCE)
pin = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pin)


def sha(data):
    return hashlib.sha256(data).hexdigest()


class PinSourceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        people = self.root / "pessoas_verdade.csv"
        with people.open("w", newline="", encoding="utf-8") as stream:
            writer = csv.writer(stream, lineterminator="\n")
            writer.writerow(["base_person_id", "particao"])
            for i in range(30000):
                writer.writerow([f"id-{i:05}", ("TRAIN", "VALIDATION", "TEST")[i % 3]])
        (self.root / "observacoes.csv").write_bytes(b"observations\n")
        truth = {
            "people": 30000, "seed": 42,
            "generator_version": "JORNADA_SYNTH_CORPUS_CSHARP_V1",
            "error_profile": "clean", "population_profile": "DEMOGRAPHIC_PRIMARY_V1",
            "input_fingerprint_sha256": "a" * 64,
        }
        (self.root / "gabarito.json").write_text(json.dumps(truth), encoding="utf-8")
        self.manifest = {
            "schema_version": 1, "seed": 42,
            "generator_version": "JORNADA_SYNTH_CORPUS_CSHARP_V1",
            "population_profile": "DEMOGRAPHIC_PRIMARY_V1", "error_profile": "clean",
            "reference_code": "CENSO2022_NOMES_BRASIL_V1",
            "input_fingerprint_sha256": "a" * 64,
            "observation_multiplicity": {
                "contract_version": "SYNTHETIC_BASE_OBSERVATION_MULTIPLICITY_V1",
                "maximum_observations_per_base_identity": 4,
            },
            "outputs": [
                {"path": name, "sha256": pin.file_sha256(self.root / name)}
                for name in ("pessoas_verdade.csv", "observacoes.csv", "gabarito.json")
            ],
        }
        self.write_manifest()

    def write_manifest(self):
        (self.root / "generation-manifest.json").write_text(
            json.dumps(self.manifest), encoding="utf-8")

    def test_bootstrap_is_sha_bound_and_no_private_rows_exported(self):
        result = pin.corpus_bootstrap(self.root)
        self.assertEqual(result["People"], 30000)
        self.assertEqual(result["GeneratorSeed"], 42)
        self.assertEqual(result["CorpusFingerprint"],
                         pin.file_sha256(self.root / "pessoas_verdade.csv"))
        self.assertEqual(result["GenerationHypothesesFingerprint"],
                         pin.file_sha256(self.root / "generation-manifest.json"))
        self.assertEqual(set(result), {
            "People", "GeneratorSeed", "CorpusFingerprint", "GeneratorVersion",
            "GenerationHypothesesFingerprint", "Provenance",
        })

    def test_modified_or_wrong_source_is_rejected(self):
        (self.root / "observacoes.csv").write_bytes(b"changed\n")
        with self.assertRaisesRegex(ValueError, "hash mismatch"):
            pin.corpus_bootstrap(self.root)
        self.manifest["seed"] = 43
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "Unsupported"):
            pin.corpus_bootstrap(self.root)

    def test_manifest_and_pin_duplicates_and_nonfinite_are_rejected(self):
        bad = self.root / "bad.json"
        for contents in ('{"seed":42,"seed":43}', '{"x":NaN}', '{"x":Infinity}'):
            bad.write_text(contents, encoding="utf-8")
            with self.assertRaises(ValueError):
                pin.parse_json(bad)

    def test_comparison_requires_all_pinned_fields_identical(self):
        bootstrap = pin.corpus_bootstrap(self.root)
        baseline = {
            "PinnedIbgeReferenceVersionId": 1,
            "PinnedIbgePublishedSha256": "a" * 64,
            "PinnedIbgeMaterializedFrequencySha256": "b" * 64,
            "PinnedIbgeBootstrap": bootstrap,
        }
        self.assertTrue(pin.verify_pin(baseline, dict(baseline)))
        for field, changed in (
            ("PinnedIbgeReferenceVersionId", 2),
            ("PinnedIbgePublishedSha256", "c" * 64),
            ("PinnedIbgeMaterializedFrequencySha256", "d" * 64),
        ):
            with self.assertRaises(ValueError):
                pin.verify_pin(baseline, {**baseline, field: changed})
        with self.assertRaises(ValueError):
            pin.verify_pin(baseline, {**baseline,
                "PinnedIbgeBootstrap": {**bootstrap, "CorpusFingerprint": "e" * 64}})


if __name__ == "__main__":
    unittest.main()
