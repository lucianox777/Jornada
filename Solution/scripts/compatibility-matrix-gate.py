#!/usr/bin/env python3
from __future__ import annotations
import json,re
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
CFG=ROOT/'config/release/compatibility-matrix.json'; HEALTH=ROOT/'src/Jornada.Api/ApiHealth.cs'; UPGRADE=ROOT/'scripts/local-ddl-upgrade.sh'
def fail(m): raise SystemExit('COMPATIBILITY MATRIX GATE: FAIL: '+m)
def main():
    d=json.loads(CFG.read_text(encoding='utf-8'))
    if d.get('schemaVersion')!=1 or d.get('status')!='VIGENTE': fail('config inválida')
    if d.get('baseNormativa')!='3.62' or d.get('solutionSchema')!='3.68': fail('schema esperado divergente')
    cases={x['id']:x for x in d.get('cases',[])}
    need={'API_CURRENT_SCHEMA_CURRENT','API_CURRENT_SCHEMA_V365','UPGRADE_V365_TO_CURRENT','DDL_REAPPLY_CURRENT'}
    if set(cases)!=need: fail('casos obrigatórios divergentes')
    h=HEALTH.read_text(encoding='utf-8')
    for token in ('N\'Jornada.BaseNormativa\'','N\'Jornada.SolutionSchema\'','N\'3.62\'','N\'3.68\'','SQL_SCHEMA_INCOMPATIVEL'):
        if token not in h: fail('readiness sem contrato exato: '+token)
    u=UPGRADE.read_text(encoding='utf-8')
    for token in ('Jornada_Fase1_v3.65.sql','Jornada_Seed_Dev_v3.65.sql','idempotent=true','schema_marker_exact=true'):
        if token not in u: fail('upgrade harness sem evidência: '+token)
    if cases['API_CURRENT_SCHEMA_V365']['expected']!='DENY_SQL_SCHEMA_INCOMPATIVEL': fail('N-1 deve ser deny até upgrade')
    print('COMPATIBILITY MATRIX GATE: OK (current=3.68; v3.65=upgrade-required; reapply=idempotent)')
    return 0
if __name__=='__main__': raise SystemExit(main())
