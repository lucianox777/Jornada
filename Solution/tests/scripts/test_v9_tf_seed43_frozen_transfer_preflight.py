#!/usr/bin/env python3
"""Offline negative tests for the physical seed-43 transfer preflight."""
import csv
import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SOURCE = Path(__file__).resolve().parents[2] / "scripts" / "v9-tf-seed43-frozen-transfer-preflight.py"
spec = importlib.util.spec_from_file_location("v9_seed43_preflight", SOURCE)
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


def sha(data):
    return hashlib.sha256(data).hexdigest()


class Seed43FrozenTransferTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.corpus = self.root / "generated"
        self.corpus.mkdir()
        with (self.corpus / "pessoas_verdade.csv").open("w", encoding="utf-8", newline="") as f:
            writer = csv.writer(f, lineterminator="\n")
            writer.writerow(["base_person_id", "particao"])
            for i in range(30000):
                writer.writerow([f"base-{i}", ["TRAIN", "VALIDATION", "TEST"][i % 3]])
        (self.corpus / "observacoes.csv").write_bytes(b"new-seed43-observations\n")
        (self.corpus / "gabarito.json").write_bytes(b'{"truth":"sealed-not-used"}\n')
        self.manifest = {
            "schema_version": 1, "generator_version": "JORNADA_SYNTH_CORPUS_CSHARP_V1",
            "seed": 43, "population_profile": "DEMOGRAPHIC_PRIMARY_V1",
            "error_profile": "clean", "reference_code": "CENSO2022_NOMES_BRASIL_V1",
            "input_fingerprint_sha256": "b" * 64,
            "observation_multiplicity": {
                "contract_version": "SYNTHETIC_BASE_OBSERVATION_MULTIPLICITY_V1",
                "maximum_observations_per_base_identity": 4,
            },
            "outputs": [
                {"path": p, "sha256": gate.sha256(self.corpus / p)}
                for p in sorted(gate.FILES)
            ],
        }
        self.manifest_path = self.corpus / "generation-manifest.json"
        self.save(self.manifest_path, self.manifest)
        self.registry = {
            "seed": 42, "population": 30000,
            "generation_manifest_sha256": "c" * 64,
            "generator_input_sha256": "d" * 64,
            "generated_files": [{"file": p, "sha256": "e" * 64} for p in gate.FILES],
            "ibge_reference": {
                "version_id_in_disposable_harness": 1,
                "published_sha256": "a" * 64,
                "materialized_tf_sha256": "f" * 64,
            }
        }
        self.registry_path = self.root / "registry.json"
        self.save(self.registry_path, self.registry)
        bootstrap, _, _ = gate.check_corpus(self.corpus, self.registry)
        self.pin = {
            "PinnedIbgeReferenceVersionId": 1,
            "PinnedIbgePublishedSha256": "a" * 64,
            "PinnedIbgeMaterializedFrequencySha256": "f" * 64,
            "PinnedIbgeBootstrap": bootstrap,
        }
        self.pin_path = self.root / "target-pin.json"
        self.save(self.pin_path, self.pin)
        self.snapshots = self.root / "snapshots"
        self.snapshots.mkdir()
        models = []
        for option, payload in (
            ("T0.95_TF0", b"frozen-tf0-snapshot"),
            ("TF_ONE_MF_W1_M0_A0.5_T0.95", b"frozen-one-mf-snapshot")
        ):
            digest = sha(payload)
            (self.snapshots / (digest + ".fitted.json")).write_bytes(payload)
            models.append({"option_id": option, "snapshot_sha256": digest})
        self.source_study = self.root / "source-study.json"
        self.source_study.write_bytes(b'{"physical":"frozen-seed42"}\n')
        self.plan = {
            "schema_version": gate.VERSION, "source_seed": 42, "target_seed": 43,
            "transfer_mode": "FROZEN_NO_REFIT", "source_commit_sha": "1" * 40,
            "source_study_sha256": gate.sha256(self.source_study), "source_run_id": 41681714081,
            "frozen_models": models, "target_fdr_max": 0.01,
            "test_sealed": True, "margins": [0.5, 2, 4],
        }
        self.plan_path = self.root / "plan.json"
        self.save(self.plan_path, self.plan)

    @staticmethod
    def save(path, value):
        path.write_text(json.dumps(value, separators=(",", ":")), encoding="utf-8")

    def verify(self):
        return gate.verify(self.plan_path, self.corpus, self.registry_path,
                           self.pin_path, self.snapshots, self.source_study)

    def test_success_proves_only_preflight_without_using_test_outcomes(self):
        report = self.verify()
        self.assertEqual(report["base_identities_by_partition"], {
            "TRAIN": 10000, "VALIDATION": 10000, "TEST": 10000
        })
        self.assertFalse(report["physical_sql_executed"])
        self.assertFalse(report["test_outcomes_inspected"])
        self.assertFalse(report["operational_authorization"])
        self.assertEqual(report["generated_output_sha256"]["gabarito.json"],
                         gate.sha256(self.corpus / "gabarito.json"))
        self.assertNotEqual(report["generator_manifest_sha256"],
                            self.registry["generation_manifest_sha256"])

    def test_rejects_unfrozen_or_retuned_models_and_changed_governance(self):
        self.plan["transfer_mode"] = "RECALIBRATE_ON_43"
        self.save(self.plan_path, self.plan)
        with self.assertRaisesRegex(ValueError, "frozen transfer protocol"):
            self.verify()
        self.plan["transfer_mode"] = "FROZEN_NO_REFIT"
        self.plan["target_fdr_max"] = 0.02
        self.save(self.plan_path, self.plan)
        with self.assertRaisesRegex(ValueError, "FDR target"):
            self.verify()
        self.plan["target_fdr_max"] = 0.01
        self.plan["test_sealed"] = False
        self.save(self.plan_path, self.plan)
        with self.assertRaisesRegex(ValueError, "protocol"):
            self.verify()
        self.plan["test_sealed"] = True
        self.save(self.plan_path, self.plan)
        snapshot = self.snapshots / (self.plan["frozen_models"][0]["snapshot_sha256"] + ".fitted.json")
        snapshot.write_bytes(b"TAMPER")
        with self.assertRaisesRegex(ValueError, "snapshot"):
            self.verify()

    def test_rejects_mutated_seed42_source_study(self):
        self.source_study.write_bytes(b"TAMPERED")
        with self.assertRaisesRegex(ValueError, "source study"):
            self.verify()

    def test_seed42_pin_cannot_be_used_for_seed43_and_reference_is_separate(self):
        self.pin["PinnedIbgeBootstrap"]["GeneratorSeed"] = 42
        self.save(self.pin_path, self.pin)
        with self.assertRaisesRegex(ValueError, "seed-43 bootstrap"):
            self.verify()
        self.pin["PinnedIbgeBootstrap"]["GeneratorSeed"] = 43
        self.pin["PinnedIbgeMaterializedFrequencySha256"] = "9" * 64
        self.save(self.pin_path, self.pin)
        with self.assertRaisesRegex(ValueError, "IBGE reference differs"):
            self.verify()

    def test_rejects_manifest_drift_and_cross_partition_duplicate_identity(self):
        self.manifest["seed"] = 42
        self.save(self.manifest_path, self.manifest)
        with self.assertRaisesRegex(ValueError, "Seed-43"):
            self.verify()
        self.manifest["seed"] = 43
        self.save(self.manifest_path, self.manifest)
        with (self.corpus / "pessoas_verdade.csv").open("a", encoding="utf-8") as stream:
            stream.write("base-0,TEST\n")
        self.manifest["outputs"] = [
            {"path": p, "sha256": gate.sha256(self.corpus / p)}
            for p in sorted(gate.FILES)
        ]
        self.save(self.manifest_path, self.manifest)
        with self.assertRaisesRegex(ValueError, "Duplicate base identity"):
            self.verify()


if __name__ == "__main__":
    unittest.main()
