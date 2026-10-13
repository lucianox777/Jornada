import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SOURCE = Path(__file__).resolve().parents[2] / "scripts" / "v9-synthetic-multiseed-gate.py"
SPEC = importlib.util.spec_from_file_location("v9_synthetic_gate", SOURCE)
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)


def write_run(root: Path, seed: int, truth="TRUTH", bad=False):
    root.mkdir(parents=True, exist_ok=True)
    outputs = []
    for filename in ("pessoas_verdade.csv", "observacoes.csv", "gabarito.json"):
        data = f"{filename}:{seed}:{truth}".encode("utf-8")
        (root / filename).write_bytes(data)
        outputs.append({"path": filename, "sha256": hashlib.sha256(data).hexdigest().upper()})
    manifest = {
        "population_profile": "DEMOGRAPHIC_PRIMARY_V1",
        "seed": seed,
        "input_fingerprint_sha256": f"INPUT-{seed}",
        "outputs": outputs
    }
    def metrics(partition, tp, fp, fn, tn, missing):
        return {
            "Partition": partition, "Tp": tp, "Fp": fp, "Fn": fn, "Tn": tn,
            "MatchedNotRetrieved": missing, "Recall": tp/(tp+fn), "Fpr": fp/(fp+tn),
            "Precision": tp/(tp+fp), "Fdr": fp/(tp+fp)
        }
    report = {
        "Version": gate.SCORER_VERSION, "Scope": gate.SCOPE,
        "Seed": seed, "People": 30000, "Observations": 58264,
        "InputFingerprint": f"INPUT-{seed}",
        "Scorer": "REAL_CSHARP_FS_PREVIEW_SINGLE_JOINT_NOMINAL_AND_BIRTH",
        "TermFrequencyStatus": "TF_WEIGHT_ZERO_IN_THIS_EXPERIMENT_NOT_FULL_TF_CALIBRATION",
        "OperationalConclusion": gate.UNKNOWN,
        "TrainMatchedRetrieved": 10974, "TrainUnmatchedSampled": 18039,
        "TrainCellsWithoutBilateralSupport": 59,
        "ValidationFprBudget": 0.01, "TestExploratoryRecallMinimum": 0.80,
        "FrozenThresholdLogOdds": "-1.61074823",
        "Validation": metrics("VALIDATION", 3531, 50, 77, 5963, 12),
        "Test": metrics("TEST", 3414, 39, 67, 5833, 15),
        "SyntheticProxyVerdict": gate.FAIL if bad else gate.PASS
    }
    (root / "generation-manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    (root / "v9-functional-probe.json").write_text(json.dumps(report), encoding="utf-8")
    return report


class V9IndependentSyntheticSeedsTest(unittest.TestCase):
    def test_three_replicas_have_provenance_and_pure_aggregate_verdict(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            rows = []
            for seed in (42, 43, 44):
                path = base / str(seed)
                write_run(path, seed)
                rows.append(gate.audit_directory(path, seed))
            report = gate.aggregate(rows)
            self.assertEqual(report["replicationCount"], 3)
            self.assertEqual(report["passedProxySeeds"], 3)
            self.assertEqual(report["crossSeedProxyVerdict"], "PASS_ALL_PROXY_REPLICATES")
            self.assertEqual(report["operationalConclusion"], gate.UNKNOWN)
            serial = json.dumps(report)
            self.assertNotIn("MARIA", serial)
            self.assertNotIn("motherName", serial)
            self.assertNotIn("cpf", serial)

    def test_a_failed_replica_is_never_overwritten_by_pass(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            rows = []
            for seed in (42, 43, 44):
                path = base / str(seed)
                write_run(path, seed)
                if seed == 43:
                    file = path / "v9-functional-probe.json"
                    doc = json.loads(file.read_text())
                    doc["Test"].update({"Tp": 600, "Fn": 2881, "Recall": 600/3481,
                                        "Precision": 600/639, "Fdr": 39/639})
                    doc["SyntheticProxyVerdict"] = gate.FAIL
                    file.write_text(json.dumps(doc))
                rows.append(gate.audit_directory(path, seed))
            report = gate.aggregate(rows)
            self.assertEqual(report["failedProxySeeds"], 1)
            self.assertEqual(report["crossSeedProxyVerdict"], "AT_LEAST_ONE_PROXY_REPLICATE_FAILED")

    def test_rejects_tampered_fpr_or_raw_truth(self):
        with tempfile.TemporaryDirectory() as temp:
            base=Path(temp)
            write_run(base, 42)
            doc=json.loads((base/"v9-functional-probe.json").read_text())
            doc["Test"]["Fpr"]=0
            (base/"v9-functional-probe.json").write_text(json.dumps(doc))
            with self.assertRaisesRegex(ValueError, "Metric differs"):
                gate.audit_directory(base, 42)
            write_run(base,42)
            (base/"gabarito.json").write_text("CHANGED")
            with self.assertRaisesRegex(ValueError, "Digest mismatch"):
                gate.audit_directory(base,42)

    def test_rejects_self_certification_crossed_splits_and_repeated_seed(self):
        with tempfile.TemporaryDirectory() as temp:
            base=Path(temp)
            write_run(base,42)
            doc=json.loads((base/"v9-functional-probe.json").read_text())
            doc["OperationalConclusion"]="CERTIFIED"
            (base/"v9-functional-probe.json").write_text(json.dumps(doc))
            with self.assertRaisesRegex(ValueError, "Unsafe assertion"):
                gate.audit_directory(base,42)
            write_run(base,42)
            doc=json.loads((base/"v9-functional-probe.json").read_text())
            doc["Test"]["Partition"]="TRAIN"
            (base/"v9-functional-probe.json").write_text(json.dumps(doc))
            with self.assertRaisesRegex(ValueError, "Missing or crossed"):
                gate.audit_directory(base,42)
            write_run(base,42)
            r=gate.audit_directory(base,42)
            with self.assertRaisesRegex(ValueError, "three distinct"):
                gate.aggregate([r,r,r])


if __name__=="__main__":
    unittest.main()
