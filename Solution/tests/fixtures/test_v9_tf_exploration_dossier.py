"""V9 TF dossier contract: tiny synthetic fixtures, no 30k, TEST or operational certification."""
import importlib.util
import json
import pathlib
import unittest

SCRIPT = pathlib.Path(__file__).resolve().parents[2] / "scripts" / "v9-tf-exploration-dossier.py"
spec = importlib.util.spec_from_file_location("v9_tf_dossier", SCRIPT)
dossier = importlib.util.module_from_spec(spec)
spec.loader.exec_module(dossier)


def sample_study():
    def row(option, margin, tp, fp):
        return {
            "OptionId": "a" * 64 + ":" + option,
            "ConflictMargin": margin,
            "TrueAcceptedSourceObservations": tp,
            "FalseAcceptedSourceObservations": fp,
            "EligibleSourceObservationsBeforeBlocking": 100,
            "GlobalSourceRecall": tp / 100,
            "FdrAfterResolve": fp / (tp + fp),
            "ClusterIidIndependentlyValidated": False,
            "ReasonsByResolveStatus": {"RESOLVIDO": tp + fp}
        }
    rows = [row("T0.95_TF0", 0.5, 95, 0),
            row("TF_TWO_PF_MF_W1_M0_A0.5_T0.95", 0.5, 96, 1)]
    return {
        "Version": dossier.EXPECTED_STUDY,
        "PlanSha256": "b" * 64,
        "TrainInputSha256": "c" * 64,
        "ValidationInputSha256": "d" * 64,
        "PredeclaredComparedConfigurations": 3861,
        "SourceSelection": {
            "TestUsed": False,
            "SourceReport": {
                "TestUsed": False,
                "OperationalModelCreated": False,
                "AutomaticActivationAllowed": False,
                "Status": "NO_SOURCE_SUGGESTION_WITHOUT_SUPPORTED_CLUSTER_FDR_BOUND"
            }},
        "Pareto": {"Status": "NO_ELIGIBLE_CONFIGURATION"},
        "SourceMeasurements": rows,
        "FrozenConfigurations": [{
            "OptionId": r["OptionId"], "ConflictMargin": r["ConflictMargin"],
            "Snapshot": {"ContentSha256": (str(i + 1) * 64)}
        } for i, r in enumerate(rows)],
        "EndToEndMeasurements": [],
        "CaseLevelSensitiveData": "SENSITIVE TEST PERSON - MUST NOT LEAK"
    }


class V9TfExplorationDossierTests(unittest.TestCase):
    def generate(self, study):
        return dossier.summarize(study, "e" * 64, "f" * 40, 41681714081)

    def test_only_aggregate_metrics_and_no_certification(self):
        report, manifest = self.generate(sample_study())
        self.assertIn("96.0000%", report)
        self.assertIn("95.0000%", report)
        self.assertIn("NO_ELIGIBLE_CONFIGURATION", report)
        self.assertNotIn("SENSITIVE TEST PERSON", report)
        self.assertNotIn("CaseLevelSensitiveData", json.dumps(manifest))
        self.assertFalse(manifest["test_used"])
        self.assertFalse(manifest["activated"])
        self.assertEqual(manifest["measurement_count"], 2)

    def test_row_order_does_not_change_semantic_digests(self):
        a = sample_study()
        b = sample_study()
        b["SourceMeasurements"].reverse()
        b["FrozenConfigurations"].reverse()
        _, ma = self.generate(a)
        _, mb = self.generate(b)
        self.assertEqual(ma["metrics_semantic_sha256"], mb["metrics_semantic_sha256"])
        self.assertEqual(ma["snapshots_semantic_sha256"], mb["snapshots_semantic_sha256"])

    def test_changed_metric_changes_digest(self):
        a = sample_study()
        b = sample_study()
        b["SourceMeasurements"][1]["TrueAcceptedSourceObservations"] = 97
        b["SourceMeasurements"][1]["GlobalSourceRecall"] = 0.97
        _, ma = self.generate(a)
        _, mb = self.generate(b)
        self.assertNotEqual(ma["metrics_semantic_sha256"], mb["metrics_semantic_sha256"])

    def test_duplicate_measurement_rejected(self):
        a = sample_study()
        a["SourceMeasurements"].append(dict(a["SourceMeasurements"][0]))
        a["FrozenConfigurations"].append(dict(a["FrozenConfigurations"][0]))
        with self.assertRaisesRegex(ValueError, "Duplicate option"):
            self.generate(a)

    def test_missing_snapshot_rejected(self):
        a = sample_study()
        a["FrozenConfigurations"].pop()
        with self.assertRaisesRegex(ValueError, "cardinality"):
            self.generate(a)

    def test_test_label_usage_rejected(self):
        a = sample_study()
        a["SourceSelection"]["TestUsed"] = True
        with self.assertRaisesRegex(ValueError, "TEST"):
            self.generate(a)

    def test_activation_rejected(self):
        a = sample_study()
        a["SourceSelection"]["SourceReport"]["AutomaticActivationAllowed"] = True
        with self.assertRaisesRegex(ValueError, "Automatic activation"):
            self.generate(a)

    def test_incoherent_fdr_rejected(self):
        a = sample_study()
        a["SourceMeasurements"][1]["FdrAfterResolve"] = 0.0
        with self.assertRaisesRegex(ValueError, "Incoherent"):
            self.generate(a)

    def test_unsafe_identifier_rejected(self):
        a = sample_study()
        a["SourceMeasurements"][1]["OptionId"] = "PF|injected content"
        with self.assertRaisesRegex(ValueError, "Unsafe"):
            self.generate(a)

    def test_duplicate_json_keys_rejected(self):
        import tempfile
        with tempfile.TemporaryDirectory() as root:
            path = pathlib.Path(root) / "dup.json"
            path.write_text('{"k": 1, "k": 2}', encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Duplicate JSON"):
                dossier.strict_json(path)


if __name__ == "__main__":
    unittest.main()
