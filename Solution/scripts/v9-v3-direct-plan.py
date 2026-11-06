#!/usr/bin/env python3
"""Freeze a V9 V3-only parameterized TRAIN plan using the existing C# calibrator.

No V2 baseline or historical M/U values are read. No SQL, REF, Gold or TEST.
The caller explicitly supplies a grid; this script never chooses a winner.
"""
import argparse
import copy
from decimal import Decimal
import hashlib
import json
import math
from pathlib import Path

VERSION = 'V9_V3_DIRECT_PARAMETERIZED_TRAIN_PLAN_V1'
METHOD = 'CLASSWISE_SUPPORT_BACKOFF'
FIELDS = ('ClassMassCoverage', 'MinimumObservedMPerCell',
          'MinimumObservedUPerCell', 'RetainSupportedTail')
# Existing V9 TF grid ID: one matched mother/person surname pair, all else equal.
SURNAME_BILATERAL = 'TF_TWO_PL_ML_W1_M0_A0.5_T0.95'
SURNAME_SHARED = 'TF_SHARED_PL_ML_W1_M0_A0.5_T0.95'



def _load(path):
    def unique(pairs):
        out = {}
        for key, value in pairs:
            if key in out:
                raise ValueError('Duplicate JSON key: ' + key)
            out[key] = value
        return out

    def finite_float(raw):
        value = float(raw)
        if not math.isfinite(value) or (value == 0 and Decimal(raw) != 0):
            raise ValueError('Non-finite or underflowed JSON float')
        return value

    def bad_constant(raw):
        raise ValueError('Forbidden JSON constant: ' + raw)

    return json.loads(Path(path).read_bytes(), object_pairs_hook=unique,
                      parse_float=finite_float, parse_constant=bad_constant)


def _grid(raw):
    if not isinstance(raw, dict) or set(raw) != set(FIELDS):
        raise ValueError('V3 grid requires exactly four explicit parameter arrays')
    for field in FIELDS:
        values = raw[field]
        if not isinstance(values, list) or not values:
            raise ValueError('Array required: ' + field)
        invalid_type = (any(type(v) is not bool for v in values)
                        if field == 'RetainSupportedTail'
                        else any(type(v) not in (float, int) for v in values))
        if invalid_type:
            raise ValueError('Invalid value type: ' + field)
        if len(set(values)) != len(values):
            raise ValueError('Duplicate grid value: ' + field)
    if any(v <= 0 or v >= 1 for v in raw['ClassMassCoverage']):
        raise ValueError('ClassMassCoverage must be strictly between 0 and 1')
    for field in ('MinimumObservedMPerCell', 'MinimumObservedUPerCell'):
        if any(type(v) is not int or v < 1 for v in raw[field]):
            raise ValueError('Observed support minimum must be a positive integer: ' + field)
    return raw


def _validate_explicit_surname_modes(options):
    """Do not invent a comparison from a CLI flag.

    For a compact TF comparison, the *input plan.json* must declare BOTH
    complete ModelOptions, including SharedSurnameMode and eligibility. The
    real C# calibrator consumes those two options directly. The historical
    45-arm catalog is left untouched.
    """
    ids = [o['OptionId'] for o in options]
    # The ordinary producer may include a bilateral option in a broader
    # 41/45-arm catalogue. It is not a paired experiment by itself.
    if SURNAME_SHARED not in ids:
        if len(options) <= 2 and SURNAME_BILATERAL in ids:
            raise ValueError('The JSON must declare BOTH surname TF ModelOptions explicitly')
        return False
    if len(options) != 2 or ids != [SURNAME_BILATERAL, SURNAME_SHARED]:
        raise ValueError('The JSON must declare BOTH surname TF ModelOptions explicitly')
    bilateral, shared = options
    if (bilateral.get('SharedSurnameMode') != 0
            or shared.get('SharedSurnameMode') != 1
            or bilateral.get('EligibleForSelection') is not True
            or shared.get('EligibleForSelection') is not False):
        raise ValueError('JSON modes require bilateral 0/selectable and shared 1/diagnostic')
    exceptions = ('OptionId', 'SharedSurnameMode', 'EligibleForSelection')
    if ({k: v for k, v in bilateral.items() if k not in exceptions}
            != {k: v for k, v in shared.items() if k not in exceptions}):
        raise ValueError('Surname JSON options must differ ONLY in mode, ID and eligibility')
    slots = bilateral.get('UnitTfOptions')
    if (not isinstance(slots, list) or len(slots) != 4
            or any(not isinstance(x, dict) for x in slots)
            or {(x.get('Side'), x.get('Unit')) for x in slots}
            != {(0, 2), (0, 3), (1, 2), (1, 3)}
            or any(type(x.get('Weight')) not in (int, float)
                   or type(x.get('Mode')) is not int for x in slots)):
        raise ValueError('Four explicit TF slots are required for the surname JSON comparison')
    weights = {(x['Side'], x['Unit']): x['Weight'] for x in slots}
    if (bilateral.get('TfWeight', 0) != 0
            or weights != {(0, 2): 0, (0, 3): 1, (1, 2): 0, (1, 3): 1}
            or any(x['Mode'] != 0 for x in slots if x['Weight'] > 0)):
        raise ValueError('Expected last-surname TF W1 symmetric, prenames zero in both JSON arms')
    return True


def prepare(source, grid_path, output, maximum_configurations=None,
            only_option_id=None, only_margin=None, initial_only=False):
    source, grid_path, output = map(Path, (source, grid_path, output))
    if output.exists():
        raise FileExistsError(output)
    original_bytes = source.read_bytes()
    grid_bytes = grid_path.read_bytes()
    plan = _load(source)
    grid = _grid(_load(grid_path))
    if not isinstance(plan, dict) or plan.get('Purpose') != 'BOOTSTRAP_CANDIDATE_SELECTION':
        raise ValueError('V3 direct calibration requires a non-fixture TRAIN plan')
    if 'MuEstimationMethods' in plan:
        raise ValueError('Source plan already specifies M/U methods')
    calibration = plan.get('Calibration')
    if not isinstance(calibration, dict):
        raise ValueError('Calibration object missing')
    options = calibration.get('ModelOptions')
    margins = calibration.get('ConflictMargins')
    declared = calibration.get('DeclaredSearchSpace')
    baselines = calibration.get('BaselineRuleSets')
    if (not isinstance(options, list) or not options
            or not isinstance(margins, list) or not margins
            or not isinstance(declared, dict)
            or not isinstance(declared.get('Catalogue'), list) or not declared['Catalogue']
            or not isinstance(baselines, list)
            or not isinstance(calibration.get('Budget'), dict)
            or not isinstance(calibration.get('SelectionPolicy'), dict)
            or not isinstance(calibration.get('ObservationCapDeclaration'), dict)):
        raise ValueError('Source plan lacks complete TRAIN comparison and provenance contract')
    # FDR is selected by the source JSON, never silently overridden by a CLI mode.
    # The observed TRAIN guard is not a statistical certificate.
    observed_limit = calibration['Budget'].get('MaximumObservedAcceptedFdrPoint')
    upper_limit = calibration['SelectionPolicy'].get('MaximumAcceptedFdrUpper')
    if (type(observed_limit) not in (float, int)
            or type(upper_limit) not in (float, int)
            or not math.isfinite(observed_limit)
            or not math.isfinite(upper_limit)
            or not (0 < upper_limit <= observed_limit <= .01)):
        raise ValueError('Explicit JSON FDR risk limits must satisfy 0 < upper <= observed <= 1')
    ids = [o.get('OptionId') for o in options if isinstance(o, dict)]
    if len(ids) != len(options) or any(not isinstance(x, str) or not x for x in ids) or len(set(ids)) != len(ids):
        raise ValueError('Source option identifiers invalid or duplicated')
    if any(o.get('JointSupportPolicy') is not None for o in options):
        raise ValueError('Source option has a competing M/U policy')
    paired_surnames = _validate_explicit_surname_modes(options)
    if paired_surnames and only_option_id is not None:
        raise ValueError('The JSON requests BOTH surname hypotheses; --only-option-id cannot remove one')
    if only_option_id is not None:
        if only_option_id not in ids:
            raise ValueError('Requested option is absent from the source plan')
        options = [o for o in options if o['OptionId'] == only_option_id]
    if only_margin is not None:
        if only_margin not in margins:
            raise ValueError('Requested margin is absent from the source plan')
        margins = [only_margin]
    # An active TF option must explicitly abstain rather than silently adopt
    # the C# RejectOperation=0 default. The original source plan is untouched.
    for option in options:
        slots = option.get('UnitTfOptions') or []
        if not isinstance(slots, list) or any(not isinstance(slot, dict) for slot in slots):
            raise ValueError('Malformed TF units: ' + option['OptionId'])
        active = (isinstance(option.get('TfWeight'), (int, float)) and option['TfWeight'] > 0
                  or any(isinstance(slot, dict) and slot.get('Weight', 0) > 0 for slot in slots))
        if active and option.get('UnpublishedTermPolicy', 1) != 1:
            raise ValueError('Active TF explicitly rejects instead of abstaining: ' + option['OptionId'])
        if active:
            option['UnpublishedTermPolicy'] = 1
    variants = math.prod(len(grid[k]) for k in FIELDS)
    # Existing nested calibrator starts with complete declared catalogue plus
    # baseline rulesets, for every model/margin/method; it may then refine.
    minimum = variants * len(options) * len(margins) * (1 + len(baselines))
    current = plan.get('MaximumSearchConfigurations')
    if (type(current) is not int or current < 1
            or type(calibration.get('MaximumComparedConfigurations')) is not int):
        raise ValueError('Invalid source budget')
    maximum = current if maximum_configurations is None else maximum_configurations
    if type(maximum) is not int or maximum < minimum:
        raise ValueError(f'Declared budget {maximum} cannot evaluate initial V3 grid of {minimum}; set explicit --maximum-configurations')
    if maximum < current and not initial_only:
        raise ValueError('Refuse to silently narrow source search budget outside explicit --initial-only study')
    if maximum > 1_000_000:
        raise ValueError('Manual compute budget must be <= 1000000')

    result = copy.deepcopy(plan)
    result['Calibration']['ModelOptions'] = options
    result['Calibration']['ConflictMargins'] = margins
    if initial_only:
        # Regression-only bounded run of initial controls. Not full adaptive selection.
        result['MaximumBatches'] = 1
        result['Calibration']['MaximumPhysicalRuleSets'] = min(
            2, result['Calibration'].get('MaximumPhysicalRuleSets', 2))
        result['Calibration']['Execution'].pop('TotalComparedConfigurations', None)
    result['MuEstimationMethods'] = [{'Method': METHOD, 'Parameters': grid}]
    result['MaximumSearchConfigurations'] = maximum
    result['Calibration']['MaximumComparedConfigurations'] = maximum
    basis = (VERSION + '|' + hashlib.sha256(original_bytes).hexdigest()
             + '|' + hashlib.sha256(grid_bytes).hexdigest() + '|' + str(maximum)
             + '|' + str(only_option_id) + '|' + str(only_margin)
             + '|' + str(initial_only))
    fingerprint = hashlib.sha256(basis.encode()).hexdigest()
    result['Calibration']['StudyFingerprintSha256'] = fingerprint
    result['Calibration']['Budget']['StudyFingerprintSha256'] = fingerprint
    data = (json.dumps(result, indent=2, allow_nan=False) + '\n').encode()
    # All preflight checks happen before the exclusive create.
    with output.open('xb') as handle:
        handle.write(data)
    return {'version': VERSION, 'method': METHOD, 'variants': variants,
            'baseOptions': len(options), 'initialComparisons': minimum,
            'initialOnly': initial_only, 'selectedOptionId': only_option_id,
            'selectedMargin': only_margin,
            'surnameDependenceExperiment': {
                'status': 'TWO_ARM_PHYSICAL_TRAIN_ONLY_NOT_CERTIFIED',
                'optionIds': [SURNAME_BILATERAL, SURNAME_SHARED],
                'sharedEligibleForSelection': False
            } if paired_surnames else None,
            'maximumConfigurations': maximum, 'studyFingerprintSha256': fingerprint,
            'inputPlanSha256': hashlib.sha256(original_bytes).hexdigest(),
            'inputGridSha256': hashlib.sha256(grid_bytes).hexdigest(),
            'outputPlanSha256': hashlib.sha256(data).hexdigest(),
            'testUsed': False, 'activated': False, 'certified': False}


if __name__ == '__main__':
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument('prepared_train_plan', type=Path)
    cli.add_argument('v3_parameter_grid', type=Path)
    cli.add_argument('output_plan', type=Path)
    cli.add_argument('--maximum-configurations', type=int, default=None,
                     help='Explicit configuration budget for the V3 grid')
    cli.add_argument('--only-option-id', default=None,
                     help='Restrict a physical technical run to a declared TF option')
    cli.add_argument('--only-margin', type=float, default=None,
                     help='Restrict a physical technical run to a declared margin')
    cli.add_argument('--initial-only', action='store_true',
                     help='One initial TRAIN batch only, never claim adaptive optimality')
    opts = cli.parse_args()
    print(json.dumps(prepare(opts.prepared_train_plan, opts.v3_parameter_grid,
                             opts.output_plan, opts.maximum_configurations,
                             opts.only_option_id, opts.only_margin,
                             opts.initial_only), sort_keys=True))
