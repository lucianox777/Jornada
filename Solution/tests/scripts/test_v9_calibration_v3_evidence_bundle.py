"""V3 uses V2 flat evidence layout/manifest. No SQL, Terraform or recalibration."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location(
    "v9_v3_evidence_bundle", ROOT / "scripts" / "v9-calibration-v3-evidence-bundle.py")
bundle = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bundle)


def write(path, obj):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return hashlib.sha256(path.read_bytes()).hexdigest()


class V3EvidenceBundleTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        home = Path(tmp.name)
        self.root = home
        self.source = home / "source"
        self.source.mkdir()
        self.output = home / "bundle"
        self.refs = home / "pinned-source.json"
        self.run = 42137399700
        self.commit = "a" * 40
        self.artifact = "v9-v3-direct-train-example"
        self.csv_sha = "b" * 64
        self.source_manifest = "c" * 64
        self.grid = {
            "ClassMassCoverage": [.95, .99],
            "MinimumObservedMPerCell": [5],
            "MinimumObservedUPerCell": [5],
            "RetainSupportedTail": [True],
        }
        self.plan = {
            "Purpose": "BOOTSTRAP_CANDIDATE_SELECTION",
            "MuEstimationMethods": [{
                "Method": "CLASSWISE_SUPPORT_BACKOFF",
                "Parameters": self.grid,
            }],
            "Calibration": {
                "StudyFingerprintSha256": "d" * 64,
                "ObservationCapDeclaration": {"ObservationsSha256": self.csv_sha},
            },
            "PinnedIbgeReferenceVersionId": 7,
            "PinnedIbgePublishedSha256": "e" * 64,
        }
        write(self.source / "plan.json", self.plan)
        self.fit = {
            "SchemaVersion": "V9_FITTED_SNAPSHOT_V3",
            "OptionId": "TF_ONE_MF_W1_M0_A0.5_T0.95__MU_CLASSWISE_SUPPORT_BACKOFF_C0.95_M5_U5_T1",
            "Threshold": .95, "ConflictMargin": .5,
            "Parameters": {"prior": .51, "m.birth": .8, "u.birth": .03},
            "BlockingPasses": [{"PassId": "P_LAST_M_PHON", "Fields": ["name", "mother"]}],
            "JointCells": [{"Person": i, "Mother": j, "M": 1/81, "U": 1/81}
                           for i in range(9) for j in range(9)],
            "TrainCorpusSha256": "1" * 64,
            "ValidationCorpusSha256": "2" * 64,
            "TrainIdentityGroupsSha256": "3" * 64,
            "ValidationIdentityGroupsSha256": "4" * 64,
            "BlockingFrameFingerprintSha256": "5" * 64,
        }
        temp_fit = self.source / "snapshot.json"
        self.fitted_sha = write(temp_fit, self.fit)
        self.fitted_path = self.source / (self.fitted_sha + ".fitted.json")
        temp_fit.rename(self.fitted_path)
        self.mu_policy = {"ClassMassCoverage": .95, "MinimumM": 5, "MinimumU": 5,
                          "SupportedTailRule": "RETAIN_CLASS_JOINT_WHEN_COUNT_AT_LEAST_K_V2"}
        self.row = {
            "MuEstimationMethod": "CLASSWISE_SUPPORT_BACKOFF",
            "OptionId": self.fit["OptionId"],
            "ConflictMargin": .5,
            "fitSnapshotSha256": self.fitted_sha,
            "MuPolicy": self.mu_policy,
            "trueAccepted": 88, "falseAccepted": 1, "eligibleSources": 100,
            "recall": .88, "fdrObserved": 1 / 89,
            "conditionalClusterCpFdrUpper": .1,
            "clusterIidIndependentlyValidated": False,
            "smoothingAlpha": .5, "threshold": .95,
            "conflicts": 10, "unresolved": 12, "p95Milliseconds": 917.5,
            "Rejections": ["OUT_OF_BUDGET"],
            "outcome": "MEASURED_NOT_ELIGIBLE",
            "estimatorJointSupport": {"Cells": [{} for _ in range(81)]},
            "observedSupportAudit": {"Cells": [{} for _ in range(81)]},
        }
        self.report = {
            "schema": "V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1",
            "PlanSha256": "6" * 64,
            "originalMethodsAndParameterArrays": self.plan["MuEstimationMethods"],
            "declaredVariantCount": 2, "evaluations": 1, "measured": 1,
            "rejectedOrIneligible": 1,
            "scope": "TRAIN_SELECTION_ONLY_NOT_INDEPENDENT_VALIDATION",
            "conditionalFdrBoundsNotCertified": True,
            "testUsed": False, "activated": False,
            "finalistSnapshotShas": [self.fitted_sha],
            "StopReason": "BATCH_BUDGET_EXHAUSTED_NOT_OPTIMALITY",
            "rows": [self.row],
        }
        write(self.source / "mu-statistical-analysis.json", self.report)
        envelope = {
            "result": {
                "PlanSha256": self.report["PlanSha256"],
                "StopReason": self.report["StopReason"],
                "FinalistSnapshotShas": [self.fitted_sha],
                "ExternalValidationUsed": False, "TestUsed": False, "Activated": False,
            },
            "statisticalCertification": False, "operationalActivation": False,
            "originalObservationsSha256": self.csv_sha,
            "producerManifestSha256": self.source_manifest,
        }
        raw_sha = write(self.source / "temp.selection.json", envelope)
        self.selection_path = self.source / (raw_sha + ".selection.json")
        (self.source / "temp.selection.json").rename(self.selection_path)
        self.selection_sha = raw_sha
        self.selection = {
            "envelopeSha256": raw_sha,
            "FinalistSnapshotShas": [self.fitted_sha],
            "StopReason": self.report["StopReason"],
            "externalValidationUsed": False, "testUsed": False, "activated": False,
        }
        write(self.source / "selection-reference.json", self.selection)
        self.update_refs()

    def update_refs(self):
        write(self.refs, {
            "schema_version": bundle.SOURCE_REF_SCHEMA,
            "run_id": str(self.run),
            "head_sha": self.commit,
            "artifact_name": self.artifact,
            "files": {
                "plan.json": bundle.sha(self.source / "plan.json"),
                self.selection_path.name: bundle.sha(self.selection_path),
                self.fitted_path.name: bundle.sha(self.fitted_path),
                "selection-reference.json": bundle.sha(self.source / "selection-reference.json"),
                "mu-statistical-analysis.json": bundle.sha(self.source / "mu-statistical-analysis.json"),
            },
        })

    def build(self, output=None, references=None):
        return bundle.build(self.source, output or self.output, self.fitted_sha, .5,
                            self.run, self.commit, self.artifact, references)

    def test_v2_layout_and_hash_linked_statistical_report(self):
        manifest = self.build(references=self.refs)
        restored, report = bundle.verify(self.output, self.refs)
        self.assertEqual(restored, manifest)
        self.assertEqual(set(p.name for p in self.output.iterdir()),
                         set(bundle.FILE_MAP.values()) | {"manifest.json"})
        self.assertEqual(set(manifest["Components"]), set(bundle.FILE_MAP))
        self.assertEqual(report["StatisticalAnalysis"]["rows"][0]["trueAccepted"], 88)
        self.assertEqual(len(report["StatisticalAnalysis"]["rows"][0]
                             ["estimatorJointSupport"]["Cells"]), 81)
        self.assertEqual(report["PolicyAndMethod"]["muPolicy"], self.mu_policy)
        self.assertFalse(report["Status"]["Dt15Authorized"])
        self.assertEqual(manifest["SourceCommitSha"], self.commit)
        self.assertEqual(manifest["CalibrationRunId"], str(self.run))
        self.assertEqual(manifest["Components"]["fitted_model"]["sha256"], self.fitted_sha)
        self.assertEqual(manifest["CrossLinks"]["SelectionEnvelopeSha256"], self.selection_sha)
        self.assertTrue(bundle.valid_sha(bundle.sha(self.output / "manifest.json")))

    def test_directory_create_only_no_overwrite(self):
        self.build()
        with self.assertRaisesRegex(ValueError, "overwrite"):
            self.build()

    def test_fitted_snapshot_mutation_prevented_even_with_old_name(self):
        self.fitted_path.write_text("{}")
        with self.assertRaisesRegex(ValueError, "bytes differ"):
            self.build()
        self.assertFalse(self.output.exists())

    def test_v2_or_multiple_mu_methods_rejected(self):
        self.plan["MuEstimationMethods"].append({"Method": "DEFAULT"})
        write(self.source / "plan.json", self.plan)
        with self.assertRaisesRegex(ValueError, "V3-only"):
            self.build()

    def test_source_analysis_mismatch_rejected(self):
        self.report["PlanSha256"] = "f" * 64
        write(self.source / "mu-statistical-analysis.json", self.report)
        with self.assertRaisesRegex(ValueError, "selection"):
            self.build()

    def test_pinned_sha_blocks_changed_source(self):
        write(self.source / "plan.json", dict(self.plan, Foo="different"))
        with self.assertRaisesRegex(ValueError, "pinned"):
            self.build(references=self.refs)

    def test_recomputed_component_sha_cannot_silently_replace_metrics(self):
        self.build()
        report = json.loads((self.output / "report.json").read_text())
        report["SourceMetrics"]["fdrObserved"] = .99
        new_sha = write(self.output / "report.json", report)
        manifest = json.loads((self.output / "manifest.json").read_text())
        manifest["Components"]["report"]["sha256"] = new_sha
        manifest["Components"]["report"]["bytes"] = (self.output / "report.json").stat().st_size
        write(self.output / "manifest.json", manifest)
        with self.assertRaisesRegex(ValueError, "Reconstructed statistical report changed"):
            bundle.verify(self.output)

    def test_unknown_file_rejected(self):
        self.build()
        (self.output / "untracked.txt").write_text("not allowed")
        with self.assertRaisesRegex(ValueError, "Extra/untracked"):
            bundle.verify(self.output)

    def test_v2_hash_helpers_and_layout_reused(self):
        self.assertEqual(bundle.FILE_MAP["result"], "result.json")
        self.assertEqual(bundle.FILE_MAP["fitted_model"], "model.fitted.json")
        self.assertEqual(bundle.FILE_MAP["report"], "report.json")
        self.assertEqual(bundle.FILE_MAP["plan"], "plan.json")
        self.assertEqual(bundle.sha, bundle.v2.sha)


if __name__ == "__main__":
    unittest.main()
