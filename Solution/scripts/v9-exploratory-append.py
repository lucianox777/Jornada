#!/usr/bin/env python3
"""Append validated exploratory V9 ModelOptions to a NEW plan, never mutate source.

Usage: python3 scripts/v9-exploratory-append.py source-plan.json candidates.json derived-plan.json
Candidates JSON contract:
 {"Candidates":[{"ModelOption":{...},"Evidence":{"Status":"VALIDATED_TRAIN",
 "Recall":0.98,"FdrUpper":0.002,"BaselineRecall":0.97,
 "BaselineFdrUpper":0.003,"StudyFingerprintSha256":"<64 hex>"}}]}
This tool does not perform calibration, certify TEST, or activate REF.
"""
import argparse
import copy
import hashlib
import json
import math
from pathlib import Path

def sha(data):
    return hashlib.sha256(data).hexdigest()

def canonical(obj):
    return json.dumps(obj, sort_keys=True, separators=(',', ':'), allow_nan=False)

def load(path):
    def unique(pairs):
        obj = {}
        for key, value in pairs:
            if key in obj:
                raise ValueError('Duplicate JSON key: ' + key)
            obj[key] = value
        return obj
    def reject(value):
        raise ValueError('Non-finite JSON number: ' + value)
    return json.loads(Path(path).read_text(encoding='utf-8'),
                      object_pairs_hook=unique, parse_constant=reject)

def append(source_path, candidate_path, output_path):
    source_path, candidate_path, output_path = map(Path, (source_path, candidate_path, output_path))
    if output_path.resolve() in (source_path.resolve(), candidate_path.resolve()):
        raise ValueError('Output must be distinct from both inputs')
    if output_path.exists():
        raise FileExistsError(output_path)
    source_bytes = source_path.read_bytes()
    candidates_bytes = candidate_path.read_bytes()
    source = load(source_path)
    payload = load(candidate_path)
    if not isinstance(source, dict) or not isinstance(source.get('Calibration'), dict):
        raise ValueError('Source lacks Calibration')
    old = source['Calibration'].get('ModelOptions')
    if not isinstance(old, list) or not old or any(not isinstance(x, dict) for x in old):
        raise ValueError('Source lacks valid ModelOptions')
    if not isinstance(payload, dict) or set(payload) != {'Candidates'} or not isinstance(payload['Candidates'], list):
        raise ValueError('Expected Candidates array')
    ids = {x.get('OptionId') for x in old}
    if len(ids) != len(old) or None in ids:
        raise ValueError('Duplicate or missing existing OptionId')
    signatures = {canonical({k:v for k,v in x.items() if k not in ('OptionId','EligibleForSelection')}) for x in old}
    additions = []
    for item in payload['Candidates']:
        if not isinstance(item, dict) or set(item) != {'ModelOption','Evidence'}:
            raise ValueError('Each candidate requires ModelOption and Evidence')
        option, ev = item['ModelOption'], item['Evidence']
        if not isinstance(option, dict) or not isinstance(ev, dict):
            raise ValueError('Malformed candidate')
        ident = option.get('OptionId')
        if not isinstance(ident, str) or not ident.startswith('EXP_') or ident in ids:
            raise ValueError('Exploratory OptionId must be unique and start EXP_')
        if option.get('EligibleForSelection') is not False:
            raise ValueError('New options must be ineligible')
        if option.get('JointSupportPolicy') is not None:
            raise ValueError('M/U joint support policy is not an individual ModelOption')
        if option.get('SharedSurnameMode') == 1 and option.get('EligibleForSelection') is not False:
            raise ValueError('Shared surname remains diagnostic')
        slots = option.get('UnitTfOptions') or []
        if (option.get('TfWeight', 0) > 0 or any(s.get('Weight', 0) > 0 for s in slots)) and option.get('UnpublishedTermPolicy') != 1:
            raise ValueError('Active TF must abstain on unpublished terms')
        if ev.get('Status') != 'VALIDATED_TRAIN':
            raise ValueError('Candidate lacks TRAIN validation')
        fingerprint = ev.get('StudyFingerprintSha256')
        if not isinstance(fingerprint, str) or len(fingerprint) != 64 or any(c not in '0123456789abcdef' for c in fingerprint):
            raise ValueError('Missing study fingerprint')
        metrics = ('Recall','FdrUpper','BaselineRecall','BaselineFdrUpper')
        if any(type(ev.get(k)) not in (float,int) or not math.isfinite(ev[k]) or not 0 <= ev[k] <= 1 for k in metrics):
            raise ValueError('Invalid metric')
        # Strict Pareto dominance, no arbitrary thresholds and no deterioration.
        if not (ev['Recall'] >= ev['BaselineRecall'] and ev['FdrUpper'] <= ev['BaselineFdrUpper']
                and (ev['Recall'] > ev['BaselineRecall'] or ev['FdrUpper'] < ev['BaselineFdrUpper'])):
            raise ValueError('Candidate does not dominate declared baseline')
        signature = canonical({k:v for k,v in option.items() if k not in ('OptionId','EligibleForSelection')})
        if signature in signatures:
            raise ValueError('Duplicate option parameters')
        signatures.add(signature)
        ids.add(ident)
        additions.append((copy.deepcopy(option), copy.deepcopy(ev)))
    result = copy.deepcopy(source)
    result['Calibration']['ModelOptions'].extend(x for x,_ in additions)
    # New fingerprint prevents accidentally reusing prior TRAIN results.
    basis = canonical({'source':sha(source_bytes),'candidates':sha(candidates_bytes),
                       'options':[x['OptionId'] for x,_ in additions]})
    fingerprint = sha(basis.encode('utf-8'))
    result['Calibration']['StudyFingerprintSha256'] = fingerprint
    if isinstance(result['Calibration'].get('Budget'), dict):
        result['Calibration']['Budget']['StudyFingerprintSha256'] = fingerprint
    result['ExploratoryAppendProvenance'] = {
        'SourcePlanSha256':sha(source_bytes),
        'CandidateEvidenceSha256':sha(candidates_bytes),
        'StudyFingerprintSha256':fingerprint,
        'AddedOptions':[{'OptionId':x['OptionId'],'Evidence':ev} for x,ev in additions],
        'ApprovedPlanUnchanged':True,
        'AutoActivate':False,
        'TestCertified':False
    }
    data = (json.dumps(result, indent=2, ensure_ascii=False, allow_nan=False)+'\n').encode('utf-8')
    with output_path.open('xb') as f:
        f.write(data)
    return {'added':len(additions),'outputSha256':sha(data),'sourceSha256':sha(source_bytes),
            'studyFingerprintSha256':fingerprint,'activated':False}

if __name__ == '__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('source_plan')
    p.add_argument('candidate_evidence')
    p.add_argument('derived_plan')
    args=p.parse_args()
    print(json.dumps(append(args.source_plan,args.candidate_evidence,args.derived_plan),sort_keys=True))
