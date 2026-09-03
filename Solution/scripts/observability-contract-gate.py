#!/usr/bin/env python3
from __future__ import annotations
import datetime as dt, hashlib, json, re, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
CFG=ROOT/'config/observability/metrics-slo-catalog.json'
TELEMETRY=ROOT/'src/Jornada.Contracts/JornadaTelemetry.cs'
SHA=re.compile(r'^[0-9a-f]{64}$')
def utc(v):
    if not isinstance(v,str) or not v.endswith('Z'): return False
    try: dt.datetime.fromisoformat(v[:-1]+'+00:00'); return True
    except ValueError: return False
def fail(m): raise SystemExit('OBSERVABILITY CONTRACT GATE: FAIL: '+m)
def main():
    d=json.loads(CFG.read_text(encoding='utf-8'))
    if d.get('schemaVersion')!=1 or d.get('status') not in {'PENDENTE_HML','APROVADO'}: fail('catálogo inválido')
    metrics=d.get('metrics'); slos=d.get('slos'); alerts=d.get('alerts')
    if not isinstance(metrics,list) or not metrics: fail('metrics ausentes')
    ids=[]
    for x in metrics:
        mid=x.get('id');
        if not isinstance(mid,str) or not mid.startswith('jornada.'): fail('metric id inválido')
        ids.append(mid)
        if x.get('kind') not in {'counter','histogram','gauge'}: fail(mid+': kind inválido')
        if x.get('thresholdStatus') not in {'PENDENTE','APROVADO'}: fail(mid+': thresholdStatus inválido')
    if len(ids)!=len(set(ids)): fail('metric id duplicado')
    src=TELEMETRY.read_text(encoding='utf-8') if TELEMETRY.is_file() else ''
    for mid in ids:
        if mid not in src: fail('instrumento não declarado em JornadaTelemetry: '+mid)
    # Dimensões também são contrato: impede cardinalidade nova/sensível sem revisão do catálogo.
    for metric in metrics:
        dims=metric.get('requiredDimensions')
        if not isinstance(dims,list) or any(not isinstance(x,str) or not x for x in dims): fail(metric.get('id','?')+': requiredDimensions inválido')
        for dim in dims:
            if f'\"{dim}\"' not in src: fail(metric['id']+': dimensão não encontrada em JornadaTelemetry: '+dim)
    by_id=set(ids)
    usage = {
        "src/Jornada.Api/ApiAuditMiddleware.cs": ["RecordApiRequest", "RecordApiAuditPersistence"],
        "src/Jornada.Bronze.Maintenance.Worker/BronzeMaintenance.cs": ["RecordBronzeMaintenance"],
        "src/Jornada.Processor.Worker/IngestionProcessor.cs": ["RecordProcessorDelivery"],
        "src/Jornada.Pipeline.Coordination/SqlPipelineCoordinator.cs": ["RecordPipelineLostToken"],
        "src/Jornada.Linkage.Runner/LinkageRunnerWorker.cs": ["RecordLinkageRun"],
        "src/Jornada.Operations.Maintenance.Worker/PipelineWatchdog.cs": ["RecordIdentityPendingAge"],
    }
    for rel, methods in usage.items():
        text=(ROOT/rel).read_text(encoding="utf-8")
        for method in methods:
            if method not in text: fail(f"instrumentação ausente: {rel}:{method}")
    for s in slos or []:
        if s.get('metric') not in by_id: fail('SLO aponta para métrica inexistente: '+str(s.get('id')))
        if s.get('status') not in {'PENDENTE','APROVADO'}: fail('SLO com status inválido: '+str(s.get('id')))
        if s.get('status')=='APROVADO':
            if s.get('objective') is None or not s.get('window'): fail('SLO aprovado sem objective/window')
            if not utc(s.get('approvedAtUtc')) or not s.get('approvedBy'): fail('SLO aprovado sem aprovação completa')
            ev=s.get('evidence') or {}
            if not SHA.fullmatch(str(ev.get('sha256','')).lower()): fail('SLO aprovado sem evidence.sha256')
    if not isinstance(alerts,list) or not alerts: fail('alerts ausentes')
    alert_ids=set()
    for alert in alerts:
        aid=alert.get('id')
        if not isinstance(aid,str) or not aid: fail('alert id inválido')
        if aid in alert_ids: fail('alert id duplicado: '+aid)
        alert_ids.add(aid)
        if alert.get('metric') not in by_id: fail('alert aponta para métrica inexistente: '+aid)
        if alert.get('severity') not in {'INFO','WARNING','CRITICAL'}: fail('alert severity inválida: '+aid)
        if alert.get('status') not in {'PENDENTE','APROVADO'}: fail('alert status inválido: '+aid)
        if alert.get('status')=='APROVADO':
            if not alert.get('condition') or not alert.get('window'): fail('alert aprovado sem condition/window: '+aid)
            if not utc(alert.get('approvedAtUtc')) or not alert.get('approvedBy'): fail('alert aprovado sem aprovação completa: '+aid)
            ev=alert.get('evidence') or {}
            if not SHA.fullmatch(str(ev.get('sha256','')).lower()): fail('alert aprovado sem evidence.sha256: '+aid)
    print(f'OBSERVABILITY CONTRACT GATE: OK (metrics={len(ids)}; slos={len(slos or [])}; alerts={len(alerts)}; status={d.get("status")})')
    return 0
if __name__=='__main__': raise SystemExit(main())
