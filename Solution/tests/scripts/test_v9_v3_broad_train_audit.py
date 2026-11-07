"""Offline invariants for larger V3 physical QA; no SQL or TEST."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / 'scripts' / 'v9-v3-broad-train-audit.py'
spec = importlib.util.spec_from_file_location('v9_v3_broad_train_audit', SCRIPT)
subject = importlib.util.module_from_spec(spec)
spec.loader.exec_module(subject)


def fixture():
    options = ['A0.5_T0.95_TF0', 'TF_ONE_MF_W1_M0_A0.5_T0.95']
    plan = {
        'Purpose':'BOOTSTRAP_CANDIDATE_SELECTION',
        'MaximumBatches':1, 'MaximumSearchConfigurations':20,
        'Calibration':{
            'ConflictMargins':[0, .25, .5, 2, 4],
            'Budget':{'MaximumObservedAcceptedFdrPoint':.01},
            'SelectionPolicy':{'MaximumAcceptedFdrUpper':.01},
            'ModelOptions':[{'OptionId':x, 'EligibleForSelection':True} for x in options]
        },
        'MuEstimationMethods':[{'Method':'CLASSWISE_SUPPORT_BACKOFF', 'Parameters':{
            'ClassMassCoverage':[.99], 'MinimumObservedMPerCell':[5],
            'MinimumObservedUPerCell':[5], 'RetainSupportedTail':[True]}}],
    }
    policy = {'ClassMassCoverage':.99, 'MinimumM':5, 'MinimumU':5,
              'SupportedTailRule':'RETAIN_CLASS_JOINT_WHEN_COUNT_AT_LEAST_K_V2'}
    rows = []
    for passes in (3,19):
        for margin in (0, .25, .5, 2, 4):
            for name in options:
                rows.append({
                    'OptionId':name+'__MU_CLASSWISE_SUPPORT_BACKOFF_C0.99_M5_U5_T1',
                    'Batch':1, 'MuEstimationMethod':'CLASSWISE_SUPPORT_BACKOFF',
                    'MuPolicy':copy.deepcopy(policy), 'ConflictMargin':margin,
                    'fitSnapshotSha256':'a'*64,'CandidatePairsSha256':('a' if passes==3 else 'b')*64,
                    'blockingPasses':[{'PassId':str(x)} for x in range(passes)],
                    'observedSupportAudit':{'Cells':[0]*81},
                    'estimatorJointSupport':{'Cells':[0]*81},
                    'candidatePriorTrainingAudit':{'Version':'V9_TRAIN_CANDIDATE_PRIOR_DENOMINATOR_AUDIT_V1'},
                    'fittedPairPrior':.5,'trueAccepted':10,'falseAccepted':0,
                    'eligibleSources':11, 'recall':10/11, 'fdrObserved':0,
                    'conditionalClusterCpFdrUpper':.005,
                    'clusterIidIndependentlyValidated':False,
                    'p95Milliseconds':50,'Rejections':[]
                })
    report = {'schema':'V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1',
              'scope':'TRAIN_SELECTION_ONLY_NOT_INDEPENDENT_VALIDATION',
              'testUsed':False,'activated':False,'evaluations':20,'measured':20,
              'rows':rows}
    return plan, report


class BroadV3AuditTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        d = Path(temp.name)
        self.plan, self.report = d/'plan.json', d/'report.json'
        self.payload = fixture()
        self.save()

    def save(self):
        p, r = self.payload
        self.plan.write_text(json.dumps(p))
        self.report.write_text(json.dumps(r))

    def check(self):
        return subject.verify(self.plan, self.report, 'first_name', 43)

    def test_twenty_valid_physical_rows_per_seed_and_two_tf_arms(self):
        audit = self.check()
        self.assertEqual(audit['physicalConfigurations'], 20)
        self.assertEqual(len(audit['pairs']), 10)
        self.assertEqual(audit['declaredFdrUpperAndPointLimit'], .01)
        self.assertFalse(audit['activated'])
        self.assertFalse(audit['testUsed'])
        self.assertEqual(audit['confidenceForTrueFdr'],
                         'NOT_CERTIFIED_INDEPENDENCE_AND_MULTIPLICITY_OPEN')

    def test_seed42_requires_all_160_configurations_across_five_margins(self):
        # The first physical surname/seed42 trial hit OOM AFTER reporting
        # all 160 configurations. A successful 64-case subset is not a
        # comparable RAM regression and must fail closed at the audit gate.
        plan, report = fixture()
        policies = []
        for coverage in (.95, .99):
            for minimum_m in (1, 5):
                for minimum_u in (1, 5):
                    policies.append((coverage, minimum_m, minimum_u))
        plan['MaximumSearchConfigurations'] = 160
        plan['MuEstimationMethods'][0]['Parameters'].update(
            ClassMassCoverage=[.95, .99],
            MinimumObservedMPerCell=[1, 5],
            MinimumObservedUPerCell=[1, 5],
        )
        originals = report['rows']
        report['rows'] = []
        for coverage, minimum_m, minimum_u in policies:
            for original in originals:
                row = copy.deepcopy(original)
                row['MuPolicy'].update(
                    ClassMassCoverage=coverage,
                    MinimumM=minimum_m,
                    MinimumU=minimum_u,
                )
                root = row['OptionId'].split('__MU_')[0]
                suffix = (f'__MU_CLASSWISE_SUPPORT_BACKOFF_C{coverage}'
                          f'_M{minimum_m}_U{minimum_u}_T1')
                row['OptionId'] = root + suffix
                # Published seed42 sentinel in the independent V3 audit.
                row.update(trueAccepted=6771, falseAccepted=0,
                           eligibleSources=7011, recall=6771/7011)
                report['rows'].append(row)
        report['evaluations'] = report['measured'] = 160
        self.save()
        exact = subject.verify(self.plan, self.report, 'first_name', 42)
        self.assertEqual(exact['physicalConfigurations'], 160)
        self.assertEqual(exact['variants'], 8)
        self.assertEqual(len(exact['pairs']), 80)
        self.assertEqual({x['margin'] for x in exact['rows']},
                         {0, .25, .5, 2, 4})

        # Explicitly reject the 64-measurement protocol that passed in
        # SHA 68bff2b: even if all 64 have 81-cell M/U support.
        report['rows'] = report['rows'][:64]
        report['evaluations'] = report['measured'] = 64
        self.save()
        with self.assertRaises(ValueError):
            subject.verify(self.plan, self.report, 'first_name', 42)

    def test_observed_zero_fdr_never_implies_certification(self):
        for row in self.payload[1]['rows']:
            self.assertEqual(row['falseAccepted'], 0)
            self.assertFalse(row['clusterIidIndependentlyValidated'])
        self.assertEqual(self.check()['falseAcceptancesSeen'], 0)
        self.assertTrue(all(p['statisticallyCertified'] is False for p in self.check()['pairs']))

    def test_bad_scope_missing_rows_inconsistent_fdr_and_mismatched_frame_fail(self):
        changes = (
            lambda p,r: r['rows'].pop(),
            lambda p,r: r['rows'][0].update(fdrObserved=.4),
            lambda p,r: r['rows'][0].update(candidatePriorTrainingAudit=None),
            lambda p,r: r['rows'][0].update(fittedPairPrior=None),
            lambda p,r: r['rows'][0].update(CandidatePairsSha256='c'*64),
            lambda p,r: r['rows'][0].update(clusterIidIndependentlyValidated=True),
            lambda p,r: p['Calibration']['SelectionPolicy'].update(MaximumAcceptedFdrUpper=.2),
            lambda p,r: p['Calibration'].update(ConflictMargins=[.5,2]),
        )
        for bad in changes:
            self.payload = fixture()
            bad(*self.payload)
            self.save()
            with self.assertRaises((ValueError, KeyError, StopIteration)):
                self.check()


if __name__ == '__main__':
    unittest.main()
