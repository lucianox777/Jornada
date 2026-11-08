"""Fast offline contract tests for D's real SQL 1/0/8 driver."""
import copy
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-local-determinism-physical-smoke.py"
SPEC = importlib.util.spec_from_file_location("v9_d_parallel_physical", SCRIPT)
DRIVER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(DRIVER)


def write(path: Path, payload):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf8")


class DPhysicalDeterminismDriverTests(unittest.TestCase):
    def test_plans_vary_only_local_concurrency_and_never_mutate_source(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            science = {
                "StudyFingerprintSha256": "a" * 64,
                "ModelOptions": [{"OptionId": f"O{i}", "TfWeight": 0} for i in range(3)],
                "DeclaredSearchSpace": {"Catalogue": [{"PassId": "ONE", "Fields": ["name_prefix_1"]}]},
                "ConflictMargins": [.5, 2], "BaselineRuleSets": [],
                "Execution": {"EndToEndRepetitions": 20}, "MaximumParallelRuleSets": 0,
            }
            source = root / "nested.json"
            nested = {"Purpose": "PLUMBING_FIXTURE_NOT_CERTIFICATION", "Calibration": science}
            write(source, nested)
            original = source.read_bytes()
            out = root / "plans"
            plans = DRIVER.make_plans(source, out)
            self.assertEqual(set(plans), {"1", "0", "8"})
            self.assertEqual(source.read_bytes(), original)
            loaded = [DRIVER.read(out / f"plan-{n}.json") for n in DRIVER.MODES]
            for n, p in zip(DRIVER.MODES, loaded):
                self.assertEqual(p["MaximumParallelRuleSets"], n)
                self.assertEqual(p["Execution"]["EndToEndRepetitions"], 0)
            for p in loaded[1:]:
                self.assertEqual(
                    {k: v for k, v in p.items() if k != "MaximumParallelRuleSets"},
                    {k: v for k, v in loaded[0].items() if k != "MaximumParallelRuleSets"})

    def test_real_physical_receipt_requires_all_three_same_metrics_fits_and_pairs(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            plan = {"MaximumParallelRuleSets": 1}
            row = {"OptionId": "test", "ConflictMargin": .5, "CandidateGrouping": "BLIND",
                   "EligibleSourceObservationsBeforeBlocking": 100,
                   "TrueAcceptedSourceObservations": 20,
                   "FalseAcceptedSourceObservations": 1, "Conflicts": 2, "Unresolved": 3,
                   "ReasonsByResolveStatus": {"SEM_CANDIDATO": 3},
                   "BonferroniComparedConfigurations": 6,
                   "ConditionalClusterCpFdrUpper": .01}
            study = {"SourceMeasurements": [row],
                     "FrozenConfigurations": [{
                         "OptionId": "test", "ConflictMargin": .5,
                         "Snapshot": {"ContentSha256": "a" * 64}}],
                     "CandidatePairsSha256ByRuleset": {"P": "b" * 64},
                     "TrainInputSha256": "c" * 64, "ValidationInputSha256": "d" * 64,
                     "Pareto": {"ExploratorySuggestedOptionId": "test"}}
            digest = {"schema": "V9_SCIENTIFIC_RESULT_DIGEST_V1",
                      "sha256": "e" * 64, "TrainInputSha256": "c" * 64,
                      "ValidationInputSha256": "d" * 64}
            for mode in DRIVER.MODES:
                plan["MaximumParallelRuleSets"] = mode
                write(root / f"plan-{mode}.json", plan)
                out = root / f"results-{mode}"
                write(out / "scientific-result-digest.json", digest)
                write(out / "study.json", study)
                write(out / "runtime-diagnostics.json",
                      {"configuredParallelism": mode, "effectiveParallelism": mode or 2,
                       "peakWorkingSetBytes": 9999, "totalMilliseconds": 10 + mode})
            result = DRIVER.audit(root, "f" * 40)
            self.assertEqual(result["scientificDigestSha256"], "e" * 64)
            self.assertFalse(result["statisticallyCertified"])
            self.assertEqual(len(result["physicalModes"]), 3)
            self.assertNotEqual(result["physicalModes"][0]["runtimeTelemetrySha256"],
                                result["physicalModes"][1]["runtimeTelemetrySha256"])
            drift = copy.deepcopy(study)
            drift["SourceMeasurements"][0]["FalseAcceptedSourceObservations"] = 2
            write(root / "results-8/study.json", drift)
            (root / "determinism-physical-receipt.json").unlink()
            with self.assertRaisesRegex(ValueError, "scientificRowsSha256"):
                DRIVER.audit(root, "f" * 40)
            # A matching metric/fitted hash is still not sufficient if the
            # SQL candidate frame itself differs.
            write(root / "results-8/study.json", study)
            drift["SourceMeasurements"][0]["FalseAcceptedSourceObservations"] = 1
            drift["CandidatePairsSha256ByRuleset"] = {"P": "0" * 64}
            write(root / "results-8/study.json", drift)
            with self.assertRaisesRegex(ValueError, "candidatePairs"):
                DRIVER.audit(root, "f" * 40)


if __name__ == "__main__":
    unittest.main()
