#!/usr/bin/env python3
from __future__ import annotations
import argparse,datetime as dt,hashlib,json,re,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
POLICY=ROOT/'config/hml/calibration-validity-policy.json'; PARAMS=ROOT/'config/hml/parameters.json'; PERF=ROOT/'config/hml/performance-baseline.json'; LINK=ROOT/'config/hml/linkage-evaluation-policy.json'; SQLPERF=ROOT/'config/hml/sql-performance-policy.json'; APIPROJ=ROOT/'config/hml/api-projection-load-policy.json'; RELEASE=ROOT.parent/'RELEASE_INFO.txt'
SHA_RE=re.compile(r'^[0-9a-f]{64}$')

def parse_release():
    return dict(line.split('=',1) for line in RELEASE.read_text(encoding='utf-8').splitlines() if '=' in line)
def parse_utc(v): return dt.datetime.fromisoformat(v[:-1]+'+00:00') if isinstance(v,str) and v.endswith('Z') else None
def fail(errors):
    for e in errors: print('ERRO:',e,file=sys.stderr)
    return 2

def technical_fingerprint(policy: dict) -> str:
    patterns=policy.get('technicalFingerprintInputs')
    if not isinstance(patterns,list) or not patterns: raise ValueError('technicalFingerprintInputs ausente')
    matched: dict[str,Path]={}
    for pat in patterns:
        if not isinstance(pat,str) or not pat.strip(): raise ValueError('technicalFingerprintInputs contém padrão inválido')
        for path in ROOT.glob(pat):
            if path.is_file(): matched[path.relative_to(ROOT).as_posix()]=path
    if not matched: raise ValueError('technicalFingerprintInputs não selecionou arquivos')
    h=hashlib.sha256()
    for rel,path in sorted(matched.items()):
        h.update(rel.encode('utf-8')); h.update(b'\0'); h.update(hashlib.sha256(path.read_bytes()).digest()); h.update(b'\n')
    return h.hexdigest()

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--strict',action='store_true'); ap.add_argument('--now-utc'); ap.add_argument('--print-fingerprint',action='store_true'); a=ap.parse_args()
    policy=json.loads(POLICY.read_text()); rel=parse_release(); now=parse_utc(a.now_utc) if a.now_utc else dt.datetime.now(dt.timezone.utc)
    if now is None: print('ERRO: --now-utc inválido',file=sys.stderr); return 2
    try: current_fp=technical_fingerprint(policy)
    except Exception as e: print('ERRO:',e,file=sys.stderr); return 2
    if a.print_fingerprint:
        print(current_fp); return 0
    max_days=policy.get('maximumApprovalAgeDays'); errors=[]; pending=[]
    if not isinstance(max_days,int) or max_days<=0: errors.append('maximumApprovalAgeDays inválido')
    for path,name in ((PARAMS,'parameters'),(PERF,'performance-baseline'),(LINK,'linkage-policy'),(SQLPERF,'sql-performance-policy'),(APIPROJ,'api-projection-policy')):
        d=json.loads(path.read_text()); status=d.get('status')
        if status!='APROVADO': pending.append(name); continue
        approved=parse_utc(d.get('approvedAtUtc')); ctx=d.get('approvalContext')
        if approved is None: errors.append(name+': approvedAtUtc inválido'); continue
        if now-approved>dt.timedelta(days=max_days): errors.append(name+': aprovação expirada')
        if not isinstance(ctx,dict): errors.append(name+': approvalContext ausente'); continue
        if ctx.get('solutionSchema')!=rel.get('schema_solution','').removeprefix('v'): errors.append(name+': solutionSchema de aprovação divergente')
        expected_env=__import__('os').environ.get('JORNADA_HML_ENVIRONMENT_PROFILE')
        if expected_env and ctx.get('environmentProfile')!=expected_env: errors.append(name+': environmentProfile de aprovação divergente do ambiente atual')
        for f in ('sourceGitTag','environmentProfile','corpusDefinitionSha256','technicalFingerprintSha256'):
            if not ctx.get(f): errors.append(name+': approvalContext.'+f+' ausente')
        corpus=ctx.get('corpusDefinitionSha256')
        if corpus and not SHA_RE.fullmatch(str(corpus).lower()): errors.append(name+': corpusDefinitionSha256 inválido')
        approved_fp=str(ctx.get('technicalFingerprintSha256') or '').lower()
        if approved_fp and not SHA_RE.fullmatch(approved_fp): errors.append(name+': technicalFingerprintSha256 inválido')
        elif approved_fp and approved_fp!=current_fp: errors.append(name+': aprovação invalidada por mudança no fingerprint técnico')
    if a.strict and pending: errors.append('aprovações ainda pendentes: '+', '.join(pending))
    if errors: return fail(errors)
    print('HML STALENESS GATE: OK (pending='+(','.join(pending) if pending else 'none')+'; technicalFingerprint='+current_fp[:12]+')')
    return 0
if __name__=='__main__': raise SystemExit(main())
