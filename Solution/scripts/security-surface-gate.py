#!/usr/bin/env python3
from __future__ import annotations
import json,re,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
POLICY=ROOT/'config/security/data-minimization-policy.json'
API=ROOT/'src/Jornada.Api'
SRC=ROOT/'src'

def fail(m): raise SystemExit('SECURITY SURFACE GATE: FAIL: '+m)

def main():
    p=json.loads(POLICY.read_text(encoding='utf-8'))
    if p.get('schemaVersion')!=1 or p.get('status')!='VIGENTE': fail('policy inválida')
    forbidden=[x.lower() for x in p.get('forbiddenLogIdentifiers',[])]
    logger_calls=[]
    for f in SRC.rglob('*.cs'):
        text=f.read_text(encoding='utf-8')
        # Extrai templates literais usados diretamente em logger.Log* em TODA a Solution.
        for m in re.finditer(r'logger\.Log(?:Information|Warning|Error|Critical|Debug|Trace)\([^;]*?"([^"]*)"',text,re.S):
            template=m.group(1); logger_calls.append((str(f.relative_to(ROOT)),template))
            low=template.lower().replace('correlationid','').replace('auditpersistencems','')
            hits=[x for x in forbidden if x in low]
            if hits: fail(f'{f.relative_to(ROOT)}: template de log menciona identificador sensível {hits}: {template!r}')
        # Mensagens de exceção podem ecoar conteúdo/identificadores controlados pela origem.
        # Logger deve usar código técnico estável; a exceção não vira argumento textual.
        for call in re.finditer(r'logger\.Log(?:Information|Warning|Error|Critical|Debug|Trace)\((.*?);', text, re.S):
            if re.search(r'\b(?:ex|exception)\.Message\b', call.group(1), re.I):
                fail(f'{f.relative_to(ROOT)}: logger interpola Exception.Message diretamente')
    audit=(API/'ApiAuditMiddleware.cs').read_text(encoding='utf-8')
    for token in [
      'http.Request.Headers.Remove("X-Jornada-Agente-CPF")',
      'Não registrar headers, body, CPF nem access key',
    ]:
        if token not in audit: fail('ApiAuditMiddleware perdeu salvaguarda: '+token)
    audit_sink=(API/'ApiAuditSink.cs').read_text(encoding='utf-8')
    for token in ['agente_cpf_hash', 'agente_hash_versao', 'controle.api_evento']:
        if token not in audit_sink: fail('SqlApiAuditSink perdeu salvaguarda/persistência: '+token)
    projection=(API/'SqlApiServices.cs').read_text(encoding='utf-8')
    if 'if (_properties.Contains(name)) output[name] = value;' not in projection:
        fail('projeção de Pessoa perdeu allowlist dirigida pelo JSON Schema')
    if 'JsonSerializer.SerializeToElement(row)' in projection or 'JsonSerializer.Serialize(row)' in projection:
        fail('projeção de Pessoa serializa a linha operacional inteira em vez da allowlist')
    if 'controle.fn_projecao_jornada_permitida' not in projection:
        fail('atributos transversais perderam filtro de restrição setorial')
    program=(API/'Program.cs').read_text(encoding='utf-8')
    if 'logger.Log' in program and 'X-Jornada-Access-Key' in program: fail('Program.cs mistura logging e access key; revisar exposição')
    # Não aceitar serialização explícita de headers/body completos em fontes da API.
    for f in API.glob('*.cs'):
        t=f.read_text(encoding='utf-8')
        for bad in ('JsonSerializer.Serialize(http.Request.Headers','JsonSerializer.Serialize(http.Request.Body','Request.Headers.ToString()'):
            if bad in t: fail(f'{f.name}: serialização sensível detectada: {bad}')
    print(f'SECURITY SURFACE GATE: OK (loggerTemplates={len(logger_calls)})')
    return 0
if __name__=='__main__': raise SystemExit(main())
