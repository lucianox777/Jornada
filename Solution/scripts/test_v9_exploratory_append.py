#!/usr/bin/env python3
"""Unit tests for append-only exploratory plan materialization (no SQL/.NET)."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).with_name('v9-exploratory-append.py')
spec = importlib.util.spec_from_file_location('v9_exploratory_append', SCRIPT)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

def baseline():
    return {'Calibration':{'ModelOptions':[{'OptionId':'CURRENT','EligibleForSelection':True,
      'SmoothingAlpha':0.5,'DecisionThreshold':0.95,'TfWeight':0}],
      'Budget':{'StudyFingerprintSha256':'0'*64},
      'StudyFingerprintSha256':'0'*64}}

def candidate(**overrides):
    opt={'OptionId':'EXP_001','EligibleForSelection':False,'SmoothingAlpha':0.5,
         'DecisionThreshold':0.96,'TfWeight':0}
    ev={'Status':'VALIDATED_TRAIN','Recall':0.98,'BaselineRecall':0.97,
        'FdrUpper':0.002,'BaselineFdrUpper':0.003,
        'StudyFingerprintSha256':'a'*64}
    for k,v in overrides.items():
        (ev if k in ev else opt)[k]=v
    return {'Candidates':[{'ModelOption':opt,'Evidence':ev}]}

class AppendTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)
        self.source=self.root/'source.json'
        self.candidates=self.root/'candidates.json'
        self.output=self.root/'derived.json'
        self.source.write_text(json.dumps(baseline()))
    def run_append(self, data):
        self.candidates.write_text(json.dumps(data))
        return mod.append(self.source,self.candidates,self.output)
    def test_appends_without_modifying_source(self):
        before=self.source.read_bytes()
        outcome=self.run_append(candidate())
        result=json.loads(self.output.read_text())
        self.assertEqual(self.source.read_bytes(),before)
        self.assertEqual(outcome['added'],1)
        self.assertEqual(result['Calibration']['ModelOptions'][0],baseline()['Calibration']['ModelOptions'][0])
        self.assertFalse(result['Calibration']['ModelOptions'][1]['EligibleForSelection'])
        self.assertNotEqual(result['Calibration']['StudyFingerprintSha256'],'0'*64)
    def test_rejects_worse_candidate(self):
        with self.assertRaisesRegex(ValueError,'does not dominate'):
            self.run_append(candidate(Recall=0.96))
        self.assertFalse(self.output.exists())
    def test_rejects_eligible_candidate(self):
        with self.assertRaisesRegex(ValueError,'ineligible'):
            self.run_append(candidate(EligibleForSelection=True))
    def test_rejects_duplicate_identifier(self):
        with self.assertRaisesRegex(ValueError,'unique'):
            self.run_append(candidate(OptionId='CURRENT'))
    def test_rejects_duplicate_parameters(self):
        with self.assertRaisesRegex(ValueError,'Duplicate option parameters'):
            self.run_append({'Candidates':[{'ModelOption':dict(baseline()['Calibration']['ModelOptions'][0], OptionId='EXP_DUP', EligibleForSelection=False), 'Evidence':candidate()['Candidates'][0]['Evidence']}]})
    def test_rejects_active_tf_without_abstention(self):
        with self.assertRaisesRegex(ValueError,'abstain'):
            self.run_append(candidate(TfWeight=1))
    def test_rejects_unvalidated_evidence(self):
        with self.assertRaisesRegex(ValueError,'TRAIN validation'):
            self.run_append(candidate(Status='UNVALIDATED'))
    def test_rejects_overwrite(self):
        self.candidates.write_text(json.dumps(candidate()))
        with self.assertRaisesRegex(ValueError,'distinct'):
            mod.append(self.source,self.candidates,self.source)
    def test_rejects_existing_output(self):
        self.output.write_text('keep')
        with self.assertRaises(FileExistsError):
            self.run_append(candidate())
        self.assertEqual(self.output.read_text(),'keep')
    def test_rejects_duplicate_json_keys(self):
        self.candidates.write_text('{"Candidates":[],"Candidates":[]}')
        with self.assertRaisesRegex(ValueError,'Duplicate JSON key'):
            mod.append(self.source,self.candidates,self.output)

if __name__=='__main__':
    unittest.main()
