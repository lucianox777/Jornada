"""Fail-closed offline tests for the direct V3-only calibration-plan builder."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / 'scripts' / 'v9-v3-direct-plan.py'
spec = importlib.util.spec_from_file_location('v9_v3_direct_plan', SCRIPT)
subject = importlib.util.module_from_spec(spec)
spec.loader.exec_module(subject)


def base():
    return {
        'Purpose': 'BOOTSTRAP_CANDIDATE_SELECTION',
        'MaximumSearchConfigurations': 48,
        'Calibration': {
            'StudyFingerprintSha256': 'b'*64,
            'ModelOptions': [
                {'OptionId':'CONTROL_TF0', 'TfWeight':0,'SmoothingAlpha':.5,'DecisionThreshold':.95},
                {'OptionId':'TF_MOTHER','TfWeight':0,'SmoothingAlpha':.5,'DecisionThreshold':.95,
                 'UnitTfOptions':[{'Side':1,'Unit':2,'Weight':1}]}
            ],
            'ConflictMargins':[.5],
            'DeclaredSearchSpace':{'Catalogue':[{'PassId':'ONE','Fields':['x']}]},
            'BaselineRuleSets':[[{'PassId':'TWO','Fields':['y']}]],
            'Budget':{'StudyFingerprintSha256':'b'*64},
            'SelectionPolicy':{'MaximumAcceptedFdrUpper': .01},
            'ObservationCapDeclaration':{'ManifestSha256':'c'*64,'ObservationsSha256':'d'*64},
            'MaximumComparedConfigurations': 48
        }
    }


def grid():
    return {'ClassMassCoverage':[.95,.99], 'MinimumObservedMPerCell':[1,5],
            'MinimumObservedUPerCell':[5], 'RetainSupportedTail':[True,False]}


class DirectV3Tests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        p=Path(tmp.name)
        self.source, self.grid, self.output = [p/n for n in ('src.json','grid.json','out.json')]
        self.source.write_text(json.dumps(base()))
        self.grid.write_text(json.dumps(grid()))

    def test_v3_only_parameterized_no_v2_and_explicit_tf_abstention(self):
        info=subject.prepare(self.source,self.grid,self.output)
        out=json.loads(self.output.read_text())
        self.assertEqual(info['initialComparisons'], 32)
        self.assertEqual(info['variants'], 8)
        self.assertEqual(len(out['MuEstimationMethods']),1)
        self.assertEqual(out['MuEstimationMethods'][0]['Method'],subject.METHOD)
        self.assertNotIn('DEFAULT',json.dumps(out['MuEstimationMethods']))
        self.assertEqual(out['Calibration']['ModelOptions'][1]['UnpublishedTermPolicy'],1)
        self.assertNotIn('UnpublishedTermPolicy',out['Calibration']['ModelOptions'][0])
        self.assertEqual(json.loads(self.source.read_text()),base())
        self.assertNotEqual(out['Calibration']['StudyFingerprintSha256'],'b'*64)
        self.assertEqual(out['Calibration']['StudyFingerprintSha256'],out['Calibration']['Budget']['StudyFingerprintSha256'])
        with self.assertRaises(FileExistsError):
            subject.prepare(self.source,self.grid,self.output)

    def test_explicit_budget_increase_and_insufficient_fail_closed(self):
        b=base();b['MaximumSearchConfigurations']=8
        self.source.write_text(json.dumps(b))
        with self.assertRaisesRegex(ValueError,'budget'):
            subject.prepare(self.source,self.grid,self.output)
        self.assertFalse(self.output.exists())
        info=subject.prepare(self.source,self.grid,self.output,32)
        self.assertEqual(info['maximumConfigurations'],32)

    def test_invalid_grid_and_prior_unchanged(self):
        b=base(); b['Calibration']['ModelOptions'][0]['SmoothingAlpha']=1
        b['Calibration']['ModelOptions'][0]['PriorMatchProbability']=.1
        self.source.write_text(json.dumps(b))
        info=subject.prepare(self.source,self.grid,self.output)
        out=json.loads(self.output.read_text())
        self.assertEqual(out['Calibration']['ModelOptions'][0]['PriorMatchProbability'],.1)
        self.assertEqual(info['variants'],8)

    def test_reject_duplicate_nonfinite_and_malformed_without_output(self):
        for bad in ('{"ClassMassCoverage":[0.99],"ClassMassCoverage":[0.95]}',
                    '{"ClassMassCoverage":[1e999]}',
                    '{"ClassMassCoverage":[1e-999]}',
                    '{"ClassMassCoverage":NaN}'):
            self.grid.write_text(bad)
            with self.assertRaises(ValueError):
                subject.prepare(self.source,self.grid,self.output)
            self.assertFalse(self.output.exists())

    def test_reject_tf_rejection_and_competing_mu(self):
        b=base()
        b['Calibration']['ModelOptions'][1]['UnpublishedTermPolicy']=0
        self.source.write_text(json.dumps(b))
        with self.assertRaises(ValueError):subject.prepare(self.source,self.grid,self.output)
        b['Calibration']['ModelOptions'][1]['UnpublishedTermPolicy']=1
        b['MuEstimationMethods']=[{'Method':'DEFAULT'}]
        self.source.write_text(json.dumps(b))
        with self.assertRaises(ValueError):subject.prepare(self.source,self.grid,self.output)
        self.assertFalse(self.output.exists())


if __name__=='__main__':unittest.main()
