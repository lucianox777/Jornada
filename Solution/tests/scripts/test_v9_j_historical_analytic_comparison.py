"""Offline contract for the V9 J historical TRAIN-only analytic reporter; no SQL."""
from __future__ import annotations

import copy
import importlib.util
import tempfile
import unittest
from pathlib import Path

SOURCE = Path(__file__).resolve().parents[2] / 'scripts' / 'v9-j-historical-analytic-comparison.py'
spec = importlib.util.spec_from_file_location('j_historical_analytics', SOURCE)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
BLOB = '29037ab5ae0c3e53924a83c2d84a367ec60fca99'


def fixture():
    plan = {'Calibration': {'ObservationCapDeclaration': {
        'ObservationsSha256': '1'*64, 'ManifestSha256': '2'*64,
        'GeneratorVersion': 'JORNADA_SYNTH_CORPUS_CSHARP_V1',
        'MaximumObservationsPerBaseIdentity': 4}},
        'SplitSeed': 'FROZEN', 'AdjustmentFraction': .7,
        'PinnedIbgeReferenceVersionId': 1, 'PinnedIbgePublishedSha256': '3'*64,
        'PinnedIbgeMaterializedFrequencySha256': '4'*64}
    rows = []
    for i, option in enumerate(m.TF_BASE_OPTIONS):
        rows.append({'OptionId': option + '__MU_DEFAULT',
                     'MuEstimationMethod': 'DEFAULT',
                     'blockingPasses': [{'PassId': 'P1'}, {'PassId': 'P2'}, {'PassId': 'P3'}],
                     'CandidatePairsSha256': '5'*64, 'ConflictMargin': .5,
                     'fittedPairPrior': .1, 'threshold': .95, 'eligibleSources': 200,
                     'fitSnapshotSha256': str(i + 6)*64, 'trueAccepted': 180,
                     'falseAccepted': 1, 'conflicts': 5, 'unresolved': 15,
                     'EligibleForSelection': True})
    report = {'schema': 'V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1',
              'rows': rows, 'PlanSha256': 'e'*64, 'testUsed': False,
              'activated': False, 'finalistSnapshotShas': ['6'*64]}
    return plan, report


class CompareContract(unittest.TestCase):
    def compare(self, right_plan=None, right_report=None, blobs=(BLOB, BLOB)):
        plan, report = fixture()
        return m.report_pair(plan, report,
                             right_plan if right_plan is not None else copy.deepcopy(plan),
                             right_report if right_report is not None else copy.deepcopy(report),
                             'master', 'PR', *blobs)

    def test_same_hash_is_identical(self):
        x = self.compare()
        self.assertEqual(x['comparisonStatus'], 'IDÊNTICO')
        self.assertEqual(len(x['arms']), 2)
        self.assertTrue(x['splitAlgorithmGitBlobMatch'])
        self.assertFalse(x['operationalSelectionChangedByThisComparison'])

    def test_hash_changed_outputs_deltas_without_reselecting(self):
        _, r = fixture()
        r['rows'][0]['fitSnapshotSha256'] = 'f'*64
        r['rows'][0]['trueAccepted'] = 185
        r['rows'][0]['conflicts'] = 3
        r['rows'][0]['EligibleForSelection'] = False
        x = self.compare(right_report=r)
        self.assertEqual(x['comparisonStatus'], 'DIFERENTE')
        self.assertEqual(x['arms'][0]['deltas']['deltaTrueAccepted'], 5)
        self.assertEqual(x['arms'][0]['deltas']['deltaConflicts'], -2)
        self.assertEqual(x['arms'][0]['currentEligibleForSelection'], False)

    def test_other_corpus_does_not_receive_fake_delta(self):
        p, r = fixture()
        p['Calibration']['ObservationCapDeclaration']['ObservationsSha256'] = 'a'*64
        x = self.compare(right_plan=p, right_report=r)
        self.assertEqual(x['comparisonStatus'], 'NÃO COMPARÁVEL')
        self.assertTrue(all(c['deltas'] is None for c in x['arms']))

    def test_same_cohort_different_sql_frame_not_comparable(self):
        _, r = fixture()
        r['rows'][0]['CandidatePairsSha256'] = 'b'*64
        x = self.compare(right_report=r)
        self.assertEqual(x['comparisonStatus'], 'NÃO COMPARÁVEL')
        self.assertIsNone(x['arms'][0]['deltas'])

    def test_unknown_split_implementation_never_implies_comparability(self):
        x = self.compare(blobs=(BLOB, 'a'*40))
        self.assertEqual(x['comparisonStatus'], 'NÃO COMPARÁVEL')
        self.assertIn('V9InternalTrainingSplitGitBlobSha', x['differentLineageFields'])

    def test_missing_witness_frame_does_not_raise_or_infer(self):
        _, r = fixture()
        r['rows'].pop()
        x = self.compare(right_report=r)
        self.assertEqual(x['comparisonStatus'], 'NÃO COMPARÁVEL')
        self.assertIsNone(x['arms'][1]['deltas'])

    def test_plan_report_conflicting_provenance_rejected(self):
        p, r = fixture()
        r['sourceObservationsSha256'] = 'f'*64
        with self.assertRaisesRegex(ValueError, 'PROVENANCE_CONFLICT'):
            m.report_pair(p, r, p, r, 'master', 'PR', BLOB, BLOB)

    def test_duplicate_json_keys_refused(self):
        with tempfile.TemporaryDirectory() as folder:
            f = Path(folder) / 'invalid.json'
            f.write_text('{"same": 1, "same": 2}', encoding='utf8')
            with self.assertRaisesRegex(ValueError, 'DUPLICATE'):
                m.read_json(f)


if __name__ == '__main__':
    unittest.main()
