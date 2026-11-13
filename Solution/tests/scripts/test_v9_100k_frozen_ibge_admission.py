"""Guard immutable 100k IBGE rehydration, never regenerate synthetic 30k."""
import importlib.util
import json
import subprocess
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "scripts/v9-100k-frozen-ibge-admission.py"
SCRIPT = ROOT / "scripts/v9-100k-load-frozen-ibge.sh"
WORKFLOW = ROOT.parent / ".github/workflows/v9-v3-four-name-100k.yml"
REGISTRY = ROOT / "data/reference/v9-synthetic-seed42-30000/manifest.json"
IBGE_MANIFEST = ROOT / "data/reference/ibge-nomes-2022/manifest.json"
spec = importlib.util.spec_from_file_location("frozen_100k_ibge", APP)
admission = importlib.util.module_from_spec(spec)
spec.loader.exec_module(admission)


def snapshot():
    registry = json.loads(REGISTRY.read_text())
    pub = registry["ibge_reference"]
    bootstrap = {
        "Provenance": "SINTETICO_IBGE",
        "People": 30000,
        "CorpusFingerprint": next(x["sha256"] for x in registry["generated_files"]
                                  if x["file"] == "pessoas_verdade.csv"),
        "GeneratorVersion": registry["generator_version"],
        "GeneratorSeed": 42,
        "GenerationHypothesesFingerprint": registry["generation_manifest_sha256"],
    }
    return {
        "PinnedIbgeReferenceVersionId": pub["version_id_in_disposable_harness"],
        "PinnedIbgePublishedSha256": pub["published_sha256"],
        "PinnedIbgeMaterializedFrequencySha256": pub["materialized_tf_sha256"],
        "PinnedIbgeBootstrap": bootstrap,
    }


def save(file, value):
    file.write_text(json.dumps(value), encoding="utf-8")


class FrozenIbge100kAdmission(unittest.TestCase):
    def test_pinned_existing_reference_accepted_without_30k_corpus(self):
        expected = snapshot()
        plan = {"Purpose": "BOOTSTRAP_CANDIDATE_SELECTION", **expected}
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            file = root / "plan.json"
            save(file, plan)
            self.assertEqual(admission.check(file, REGISTRY, IBGE_MANIFEST), expected)
            self.assertEqual(list(root.iterdir()), [file])

    def test_drift_of_pinned_identity_or_historical_bootstrap_fails_closed(self):
        for field, value in (
            ("PinnedIbgePublishedSha256", "A" * 64),
            ("PinnedIbgeMaterializedFrequencySha256", "B" * 64),
            ("PinnedIbgeReferenceVersionId", 999),
            ("PinnedIbgeBootstrap", {**snapshot()["PinnedIbgeBootstrap"],
                                     "GeneratorSeed": 43}),
        ):
            with self.subTest(field=field):
                plan = snapshot()
                plan[field] = value
                with self.assertRaisesRegex(ValueError, "diverges|bootstrap"):
                    admission.pinned_from_plan(plan, json.loads(REGISTRY.read_text()),
                                               json.loads(IBGE_MANIFEST.read_text()))

    def test_reloaded_sql_pin_must_match_original_pinned_plan_exactly(self):
        frozen = snapshot()
        self.assertTrue(admission.verify_physical_pin(frozen, dict(frozen)))
        for key, wrong in (
            ("PinnedIbgeReferenceVersionId", 999),
            ("PinnedIbgePublishedSha256", "0" * 64),
            ("PinnedIbgeMaterializedFrequencySha256", "1" * 64),
            ("PinnedIbgeBootstrap", {**frozen["PinnedIbgeBootstrap"],
                                     "CorpusFingerprint": "0" * 64}),
        ):
            with self.subTest(key=key):
                altered = {**frozen, key: wrong}
                with self.assertRaisesRegex(ValueError, "physical SQL IBGE"):
                    admission.verify_physical_pin(frozen, altered)

    def test_reject_source_manifest_allowing_mutations_or_remote_runtime_api(self):
        frozen = snapshot()
        m = json.loads(IBGE_MANIFEST.read_text())
        m["policy"]["remoteApiRequiredAtRuntime"] = True
        with self.assertRaisesRegex(ValueError, "immutable snapshot"):
            admission.pinned_from_plan(frozen, json.loads(REGISTRY.read_text()), m)
        m = json.loads(IBGE_MANIFEST.read_text())
        m["policy"]["replaceInPlace"] = True
        with self.assertRaisesRegex(ValueError, "immutable snapshot"):
            admission.pinned_from_plan(frozen, json.loads(REGISTRY.read_text()), m)

    def test_both_jobs_reuse_static_ibge_no_recreated_30k(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        train, validation = workflow.split("\n  validation-100k:\n", 1)
        self.assertIn('v9-100k-load-frozen-ibge.sh "$root/bound/plan.json"', train)
        self.assertIn('v9-100k-load-frozen-ibge.sh "$root/handoff/plan.json"', validation)
        self.assertNotIn('v9-tf-prepare-pin.sh', workflow)
        self.assertNotIn('"$root/reference-30k"', workflow)
        for source in (SCRIPT.read_text(), train, validation):
            self.assertNotIn("--people 30000", source)
        script = SCRIPT.read_text()
        self.assertIn("LOAD_NAME_FREQUENCY_SNAPSHOT", script)
        self.assertIn("v9-100k-frozen-ibge-admission.py bootstrap", script)
        self.assertIn("v9-100k-frozen-ibge-admission.py verify", script)
        self.assertIn("--v9-tf-export-ibge-pin", script)
        self.assertIn("20261103_Ref_Corpus_Sintetico_Evidencia_V9.sql", script)
        self.assertIn('GITHUB_ACTOR', script)
        self.assertIn('GITHUB_REF', script)
        self.assertIn('JORNADA_RUNTIME_MODE', script)
        self.assertIn('ConnectionStrings__Jornada', script)
        self.assertIn('physical-100k:', train)
        self.assertIn('needs: physical-100k', validation)
        self.assertIn('timeout-minutes: 315', train)
        self.assertIn('timeout-minutes: 330', validation)
        self.assertIn('--v9-validate-finalists', validation)
        self.assertIn('--seed 20261112001', validation)

    def test_sql_bootstrap_shell_syntax(self):
        subprocess.run(["bash", "-n", str(SCRIPT)], check=True)

    def test_other_30k_manual_workflows_still_use_original_provenance(self):
        old = ROOT / "scripts/v9-tf-prepare-pin.sh"
        self.assertIn("--people 30000", old.read_text())
        self.assertIn("v9-tf-ref-provenance-gate.py", old.read_text())


if __name__ == "__main__":
    unittest.main()
