#!/usr/bin/env python3
"""Independent, fail-closed aggregate audit for the V3 physical breadth study.

Only TRAIN_SELECTION metrics. Does not re-label train as validation, infer IID,
certify FDR, approve a model, or use TEST/Gold/DT-15.
"""
import argparse
import hashlib
import json
from collections import defaultdict
from pathlib import Path


def sha(data):
    return hashlib.sha256(data).hexdigest()


def verify(plan_path, report_path, family, seed):
    plan_bytes, report_bytes = Path(plan_path).read_bytes(), Path(report_path).read_bytes()
    plan, report = json.loads(plan_bytes), json.loads(report_bytes)
    calibration = plan['Calibration']
    budget = calibration['Budget']
    policy = calibration['SelectionPolicy']
    if (budget['MaximumObservedAcceptedFdrPoint'] != 1
            or policy['MaximumAcceptedFdrUpper'] != .01):
        raise ValueError('FDR=1% must come from plan.json, both point and upper limits')
    if (plan['Purpose'] != 'BOOTSTRAP_CANDIDATE_SELECTION'
            or plan['MaximumBatches'] != 1
            or report['scope'] != 'TRAIN_SELECTION_ONLY_NOT_INDEPENDENT_VALIDATION'
            or report['schema'] != 'V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1'
            or report.get('testUsed') is not False
            or report.get('activated') is not False):
        raise ValueError('Not a controlled TRAIN-only V3 study')
    methods = plan['MuEstimationMethods']
    if len(methods) != 1 or methods[0]['Method'] != 'CLASSWISE_SUPPORT_BACKOFF':
        raise ValueError('V3-only estimator violated')
    grid = methods[0]['Parameters']
    nvariants = 1
    for key in ('ClassMassCoverage', 'MinimumObservedMPerCell',
                'MinimumObservedUPerCell', 'RetainSupportedTail'):
        nvariants *= len(grid[key])
    expected_variants = 8 if seed == 42 else 1
    expected = expected_variants * 2 * 2 * 2
    options = calibration['ModelOptions']
    names = [o['OptionId'] for o in options]
    if nvariants != expected_variants or len(names) != 2 or len(set(names)) != 2:
        raise ValueError('Incorrect options/grid')
    if family == 'surname':
        if names != ['TF_TWO_PL_ML_W1_M0_A0.5_T0.95',
                      'TF_SHARED_PL_ML_W1_M0_A0.5_T0.95']:
            raise ValueError('Wrong surname hypotheses')
        if [(o['SharedSurnameMode'], o['EligibleForSelection']) for o in options] != [(0, True), (1, False)]:
            raise ValueError('Incorrect surname eligibility')
    elif family == 'first_name':
        if set(names) != {'A0.5_T0.95_TF0', 'TF_ONE_MF_W1_M0_A0.5_T0.95'}:
            raise ValueError('Missing first-name control/treatment')
    else:
        raise ValueError('Unknown family')
    if expected != plan['MaximumSearchConfigurations'] or report['evaluations'] != expected:
        raise ValueError('Missing measured comparison requests')
    rows = report['rows']
    if len(rows) != expected or report['measured'] != expected:
        raise ValueError('Unmeasured V3 cells: fail closed, do not summarize')
    groups = defaultdict(list)
    observed = []
    for row in rows:
        if not row['OptionId'] or not any(row['OptionId'].startswith(x + '__MU_') for x in names):
            raise ValueError('Unknown or non-V3 fit')
        if (row['MuEstimationMethod'] != 'CLASSWISE_SUPPORT_BACKOFF'
                or row['fitSnapshotSha256'] is None
                or not row['CandidatePairsSha256']
                or len(row['observedSupportAudit']['Cells']) != 81
                or len(row['estimatorJointSupport']['Cells']) != 81
                or row['clusterIidIndependentlyValidated'] is not False):
            raise ValueError('Missing physical fit, joint support or IID fail-close')
        if (row.get('candidatePriorTrainingAudit') is None
                or row.get('fittedPairPrior') is None):
            raise ValueError('Missing audited TRAIN pair prior; integrated V3 required')
        tp, fp, eligible = row['trueAccepted'], row['falseAccepted'], row['eligibleSources']
        if not (isinstance(tp, int) and isinstance(fp, int) and isinstance(eligible, int)
                and 0 <= tp <= eligible and fp >= 0 and eligible > 0):
            raise ValueError('Invalid TP/FP/eligible counts')
        fdr = fp / (fp + tp) if fp + tp else None
        if row['fdrObserved'] is None or fdr is None or abs(fdr - row['fdrObserved']) > 1e-11:
            raise ValueError('FDR formula mismatch')
        if abs(row['recall'] - tp / eligible) > 1e-11:
            raise ValueError('Recall formula mismatch')
        cp = row['conditionalClusterCpFdrUpper']
        if cp is not None and (cp < fdr - 1e-12 or not 0 <= cp <= 1):
            raise ValueError('Invalid conditional upper bound')
        key = (row['Batch'], row['ConflictMargin'], len(row['blockingPasses']),
               json.dumps(row['MuPolicy'], sort_keys=True))
        groups[key].append(row)
        observed.append({'option':row['OptionId'].split('__MU_')[0], 'passes':key[2],
                         'muPolicy':row['MuPolicy'], 'margin':row['ConflictMargin'],
                         'tp':tp, 'fp':fp, 'eligible':eligible, 'recall':row['recall'],
                         'fdr':fdr, 'cpConditionalNotCertified':cp,
                         'p95Milliseconds':row['p95Milliseconds'],
                         'rejections':row['Rejections'],
                         'candidatePairsSha256':row['CandidatePairsSha256'],
                         'prior':row['fittedPairPrior'],
                         'reportedAffectedSharedSurnamePairs':row.get('SharedSurnameCandidatePairs')})
    if len(groups) != expected // 2:
        raise ValueError('Missing M/U/margin/ruleset strata')
    comparisons = []
    for key, candidates in groups.items():
        if len(candidates) != 2:
            raise ValueError('Stratum not paired')
        by = {r['OptionId'].split('__MU_')[0]: r for r in candidates}
        if len(by) != 2 or set(by) != set(names):
            raise ValueError('Missing model option in stratum')
        a, b = by[names[0]], by[names[1]]
        if (a['CandidatePairsSha256'] != b['CandidatePairsSha256']
                or a['eligibleSources'] != b['eligibleSources']
                or a['fittedPairPrior'] != b['fittedPairPrior']):
            raise ValueError('Paired options have different physical frame or prior')
        comparisons.append({
            'blockingPasses':key[2], 'margin':key[1], 'muPolicy':a['MuPolicy'],
            'candidatePairsSha256':a['CandidatePairsSha256'],
            'leftOption':names[0], 'rightOption':names[1],
            'deltaTpRightMinusLeft':b['trueAccepted'] - a['trueAccepted'],
            'deltaFpRightMinusLeft':b['falseAccepted'] - a['falseAccepted'],
            'deltaRecallRightMinusLeft':b['recall'] - a['recall'],
            'fdrWithinPlanOnePercent':a['fdrObserved'] <= .01 and b['fdrObserved'] <= .01,
            'statisticallyCertified':False,
        })
    # The historical seed42 score outputs are REGRESSION sentinels only.
    # No comparison of different TF policies can be called an M/U improvement.
    if seed == 42:
        standard = next(k for k in rows if len(k['blockingPasses']) == 3
            and k['ConflictMargin'] == .5 and k['MuPolicy']['ClassMassCoverage'] == .99
            and k['MuPolicy']['MinimumM'] == 5 and k['MuPolicy']['MinimumU'] == 5
            and k['MuPolicy']['SupportedTailRule'] == 'RETAIN_CLASS_JOINT_WHEN_COUNT_AT_LEAST_K_V2'
            and k['OptionId'].startswith(('TF_ONE_MF_W1_M0_A0.5_T0.95' if family == 'first_name'
                                         else 'TF_TWO_PL_ML_W1_M0_A0.5_T0.95') + '__MU_'))
        baseline_tp = 6771 if family == 'first_name' else 6307
        if standard['trueAccepted'] != baseline_tp or standard['falseAccepted'] != 0 or standard['eligibleSources'] != 7011:
            raise ValueError('Known seed42 V3 physical regression changed without explanation')
    return {
        'schema': 'V9_V3_BROAD_TRAIN_REGRESSION_AUDIT_V1',
        'scope': 'TECHNICAL_TRAIN_SELECTION_NOT_INDEPENDENT_VALIDATION',
        'family':family, 'seed':seed,
        'planBytesSha256':sha(plan_bytes), 'reportBytesSha256':sha(report_bytes),
        'declaredFdrUpperAndPointLimit':.01,
        'physicalConfigurations':expected,
        'variants':nvariants, 'pairs':comparisons, 'rows':observed,
        'falseAcceptancesSeen':sum(x['fp'] for x in observed),
        'confidenceForTrueFdr':'NOT_CERTIFIED_INDEPENDENCE_AND_MULTIPLICITY_OPEN',
        'goldUsed':False, 'testUsed':False, 'activated':False,
    }


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--plan', type=Path, required=True)
    p.add_argument('--report', type=Path, required=True)
    p.add_argument('--family', choices=['surname', 'first_name'], required=True)
    p.add_argument('--seed', type=int, choices=[42, 43, 57], required=True)
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    result = verify(a.plan, a.report, a.family, a.seed)
    with a.output.open('x', encoding='utf-8') as out:
        json.dump(result, out, indent=2, allow_nan=False, ensure_ascii=False)
        out.write('\n')
    print(json.dumps({k:result[k] for k in ('family','seed','physicalConfigurations',
                                            'variants','falseAcceptancesSeen',
                                            'confidenceForTrueFdr')}, sort_keys=True))


if __name__ == '__main__':
    main()
