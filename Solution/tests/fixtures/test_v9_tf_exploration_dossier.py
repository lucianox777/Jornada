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
    grid_spec = importlib.util.spec_from_file_location(
        "v9_tf_grid", SCRIPT.with_name("v9-tf-option-grid.py"))
    grid = importlib.util.module_from_spec(grid_spec)
    grid_spec.loader.exec_module(grid)
    arms = [opt["OptionId"] for opt in grid.option_grid()]
    arms[0] = "T0.95_TF0"
    rows = []
    for margin in (0.5, 2, 4):
        for arm in arms:
            leader = arm == "TF_TWO_PF_MF_W1_M0_A0.5_T0.95"
            example = row(arm, margin, 96 if leader else 95, 1 if leader else 0)
            example["BonferroniComparedConfigurations"] = 3849
            rows.append(example)
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
            "Snapshot": {"ContentSha256": f"{i+1:064x}"}
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
        self.assertEqual(manifest["measurement_count"], 135)
        self.assertEqual(manifest["statistical_family"]["historical_source_selector_family"], 3849)
        self.assertEqual(manifest["statistical_family"]["conservative_full_inference_family"], 3861)
        self.assertFalse(manifest["statistical_family"]["diagnostic_non_influence_independently_established"])

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
        b["SourceMeasurements"][1]["FdrAfterResolve"] = (
            b["SourceMeasurements"][1]["FalseAcceptedSourceObservations"]
            / (97 + b["SourceMeasurements"][1]["FalseAcceptedSourceObservations"]))
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
        a["SourceMeasurements"][1]["FdrAfterResolve"] = 0.3
        with self.assertRaisesRegex(ValueError, "Incoherent"):
            self.generate(a)

    def test_unsafe_identifier_rejected(self):
        a = sample_study()
        a["SourceMeasurements"][1]["OptionId"] = "PF|injected content"
        with self.assertRaisesRegex(ValueError, "Unsafe"):
            self.generate(a)

    def test_pareto_is_exploratory_and_does_not_certify(self):
        report, manifest = self.generate(sample_study())
        self.assertIn("Fronteiras Pareto descritivas", report)
        self.assertIn("TF_TWO_PF_MF_W1_M0", report)
        self.assertEqual(len(manifest["margin_results"]), 3)
        self.assertTrue(all(item["pareto_frontier"] for item in manifest["margin_results"]))
        self.assertFalse(manifest["operationally_certified"])

    def test_diagnostic_omission_with_balanced_snapshot_is_rejected(self):
        study = sample_study()
        index = next(i for i, r in enumerate(study["SourceMeasurements"])
                     if ":TF_SHARED_PL_ML_" in r["OptionId"])
        study["SourceMeasurements"].pop(index)
        study["FrozenConfigurations"].pop(index)
        with self.assertRaisesRegex(ValueError, "family omission"):
            self.generate(study)

    def test_margin_omission_fails_closed(self):
        study = sample_study()
        study["SourceMeasurements"] = [
            r for r in study["SourceMeasurements"] if r["ConflictMargin"] != 4]
        study["FrozenConfigurations"] = [
            r for r in study["FrozenConfigurations"] if r["ConflictMargin"] != 4]
        with self.assertRaisesRegex(ValueError, "family omission"):
            self.generate(study)

    def test_reduced_historical_multiplicity_rejected(self):
        study = sample_study()
        study["SourceMeasurements"][0]["BonferroniComparedConfigurations"] = 3848
        with self.assertRaisesRegex(ValueError, "FDR correction family"):
            self.generate(study)

    def test_double_counted_historical_multiplicity_rejected(self):
        study = sample_study()
        for row in study["SourceMeasurements"]:
            row["BonferroniComparedConfigurations"] = 3861
        with self.assertRaisesRegex(ValueError, "FDR correction family"):
            self.generate(study)

    def test_declared_exploration_family_cannot_shrink(self):
        study = sample_study()
        study["PredeclaredComparedConfigurations"] = 3849
        with self.assertRaisesRegex(ValueError, "physical budget"):
            self.generate(study)

    def test_duplicate_json_keys_rejected(self):
        import tempfile
        with tempfile.TemporaryDirectory() as root:
            path = pathlib.Path(root) / "dup.json"
            path.write_text('{"k": 1, "k": 2}', encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Duplicate JSON"):
                dossier.strict_json(path)


if __name__ == "__main__":
    unittest.main()
