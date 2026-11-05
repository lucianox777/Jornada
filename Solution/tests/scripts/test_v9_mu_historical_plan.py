"""Offline regression: historical M/U arrays, V2/V3, TF ONE_MF and budget remain pinned."""
import copy
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parents[2] / "scripts"
spec = importlib.util.spec_from_file_location("historical_mu", HERE / "v9-mu-historical-plan.py")
mu = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mu)


def baseline():
    option = [x for x in mu._helper().option_grid(alpha=.5, threshold=.95)
              if x["OptionId"] == mu.HISTORICAL_OPTION][0]
    frozen = mu._historical_catalogue()
    controls = [{"PassId": p, "Fields": frozen[p]} for p in sorted(mu.HISTORICAL_PASSES)]
    catalogue = [{"PassId": p, "Fields": frozen[p]} for p in sorted(frozen)]
    return dict(Purpose="BOOTSTRAP_CANDIDATE_SELECTION",
        SplitSeed="SPLIT_SEED_PREDECLARED", MaximumBatches=32,
        MaximumSearchConfigurations=512, MinimumObservedMPerCell=5,
        MinimumObservedUPerCell=5, PinnedIbgeReferenceVersionId=1,
        PinnedIbgePublishedSha256="a" * 64,
        PinnedIbgeMaterializedFrequencySha256="b" * 64,
        PinnedIbgeBootstrap=dict(Provenance="SINTETICO_IBGE",
            People=30000, GeneratorSeed=42, CorpusFingerprint="c" * 64,
            GeneratorVersion="JORNADA_SYNTH_CORPUS_CSHARP_V1",
            GenerationHypothesesFingerprint="d" * 64),
        Calibration=dict(ModelOptions=[option],
            BaselineRuleSets=[controls],
            DeclaredSearchSpace=dict(Catalogue=catalogue, MinimumPasses=19, MaximumPasses=19),
            MaximumAcceptedPerBaseIdentity=4, MaximumPhysicalRuleSets=2,
            MaximumComparedConfigurations=512, MaximumParallelRuleSets=2,
            Budget=dict(StudyFingerprintSha256="e" * 64,
                MaximumObservedAcceptedFdrPoint=1.0),
            SelectionPolicy=dict(MaximumAcceptedFdrUpper=1.0),
            ObservationCapDeclaration=dict(ManifestSha256="f" * 64,
                ObservationsSha256="1" * 64),
            Execution=dict(EndToEndRepetitions=20)))


class HistoricalMuPlanTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.source = self.root / "nested-source.json"
        self.output = self.root / "mu.json"

    def write(self, data):
        self.source.write_text(json.dumps(data), encoding="utf-8")

    def test_only_two_pinned_v2_v3_methods_and_same_hist_one_mf(self):
        self.write(baseline())
        record = mu.freeze(self.source, self.output)
        out = json.loads(self.output.read_text())
        self.assertEqual(record["initialComparisons"], 4)
        self.assertEqual(record["mode"], "KNOWN_SEED42_TRAIN_TECHNICAL_NON_CONFIRMATORY")
        self.assertEqual(out["MuEstimationMethods"], [
            {"Method": "DEFAULT"}, {"Method": "CLASSWISE_SUPPORT_BACKOFF",
                "Parameters": {"ClassMassCoverage": [.99], "MinimumObservedMPerCell": [5],
                    "MinimumObservedUPerCell": [5], "RetainSupportedTail": [True]}}])
        self.assertEqual(out["Calibration"]["ModelOptions"], baseline()["Calibration"]["ModelOptions"])
        self.assertEqual(out["Calibration"]["ConflictMargins"], [.5])
        self.assertEqual(out["MaximumSearchConfigurations"], 4)
        self.assertEqual(out["Calibration"]["MaximumComparedConfigurations"], 4)
        self.assertEqual(out["MaximumBatches"], 1)
        self.assertEqual(out["Calibration"]["Budget"]["StudyFingerprintSha256"],
                         out["Calibration"]["StudyFingerprintSha256"])
        self.assertEqual(out["Calibration"]["Budget"]["MaximumObservedAcceptedFdrPoint"], .01)
        self.assertEqual(out["Calibration"]["SelectionPolicy"]["MaximumAcceptedFdrUpper"], .01)
        self.assertEqual(out["Calibration"]["BaselineRuleSets"], baseline()["Calibration"]["BaselineRuleSets"])
        self.assertEqual(out["Calibration"]["DeclaredSearchSpace"],
                         baseline()["Calibration"]["DeclaredSearchSpace"])
        self.assertEqual(json.loads(self.source.read_text()), baseline())
        with self.assertRaises(FileExistsError):
            mu.freeze(self.source, self.output)

    def test_malformed_source_and_cross_seed_usage_fail_before_writing(self):
        good = baseline()
        mutants = []
        for edit in (
            lambda x: x["PinnedIbgeBootstrap"].update(GeneratorSeed=43),
            lambda x: x["PinnedIbgeBootstrap"].update(People=45000),
            lambda x: x["Calibration"].update(ObservationCapDeclaration={}),
            lambda x: x["Calibration"]["ModelOptions"][0].update(SmoothingAlpha=.99),
            lambda x: x["Calibration"]["ModelOptions"][0].update(UnitTfOptions=[]),
            lambda x: x["Calibration"].update(BaselineRuleSets=[]),
            lambda x: x["Calibration"]["BaselineRuleSets"][0][0].update(Fields=["tampered"]),
            lambda x: x["Calibration"]["DeclaredSearchSpace"]["Catalogue"][0].update(Fields=["wrong"]),
            lambda x: x["Calibration"].update(DeclaredSearchSpace=None),
            lambda x: x.update(MuEstimationMethods=[{"Method": "UNKNOWN"}]),
            lambda x: x["Calibration"]["Execution"].update(EndToEndRepetitions=0),
        ):
            data = copy.deepcopy(good)
            edit(data)
            mutants.append(data)
        for data in mutants:
            with self.subTest(data=data.get("MuEstimationMethods"), sha=data["PinnedIbgeBootstrap"]["GeneratorSeed"]):
                self.write(data)
                with self.assertRaises((ValueError, KeyError, TypeError)):
                    mu.freeze(self.source, self.output)
                self.assertFalse(self.output.exists())

    def test_duplicate_or_nonfinite_json_rejected(self):
        self.source.write_text('{"Purpose":"BOOTSTRAP_CANDIDATE_SELECTION","Purpose":"MALICIOUS"}')
        with self.assertRaises(ValueError):
            mu.freeze(self.source, self.output)
        self.source.write_text('{"Purpose":NaN}')
        with self.assertRaises(ValueError):
            mu.freeze(self.source, self.output)


if __name__ == "__main__":
    unittest.main()
