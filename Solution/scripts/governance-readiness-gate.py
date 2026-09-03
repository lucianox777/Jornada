#!/usr/bin/env python3
from __future__ import annotations
import argparse, hashlib, json, re, sys
from pathlib import Path
from datetime import datetime

SHA=re.compile(r'^[0-9a-f]{64}$')
ROOT=Path(__file__).resolve().parents[1]


def fail(msg:str)->None:
    raise SystemExit('GOVERNANCE READINESS GATE: FAIL: '+msg)

def load(p:Path):
    if not p.is_file(): fail(f'arquivo ausente: {p.relative_to(ROOT)}')
    try: return json.loads(p.read_text(encoding='utf-8'))
    except Exception as e: fail(f'JSON inválido {p.name}: {e}')

def utc(v):
    if not isinstance(v,str) or not v.endswith('Z'): return False
    try: datetime.fromisoformat(v[:-1]+'+00:00'); return True
    except ValueError: return False

def evidence(obj,prefix):
    if not isinstance(obj,dict): fail(f'{prefix}.evidence ausente')
    if not isinstance(obj.get('artifact'),str) or not obj['artifact'].strip(): fail(f'{prefix}.evidence.artifact ausente')
    s=str(obj.get('sha256','')).lower()
    if not SHA.fullmatch(s): fail(f'{prefix}.evidence.sha256 inválido')

def approval(obj,prefix):
    if not isinstance(obj,dict): fail(f'{prefix}.approval ausente')
    if not utc(obj.get('approvedAtUtc')): fail(f'{prefix}.approvedAtUtc inválido')
    if not isinstance(obj.get('approvedBy'),str) or not obj['approvedBy'].strip(): fail(f'{prefix}.approvedBy ausente')
    evidence(obj.get('evidence'),prefix)


def validate_schema(root:Path, require:bool)->dict:
    p=root/'config/governance/schema-approvals.json'; d=load(p)
    if d.get('schemaVersion')!=1 or d.get('baseNormativa')!='3.62' or d.get('solutionSchema')!='3.68': fail('schema-approvals versão/base/schema inválidos')
    actual={x.relative_to(root).as_posix():hashlib.sha256(x.read_bytes()).hexdigest() for x in sorted((root/'config/contracts').rglob('*.json'))}
    listed={}
    for row in d.get('contracts',[]):
        path=row.get('path'); h=str(row.get('sha256','')).lower()
        if path in listed: fail(f'schema duplicado: {path}')
        if path not in actual: fail(f'schema catalogado inexistente: {path}')
        if actual[path]!=h: fail(f'hash divergente do schema: {path}')
        if row.get('status') not in {'PENDENTE','APROVADO'}: fail(f'status inválido: {path}')
        if row.get('status')=='APROVADO':
            approval(row.get('approval'),f'contracts[{path}]')
            if str(row['approval'].get('approvedContentSha256','')).lower()!=h:
                fail(f'contracts[{path}].approval.approvedContentSha256 deve coincidir com o conteúdo aprovado')
        listed[path]=row
    if set(listed)!=set(actual):
        miss=sorted(set(actual)-set(listed)); extra=sorted(set(listed)-set(actual))
        fail(f'inventário de schemas divergente missing={miss} extra={extra}')
    approved=bool(listed) and all(r.get('status')=='APROVADO' for r in listed.values()) and d.get('status')=='APROVADO'
    if d.get('status')=='APROVADO' and not approved: fail('schema-approvals APROVADO exige todos os schemas aprovados')
    if require and not approved: fail('schemas ainda não estão integralmente APROVADOS')
    return {'contracts':len(actual),'approved':approved}


def validate_retention(root:Path, require:bool)->dict:
    d=load(root/'config/governance/retention-dr-policy.json')
    if d.get('schemaVersion')!=1: fail('retention-dr schemaVersion inválido')
    for sec in ('itemProcessed','bronze','dr'):
        if not isinstance(d.get(sec),dict): fail(f'retention-dr.{sec} ausente')
        if d[sec].get('status') not in {'PENDENTE','APROVADO'}: fail(f'retention-dr.{sec}.status inválido')
    approved=d.get('status')=='APROVADO'
    if approved:
        ip=d['itemProcessed']; br=d['bronze']; dr=d['dr']
        numeric=[('detailRetentionDays',ip.get('detailRetentionDays')),('retentionDays',br.get('retentionDays')),('orphanGraceHours',br.get('orphanGraceHours')),('scanIntervalMinutes',br.get('scanIntervalMinutes')),('maxObjectsPerCycle',br.get('maxObjectsPerCycle')),('rpoMinutes',dr.get('rpoMinutes')),('rtoMinutes',dr.get('rtoMinutes')),('backupFrequencyMinutes',dr.get('backupFrequencyMinutes'))]
        for name,val in numeric:
            if isinstance(val,bool) or not isinstance(val,int) or val<=0: fail(f'retention-dr {name} deve ser inteiro >0 quando APROVADO')
        if not isinstance(dr.get('restoreEvidencePath'),str) or not dr['restoreEvidencePath'].strip(): fail('restoreEvidencePath ausente')
        if not SHA.fullmatch(str(dr.get('restoreEvidenceSha256','')).lower()): fail('restoreEvidenceSha256 inválido')
        approval(d.get('approval'),'retention-dr')
    if require and not approved: fail('retenção/DR ainda não está APROVADA')
    return {'approved':approved}


def validate_identity(root:Path, require:bool)->dict:
    d=load(root/'config/governance/identity-pending-lifecycle.json')
    required={'PENDENTE_PROBABILISTICO','NAO_RESOLVIDO','CONFLITO'}
    rows=d.get('states') if isinstance(d.get('states'),list) else []
    states={r.get('state'):r for r in rows if isinstance(r,dict)}
    if set(states)!=required: fail(f'identity lifecycle deve cobrir {sorted(required)}')
    allowed=set(d.get('allowedActions') or [])
    for name,row in states.items():
        if row.get('autoMutate') is not False: fail(f'{name}: autoMutate deve permanecer false; ação institucional não é automática')
        if row.get('status') not in {'PENDENTE','APROVADO'}: fail(f'{name}: status inválido')
        if row.get('status')=='APROVADO':
            age=row.get('agingDays'); action=row.get('action')
            if isinstance(age,bool) or not isinstance(age,int) or age<=0: fail(f'{name}: agingDays inválido')
            if action not in allowed: fail(f'{name}: action inválida')
    approved=d.get('status')=='APROVADO' and all(r.get('status')=='APROVADO' for r in states.values())
    if d.get('status')=='APROVADO':
        if not approved: fail('identity lifecycle APROVADO exige todos os estados aprovados')
        approval(d.get('approval'),'identity-lifecycle')
    if require and not approved: fail('política de ciclo de vida de identidade ainda não está APROVADA')
    return {'states':len(states),'approved':approved}


def main()->int:
    ap=argparse.ArgumentParser()
    ap.add_argument('--root',default=str(ROOT))
    ap.add_argument('--require-approved',action='store_true')
    ap.add_argument('--summary')
    ap.add_argument('--self-test',action='store_true')
    args=ap.parse_args(); root=Path(args.root).resolve()
    if args.self_test:
        # O self-test útil aqui é estrutural: o conjunto distribuído PENDENTE deve passar em modo não estrito e falhar no estrito.
        validate_schema(root,False); validate_retention(root,False); validate_identity(root,False)
        try:
            validate_schema(root,True)
        except SystemExit:
            print('GOVERNANCE READINESS GATE SELF-TEST: OK (pending accepted non-strict; strict rejects)')
            return 0
        fail('self-test esperava rejeição em modo estrito')
    result={'schemaVersion':1,'status':'PASS','schemaApprovals':validate_schema(root,args.require_approved),'retentionDr':validate_retention(root,args.require_approved),'identityLifecycle':validate_identity(root,args.require_approved),'requireApproved':args.require_approved}
    if args.summary:
        out=Path(args.summary); out.parent.mkdir(parents=True,exist_ok=True); out.write_text(json.dumps(result,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
    print(f"GOVERNANCE READINESS GATE: OK (schemas={result['schemaApprovals']['contracts']}; approved={args.require_approved})")
    return 0
if __name__=='__main__':
    raise SystemExit(main())
