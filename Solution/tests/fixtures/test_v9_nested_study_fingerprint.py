"""Plan-only V9 regression: frozen study identity must bind all inputs.

Synthetic tiny files here are NOT physical TRAIN 30k, TEST, or certification.
"""
import hashlib
import importlib.util
import json
import pathlib
import tempfile
import unittest

SCRIPT = pathlib.Path(__file__).resolve().parents[2] / "scripts" / "v9-nested-calibration-study.py"
spec = importlib.util.spec_from_file_location("v9_nested_fingerprint", SCRIPT)
nested = importlib.util.module_from_spec(spec)
spec.loader.exec_module(nested)


class V9NestedStudyFingerprintTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = pathlib.Path(self.temp.name)
        self.corpus = self.root / "observacoes.csv"
        self.corpus.write_bytes(b"PLAN-ONLY; NO PHYSICAL 30K EVIDENCE\n")
        self.manifest = self.root / "generation-manifest.json"
        self.manifest_data = {
            "error_profile": "clean",
            "population_profile": "DEMOGRAPHIC_PRIMARY_V1",
            "generator_version": "UNIT_TEST_GENERATOR_V1",
            "observation_multiplicity": {"contract_version": "UNIT_TEST_CAP_V1"},
        }
        self.manifest.write_text(json.dumps(self.manifest_data), encoding="utf-8")

    def pinned(self, suffix, published_hash):
        path = self.root / ("pinned-" + suffix + ".json")
        path.write_text(json.dumps({
            "PinnedIbgeReferenceVersionId": 1,
            "PinnedIbgePublishedSha256": published_hash * 64,
            "PinnedIbgeMaterializedFrequencySha256": "c" * 64,
            "PinnedIbgeBootstrap": {
                "Provenance": "SINTETICO_IBGE",
                "People": 30000,
                "CorpusFingerprint": hashlib.sha256(self.corpus.read_bytes()).hexdigest(),
                "GeneratorVersion": "UNIT_TEST_GENERATOR_V1",
                "GeneratorSeed": 42,
                "GenerationHypothesesFingerprint": "d" * 64,
            },
        }), encoding="utf-8")
        return path

    def prepare(self, name, *, fixture=False, tf_search=False, pinned=None):
        target = self.root / (name + ".json")
        nested.prepare(self.corpus, target, fixture=fixture,
                       tf_search=tf_search, pinned_ibge=pinned)
        return json.loads(target.read_text(encoding="utf-8"))

    def test_different_published_ibge_reference_must_change_frozen_study_identity(self):
        first = self.prepare("first", tf_search=True, pinned=self.pinned("a", "a"))
        second = self.prepare("second", tf_search=True, pinned=self.pinned("b", "b"))
        self.assertNotEqual(first["Calibration"]["StudyFingerprintSha256"],
                            second["Calibration"]["StudyFingerprintSha256"])
        for plan in (first, second):
            self.assertEqual(plan["Calibration"]["StudyFingerprintSha256"],
                             plan["Calibration"]["Budget"]["StudyFingerprintSha256"])
            self.assertEqual(len(plan["Calibration"]["ModelOptions"]), 41)

    def test_identical_inputs_reproduce_same_study_identity(self):
        pin = self.pinned("same", "a")
        first = self.prepare("first", tf_search=True, pinned=pin)
        second = self.prepare("second", tf_search=True, pinned=pin)
        self.assertEqual(first["Calibration"]["StudyFingerprintSha256"],
                         second["Calibration"]["StudyFingerprintSha256"])

    def test_manifest_change_changes_frozen_study_identity(self):
        first = self.prepare("before", fixture=True)
        self.manifest_data["generator_version"] = "UNIT_TEST_GENERATOR_V2"
        self.manifest.write_text(json.dumps(self.manifest_data), encoding="utf-8")
        second = self.prepare("after", fixture=True)
        self.assertNotEqual(first["Calibration"]["StudyFingerprintSha256"],
                            second["Calibration"]["StudyFingerprintSha256"])

    def test_invalid_bootstrap_metadata_is_rejected_before_plan_creation(self):
        pin = self.pinned("guard", "a")
        valid = json.loads(pin.read_text(encoding="utf-8"))
        invalid = (
            ("Provenance", "UNVERIFIED_SOURCE"),
            ("GeneratorSeed", True),
            ("GeneratorSeed", 1 << 64),
            ("GeneratorVersion", ["invalid-type"]),
            ("CorpusFingerprint", {"untrusted": "value"}),
        )
        for index, (field, value) in enumerate(invalid):
            with self.subTest(field=field, value=value):
                candidate = json.loads(json.dumps(valid))
                candidate["PinnedIbgeBootstrap"][field] = value
                pin.write_text(json.dumps(candidate), encoding="utf-8")
                destination = self.root / f"invalid-{index}.json"
                with self.assertRaises(ValueError):
                    nested.prepare(self.corpus, destination, tf_search=True,
                                   pinned_ibge=pin)
                self.assertFalse(destination.exists())

    def test_pinned_reference_without_tf_search_is_rejected_not_ignored(self):
        pin = self.pinned("unexpected", "a")
        destination = self.root / "unexpected-pin.json"
        with self.assertRaisesRegex(ValueError, "requires TF search"):
            nested.prepare(self.corpus, destination, pinned_ibge=pin)
        self.assertFalse(destination.exists())

    def test_duplicate_generation_manifest_key_fails_closed(self):
        original = self.manifest.read_text(encoding="utf-8")
        self.manifest.write_text(
            original.replace('"generator_version": "UNIT_TEST_GENERATOR_V1"',
                             '"generator_version": "UNIT_TEST_GENERATOR_V1", '
                             '"generator_version": "TAMPERED"'),
            encoding="utf-8")
        destination = self.root / "duplicate-manifest.json"
        with self.assertRaisesRegex(ValueError, "Duplicate JSON key"):
            nested.prepare(self.corpus, destination, fixture=True)
        self.assertFalse(destination.exists())

    def test_duplicate_nested_pinned_ibge_key_fails_closed(self):
        pin = self.pinned("duplicate-nested", "a")
        original = pin.read_text(encoding="utf-8")
        pin.write_text(
            original.replace('"GeneratorSeed": 42',
                             '"GeneratorSeed": 42, "GeneratorSeed": 43'),
            encoding="utf-8")
        destination = self.root / "duplicate-pinned.json"
        with self.assertRaisesRegex(ValueError, "Duplicate JSON key"):
            nested.prepare(self.corpus, destination, tf_search=True,
                           pinned_ibge=pin)
        self.assertFalse(destination.exists())

    def test_nonfinite_generation_manifest_constant_fails_closed(self):
        # Python's json parser accepts NaN/Infinity by default, although
        # neither token is legal JSON for a frozen REF/TRAIN contract.
        for token in ("NaN", "Infinity", "-Infinity"):
            with self.subTest(token=token):
                self.manifest.write_text(
                    json.dumps(self.manifest_data)[:-1] +
                    ', "extra_metric": ' + token + '}',
                    encoding="utf-8")
                destination = self.root / ("nonfinite-manifest-" + token + ".json")
                with self.assertRaisesRegex(ValueError, "Non-finite JSON"):
                    nested.prepare(self.corpus, destination, fixture=True)
                self.assertFalse(destination.exists())

    def test_nonfinite_pinned_reference_constant_fails_closed(self):
        pin = self.pinned("nonfinite", "a")
        pin.write_text(pin.read_text(encoding="utf-8")[:-1] +
                       ', "extra_metric": NaN}', encoding="utf-8")
        destination = self.root / "nonfinite-pinned.json"
        with self.assertRaisesRegex(ValueError, "Non-finite JSON"):
            nested.prepare(self.corpus, destination, tf_search=True,
                           pinned_ibge=pin)
        self.assertFalse(destination.exists())

    def test_missing_published_ibge_reference_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "pinned-ibge"):
            self.prepare("no-pin", tf_search=True)


if __name__ == "__main__":
    unittest.main()
