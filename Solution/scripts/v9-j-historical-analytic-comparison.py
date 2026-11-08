#!/usr/bin/env python3
"""Descriptive V2 witness comparison across physical V9 reports; never selects a model.

Requires two real plan.json + mu-statistical-analysis.json files. Unknown or
incomparable frames do not receive deltas. No fitted/SQL/TEST/Gold execution.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

STATUS_IDENTICAL = 'IDÊNTICO'
STATUS_DIFFERENT = 'DIFERENTE'
STATUS_NOT_COMPARABLE = 'NÃO COMPARÁVEL'
TF_BASE_OPTIONS = ('A0.5_T0.95_TF0', 'TF_ONE_MF_W1_M0_A0.5_T0.95')
_SHA = set('0123456789abcdefABCDEF')


def sha_bytes(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def read_json(path: Path) -> tuple[dict, str]:
    raw = path.read_bytes()

    def unique(items):
        obj = {}
        for k, v in items:
            if k in obj:
                raise ValueError('JSON_DUPLICATE_PROPERTY')
            obj[k] = v
        return obj

    data = json.loads(raw, object_pairs_hook=unique,
                      parse_constant=lambda _: (_ for _ in ()).throw(ValueError('NONFINITE_JSON')))
    if not isinstance(data, dict):
        raise ValueError('INVALID_JSON_ROOT')
    return data, sha_bytes(raw)


def is_sha(x: object) -> bool:
    return isinstance(x, str) and len(x) == 64 and all(c in _SHA for c in x)


def provenance(plan: dict, report: dict) -> dict:
    if report.get('schema') != 'V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1' or not isinstance(report.get('rows'), list):
        raise ValueError('WRONG_REPORT_SCHEMA')
    c = plan['Calibration']
    cap = c['ObservationCapDeclaration']
    for k in ('ManifestSha256', 'ObservationsSha256'):
        if not is_sha(cap.get(k)):
            raise ValueError('INVALID_OBSERVATION_PIN_' + k)
    if not is_sha(report.get('PlanSha256')):
        raise ValueError('REPORT_PLAN_SHA_MISSING')
    if report.get('testUsed') is not False or report.get('activated') is not False:
        raise ValueError('ONLY_NON_ACTIVATED_TRAIN_REPORTS')
    for name, expected in (('sourceObservationsSha256', cap['ObservationsSha256']),
                           ('producerManifestSha256', cap['ManifestSha256'])):
        if name in report and report[name].lower() != expected.lower():
            raise ValueError('PLAN_REPORT_PROVENANCE_CONFLICT_' + name)
    if report.get('trainSplitSeed', plan['SplitSeed']) != plan['SplitSeed']:
        raise ValueError('PLAN_REPORT_SPLIT_SEED_CONFLICT')
    return {'ObservationsSha256': cap['ObservationsSha256'].lower(),
            'ManifestSha256': cap['ManifestSha256'].lower(),
            'GeneratorVersion': cap['GeneratorVersion'],
            'MaxObservationsPerBaseIdentity': cap['MaximumObservationsPerBaseIdentity'],
            'SplitSeed': plan['SplitSeed'], 'AdjustmentFraction': plan['AdjustmentFraction'],
            'PinnedIbgeReferenceVersionId': plan.get('PinnedIbgeReferenceVersionId'),
            'PinnedIbgePublishedSha256': plan.get('PinnedIbgePublishedSha256'),
            'PinnedIbgeMaterializedFrequencySha256': plan.get('PinnedIbgeMaterializedFrequencySha256')}


def v2_rows(report: dict, option: str) -> list[dict]:
    return [row for row in report['rows']
            if row.get('OptionId') == option + '__MU_DEFAULT'
            and row.get('MuEstimationMethod') == 'DEFAULT'
            and isinstance(row.get('blockingPasses'), list)
            and len(row['blockingPasses']) == 3]


def report_pair(left_plan: dict, left_report: dict,
                right_plan: dict, right_report: dict,
                baseline_label: str, current_label: str,
                baseline_split_blob: str | None = None,
                current_split_blob: str | None = None) -> dict:
    left = provenance(left_plan, left_report)
    right = provenance(right_plan, right_report)
    distinct_pins = sorted(k for k in left if left[k] != right[k])
    # Frozen V9InternalTrainingSplit.cs implementation must match across
    # revisions, unless both reports carry independently matching assignments.
    if not (isinstance(baseline_split_blob, str) and len(baseline_split_blob) == 40
            and isinstance(current_split_blob, str) and len(current_split_blob) == 40
            and all(c in _SHA for c in baseline_split_blob + current_split_blob)
            and baseline_split_blob.lower() == current_split_blob.lower()):
        distinct_pins.append('V9InternalTrainingSplitGitBlobSha')
    old_assignment = left_report.get('trainSplitAssignmentsSha256')
    new_assignment = right_report.get('trainSplitAssignmentsSha256')
    if old_assignment is not None and new_assignment is not None and old_assignment != new_assignment:
        distinct_pins.append('trainSplitAssignmentsSha256')
    comparisons = []
    for option in TF_BASE_OPTIONS:
        l, r = v2_rows(left_report, option), v2_rows(right_report, option)
        reasons = ['PROVENANCE_MISMATCH:' + k for k in distinct_pins]
        if len(l) != 1 or len(r) != 1:
            reasons.append('V2_THREE_PASS_FRAME_ABSENT_OR_AMBIGUOUS')
        a = l[0] if len(l) == 1 else None
        b = r[0] if len(r) == 1 else None
        if a is not None and b is not None:
            for key in ('CandidatePairsSha256', 'blockingPasses', 'ConflictMargin',
                        'fittedPairPrior', 'threshold', 'eligibleSources'):
                if key == 'CandidatePairsSha256' and (not is_sha(a.get(key)) or not is_sha(b.get(key))):
                    reasons.append('MISSING_CANDIDATE_FRAME_DIGEST')
                elif a.get(key) != b.get(key):
                    reasons.append('FRAME_MISMATCH:' + key)
            if not is_sha(a.get('fitSnapshotSha256')) or not is_sha(b.get('fitSnapshotSha256')):
                reasons.append('MISSING_SNAPSHOT_SHA')
        same = len(reasons) == 0
        status = (STATUS_NOT_COMPARABLE if not same else
                  STATUS_IDENTICAL if a['fitSnapshotSha256'] == b['fitSnapshotSha256'] else STATUS_DIFFERENT)
        numeric = ('trueAccepted', 'falseAccepted', 'conflicts', 'unresolved')
        metrics = ({} if not same else
                   {('delta' + k[:1].upper() + k[1:]): b[k] - a[k]
                    for k in numeric if isinstance(a.get(k), int) and isinstance(b.get(k), int)})
        comparisons.append({'optionId': option, 'historicalMethod': 'DEFAULT',
                            'blockingPassCount': 3, 'status': status,
                            'reasons': reasons,
                            'baselineSnapshotSha256': a.get('fitSnapshotSha256') if a is not None else None,
                            'currentSnapshotSha256': b.get('fitSnapshotSha256') if b is not None else None,
                            'baselineCandidateFrameSha256': a.get('CandidatePairsSha256') if a is not None else None,
                            'currentCandidateFrameSha256': b.get('CandidatePairsSha256') if b is not None else None,
                            'baselineEligibleForSelection': a.get('EligibleForSelection') if a is not None else None,
                            'currentEligibleForSelection': b.get('EligibleForSelection') if b is not None else None,
                            'deltas': metrics if same else None})
    statuses = {x['status'] for x in comparisons}
    overall = (STATUS_NOT_COMPARABLE if STATUS_NOT_COMPARABLE in statuses else
               STATUS_DIFFERENT if STATUS_DIFFERENT in statuses else STATUS_IDENTICAL)
    return {'schema': 'V9_J_HISTORICAL_ANALYTIC_COMPARISON_V1',
            'comparisonStatus': overall, 'arms': comparisons,
            'baseline': {'label': baseline_label, 'planSha256': left_report['PlanSha256'],
                         'recordedFinalistShas': left_report.get('finalistSnapshotShas')},
            'current': {'label': current_label, 'planSha256': right_report['PlanSha256'],
                        'recordedFinalistShas': right_report.get('finalistSnapshotShas')},
            'sharedLineage': {k: left[k] for k in left if k not in distinct_pins},
            'differentLineageFields': distinct_pins,
            'splitAlgorithmGitBlobMatch': 'V9InternalTrainingSplitGitBlobSha' not in distinct_pins,
            'splitAlgorithmGitBlobSha': baseline_split_blob if baseline_split_blob == current_split_blob else None,
            'operationalSelectionChangedByThisComparison': False,
            'certified': False, 'testUsed': False, 'activated': False,
            'scope': 'DESCRIPTIVE_READ_ONLY_NO_CI_GATE_NO_RERESELECT'}


def main():
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument('--baseline-plan', type=Path, required=True)
    cli.add_argument('--baseline-analysis', type=Path, required=True)
    cli.add_argument('--current-plan', type=Path, required=True)
    cli.add_argument('--current-analysis', type=Path, required=True)
    cli.add_argument('--baseline-label', required=True)
    cli.add_argument('--current-label', required=True)
    cli.add_argument('--baseline-split-code-blob', required=True)
    cli.add_argument('--current-split-code-blob', required=True)
    cli.add_argument('--out', type=Path, required=True)
    args = cli.parse_args()
    a, asha = read_json(args.baseline_plan)
    ar, arsha = read_json(args.baseline_analysis)
    b, bsha = read_json(args.current_plan)
    br, brsha = read_json(args.current_analysis)
    result = report_pair(a, ar, b, br, args.baseline_label, args.current_label,
                         args.baseline_split_code_blob, args.current_split_code_blob)
    result['inputFileSha256'] = {'baselinePlan': asha, 'baselineAnalysis': arsha,
                                 'currentPlan': bsha, 'currentAnalysis': brsha}
    if args.out.exists():
        raise FileExistsError('NEVER_OVERWRITE_PREVIOUS_ANALYTIC_RESULT')
    args.out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf8')
    print(json.dumps({'comparisonStatus': result['comparisonStatus'],
                      'arms': len(result['arms']), 'selectedModelChanged': False}, ensure_ascii=False))


if __name__ == '__main__':
    main()
