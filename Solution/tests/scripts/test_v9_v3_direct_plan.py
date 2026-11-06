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
            'Budget':{'StudyFingerprintSha256':'b'*64, 'MaximumObservedAcceptedFdrPoint':.01},
            'SelectionPolicy':{'MaximumAcceptedFdrUpper': .01},
            'ObservationCapDeclaration':{'ManifestSha256':'c'*64,'ObservationsSha256':'d'*64},
            'MaximumComparedConfigurations': 48,
            'MaximumPhysicalRuleSets': 2,
            'Execution': {'EndToEndRepetitions': 20,
                          'TotalComparedConfigurations': 48}
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

    @staticmethod
    def bilateral():
        return {
            'OptionId': subject.SURNAME_BILATERAL,
            'SmoothingAlpha': .5, 'DecisionThreshold': .95, 'TfWeight': 0,
            'UnitTfOptions': [
                {'Side': 0, 'Unit': 2, 'Weight': 0, 'Mode': 0},
                {'Side': 0, 'Unit': 3, 'Weight': 1, 'Mode': 0},
                {'Side': 1, 'Unit': 2, 'Weight': 0, 'Mode': 0},
                {'Side': 1, 'Unit': 3, 'Weight': 1, 'Mode': 0},
            ]
        }

    @classmethod
    def surname_pair(cls):
        bilateral = cls.bilateral()
        bilateral.update(SharedSurnameMode=0, EligibleForSelection=True)
        shared = json.loads(json.dumps(bilateral))
        shared.update(OptionId=subject.SURNAME_SHARED, SharedSurnameMode=1,
                      EligibleForSelection=False)
        return [bilateral, shared]

    def test_surname_dependence_two_options_are_read_from_json_without_cli_switch(self):
        b = base()
        b['Calibration']['ModelOptions'] = self.surname_pair()
        self.source.write_text(json.dumps(b))
        info = subject.prepare(self.source, self.grid, self.output)
        output = json.loads(self.output.read_text())
        opts = output['Calibration']['ModelOptions']
        self.assertEqual([o['OptionId'] for o in opts],
                         [subject.SURNAME_BILATERAL, subject.SURNAME_SHARED])
        self.assertEqual([o['SharedSurnameMode'] for o in opts], [0, 1])
        self.assertEqual([o['EligibleForSelection'] for o in opts], [True, False])
        self.assertEqual([o['UnpublishedTermPolicy'] for o in opts], [1, 1])
        self.assertEqual(opts[0]['UnitTfOptions'], opts[1]['UnitTfOptions'])
        self.assertEqual(opts[0]['DecisionThreshold'], opts[1]['DecisionThreshold'])
        self.assertEqual(opts[0]['SmoothingAlpha'], opts[1]['SmoothingAlpha'])
        self.assertEqual({k:v for k,v in opts[0].items()
                          if k not in ('OptionId','SharedSurnameMode','EligibleForSelection')},
                         {k:v for k,v in opts[1].items()
                          if k not in ('OptionId','SharedSurnameMode','EligibleForSelection')})
        self.assertEqual(info['initialComparisons'], 32)
        self.assertEqual(info['baseOptions'], 2)
        self.assertIs(info['surnameDependenceExperiment']['sharedEligibleForSelection'], False)
        self.assertEqual(json.loads(self.source.read_text()), b)
        self.assertNotIn('DEFAULT', json.dumps(output['MuEstimationMethods']))

    def test_surname_pair_must_be_explicit_and_never_filtered_silently(self):
        b = base()
        b['Calibration']['ModelOptions'] = self.surname_pair()
        self.source.write_text(json.dumps(b))
        with self.assertRaisesRegex(ValueError, 'BOTH surname'):
            subject.prepare(self.source, self.grid, self.output,
                            only_option_id=subject.SURNAME_BILATERAL)
        self.assertFalse(self.output.exists())
        b['Calibration']['ModelOptions'][1]['UnitTfOptions'][1]['Weight'] = 0.5
        self.source.write_text(json.dumps(b))
        with self.assertRaisesRegex(ValueError, 'differ ONLY'):
            subject.prepare(self.source, self.grid, self.output)
        self.assertFalse(self.output.exists())
        b['Calibration']['ModelOptions'].pop()
        self.source.write_text(json.dumps(b))
        with self.assertRaisesRegex(ValueError, 'BOTH surname'):
            subject.prepare(self.source, self.grid, self.output)
        self.assertFalse(self.output.exists())

    def test_wrong_modes_selection_and_extra_arm_fail_closed(self):
        for mutate in (
            lambda opts: opts[1].update(SharedSurnameMode=0),
            lambda opts: opts[1].update(EligibleForSelection=True),
            lambda opts: opts[0].update(EligibleForSelection=False),
            lambda opts: opts.append({'OptionId':'EXTRA','TfWeight':0}),
        ):
            b = base()
            b['Calibration']['ModelOptions'] = self.surname_pair()
            mutate(b['Calibration']['ModelOptions'])
            self.source.write_text(json.dumps(b))
            with self.assertRaises(ValueError):
                subject.prepare(self.source, self.grid, self.output)
            self.assertFalse(self.output.exists())

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

    def test_restricted_one_batch_v3_only_physical_contract(self):
        info=subject.prepare(self.source,self.grid,self.output,16,
                             only_option_id='TF_MOTHER',
                             only_margin=.5, initial_only=True)
        out=json.loads(self.output.read_text())
        self.assertEqual(info['initialComparisons'],16)
        self.assertTrue(info['initialOnly'])
        self.assertEqual(out['MaximumBatches'],1)
        self.assertEqual(out['MaximumSearchConfigurations'],16)
        self.assertEqual(out['Calibration']['MaximumComparedConfigurations'],16)
        self.assertEqual([o['OptionId'] for o in out['Calibration']['ModelOptions']],['TF_MOTHER'])
        self.assertEqual(out['Calibration']['ModelOptions'][0]['UnpublishedTermPolicy'],1)
        self.assertEqual(out['Calibration']['ConflictMargins'],[.5])
        self.assertEqual(out['Calibration']['Budget']['MaximumObservedAcceptedFdrPoint'],.01)
        self.assertEqual(out['Calibration']['SelectionPolicy']['MaximumAcceptedFdrUpper'],.01)
        self.assertNotIn('TotalComparedConfigurations',out['Calibration']['Execution'])
        self.assertEqual(json.loads(self.source.read_text()),base())

    def test_restricted_inputs_rejected_before_publication(self):
        for kwargs in ({'only_option_id':'NONEXISTENT','initial_only':True},
                       {'only_margin':3,'initial_only':True}):
            with self.assertRaises(ValueError):
                subject.prepare(self.source,self.grid,self.output,32,**kwargs)
            self.assertFalse(self.output.exists())

    def test_fdr_from_json_is_used_without_hidden_cli_clamping(self):
        for upper, observed in ((.01, .01), (.005, .01), (.0025, .005)):
            b = base()
            b['Calibration']['Budget']['MaximumObservedAcceptedFdrPoint'] = observed
            b['Calibration']['SelectionPolicy']['MaximumAcceptedFdrUpper'] = upper
            self.source.write_text(json.dumps(b))
            subject.prepare(self.source, self.grid, self.output,
                            initial_only=True)
            published = json.loads(self.output.read_text())['Calibration']
            self.assertEqual(published['Budget']['MaximumObservedAcceptedFdrPoint'], observed)
            self.assertEqual(published['SelectionPolicy']['MaximumAcceptedFdrUpper'], upper)
            self.output.unlink()

    def test_fdr_invalid_limits_fail_before_output(self):
        for upper, observed in ((.02, .01), (.02, .03), (0, .01), (-.1, .01),
                                (.01, 1.01), (.01, float('nan'))):
            b = base()
            b['Calibration']['Budget']['MaximumObservedAcceptedFdrPoint'] = observed
            b['Calibration']['SelectionPolicy']['MaximumAcceptedFdrUpper'] = upper
            self.source.write_text(json.dumps(b))
            with self.assertRaises(ValueError):
                subject.prepare(self.source, self.grid, self.output)
            self.assertFalse(self.output.exists())

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
