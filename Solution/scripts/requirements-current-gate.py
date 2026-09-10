#!/usr/bin/env python3
from __future__ import annotations
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REQ = ROOT.parent / 'Documentos' / 'Requisitos'
CURRENT = REQ / 'requirements-map-v1.1.json'
RF = REQ / '02_Requisitos_Funcionais_Jornada_v1.1.md'
RNF = REQ / '03_Requisitos_Nao_Funcionais_Jornada_v1.1.md'
TRACE = REQ / '05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1.md'
ADENDO = REQ / '06_Adendo_RF_RNF_Linkage_Calibracao_Avaliacao_v1.0.md'


def fail(message: str) -> None:
    raise SystemExit('REQUIREMENTS CURRENT GATE: FAIL: ' + message)


def main() -> None:
    data = json.loads(CURRENT.read_text(encoding='utf-8'))
    if data.get('schemaVersion') != 2 or data.get('status') != 'VIGENTE':
        fail('mapa corrente inválido')
    if data.get('counts') != {'RN': 36, 'RF': 56, 'RNF': 37, 'RT': 65}:
        fail('contagens correntes divergentes')

    additive_rf = data.get('additiveRf', {})
    expected_rf = {f'RF-{i:03d}' for i in range(51, 57)}
    if set(additive_rf) != expected_rf:
        fail('RF-051..RF-056 não estão cobertos exatamente uma vez')

    additive_rnf = data.get('additiveRnf', {})
    expected_rnf = {'RNF34-A', 'RNF34-B', 'RNF34-C', 'RNF34-D'}
    if set(additive_rnf) != expected_rnf:
        fail('RNF34-A..D não estão cobertos exatamente uma vez')

    for req in additive_rf.values():
        for rnf in req.get('rnf', []):
            if re.fullmatch(r'RNF\d{2}', rnf):
                fail('identificador RNF legado sem hífen no mapa corrente: ' + rnf)
            if rnf.startswith('RNF-') and not re.fullmatch(r'RNF-\d{3}', rnf):
                fail('identificador RNF corrente inválido: ' + rnf)

    rf_text = RF.read_text(encoding='utf-8')
    rnf_text = RNF.read_text(encoding='utf-8')
    trace_text = TRACE.read_text(encoding='utf-8')
    for rid in sorted(expected_rf):
        if rf_text.count('## ' + rid + ' ') != 1:
            fail(rid + ' ausente/duplicado em RF v1.1')
        if rid not in trace_text:
            fail(rid + ' ausente na matriz v1.1')
    for rid in sorted(expected_rnf):
        if ('## ' + rid + ' ') not in rnf_text:
            fail(rid + ' ausente em RNF v1.1')
        if rid not in trace_text:
            fail(rid + ' ausente na matriz v1.1')

    adendo = ADENDO.read_text(encoding='utf-8')
    if 'Status:** HISTÓRICO — SUPERADO' not in adendo:
        fail('Adendo 06 ainda aparenta ser norma concorrente')

    if 'Microsoft SQL Server é a tecnologia relacional normativa' not in rnf_text:
        fail('tecnologia relacional normativa não está explícita')

    print('REQUIREMENTS CURRENT GATE: OK (RN=36; RF=56; RNF=37; RT=65; IDs canônicos e adendo histórico)')


if __name__ == '__main__':
    main()
