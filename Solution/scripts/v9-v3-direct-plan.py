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


def prepare(source, grid_path, output, maximum_configurations=None):
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
    ids = [o.get('OptionId') for o in options if isinstance(o, dict)]
    if len(ids) != len(options) or any(not isinstance(x, str) or not x for x in ids) or len(set(ids)) != len(ids):
        raise ValueError('Source option identifiers invalid or duplicated')
    if any(o.get('JointSupportPolicy') is not None for o in options):
        raise ValueError('Source option has a competing M/U policy')
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
    if maximum < current:
        raise ValueError('Refuse to silently narrow source search budget')
    if maximum > 1_000_000:
        raise ValueError('Manual compute budget must be <= 1000000')

    result = copy.deepcopy(plan)
    result['MuEstimationMethods'] = [{'Method': METHOD, 'Parameters': grid}]
    result['MaximumSearchConfigurations'] = maximum
    result['Calibration']['MaximumComparedConfigurations'] = maximum
    basis = (VERSION + '|' + hashlib.sha256(original_bytes).hexdigest()
             + '|' + hashlib.sha256(grid_bytes).hexdigest() + '|' + str(maximum))
    fingerprint = hashlib.sha256(basis.encode()).hexdigest()
    result['Calibration']['StudyFingerprintSha256'] = fingerprint
    result['Calibration']['Budget']['StudyFingerprintSha256'] = fingerprint
    data = (json.dumps(result, indent=2, allow_nan=False) + '\n').encode()
    # All preflight checks happen before the exclusive create.
    with output.open('xb') as handle:
        handle.write(data)
    return {'version': VERSION, 'method': METHOD, 'variants': variants,
            'baseOptions': len(options), 'initialComparisons': minimum,
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
                     help='Explicit budget increase for initial grid plus adaptive search')
    opts = cli.parse_args()
    print(json.dumps(prepare(opts.prepared_train_plan, opts.v3_parameter_grid,
                             opts.output_plan, opts.maximum_configurations), sort_keys=True))
