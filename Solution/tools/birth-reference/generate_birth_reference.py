#!/usr/bin/env python3
"""Generate JORNADA_SYNTH_BIRTH_DAILY_V1 from an immutable IBGE 2024 projection REF."""
from __future__ import annotations
import argparse,csv,hashlib,json,math
from collections import defaultdict
from datetime import date,timedelta
from pathlib import Path

SCHEMA="JORNADA_SYNTH_BIRTH_DAILY_V1"
REFERENCE_DATE=date(2026,7,1)
SOURCE_XLSX="projecoes_2024_tab1_idade_simples.xlsx"
SOURCE_XLSX_SHA256="6E5C3D21A2E8FF50BADD7BE2785E1664B41A43277543BE541641B0CD802C3205"
EXPECTED_TOTAL=46179008
MAX_SYNTHETIC_AGE=115

def read_ref(path:Path):
    ages={}
    with path.open(encoding="utf-8-sig",newline="") as f:
        for row in csv.DictReader(f):
            try: age=int(row["idade"]); pop=int(row["populacao"])
            except (KeyError,ValueError) as e: raise ValueError("IBGE REF: linha idade/populacao invalida") from e
            if age in ages: raise ValueError(f"IBGE REF: idade duplicada: {age}")
            if not 0<=age<=90 or pop<=0: raise ValueError(f"IBGE REF: idade/populacao invalida: {age}/{pop}")
            ages[age]=pop
    missing=[a for a in range(91) if a not in ages]
    if missing: raise ValueError(f"IBGE REF: idades ausentes: {missing}")
    total=sum(ages.values())
    if total!=EXPECTED_TOTAL: raise ValueError(f"IBGE REF: soma etaria {total} difere do Total congelado {EXPECTED_TOTAL}")
    return ages,total

def apportion(total:int,weights):
    s=sum(weights)
    if total<0 or not weights or s<=0: raise ValueError("rateio invalido")
    raw=[total*w/s for w in weights]; out=[math.floor(x) for x in raw]
    for i in sorted(range(len(raw)),key=lambda i:(-(raw[i]-out[i]),i))[:total-sum(out)]: out[i]+=1
    return out

def expand_90_plus(ages):
    ratio=ages[89]/ages[88]
    if not 0<ratio<1: raise ValueError(f"IBGE REF: razao 89/88 invalida: {ratio}")
    # The source publishes one open 90+ cell. Use only the source-derived
    # 89/88 ratio for relative weights, but bound the synthetic disaggregation
    # to the repository's declared contemporary plausibility support. The cap
    # is a modelling guard, not an IBGE observation; largest remainder keeps
    # the complete published 90+ population.
    weights=[ratio**i for i in range(MAX_SYNTHETIC_AGE-90+1)]
    allocated=apportion(ages[90],weights)
    return [(90+i,v) for i,v in enumerate(allocated) if v],ratio

def daily_distribution(ages):
    daily=defaultdict(int); expanded=[(a,ages[a]) for a in range(90)]
    tail,ratio=expand_90_plus(ages); expanded+=tail
    for age,pop in expanded:
        start=date(REFERENCE_DATE.year-age-1,7,2); end=date(REFERENCE_DATE.year-age,7,1)
        days=(end-start).days+1
        for i,w in enumerate(apportion(pop,[1]*days)):
            if w: daily[start+timedelta(days=i)]+=w
    return daily,ratio

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest().upper()

def main(argv=None):
    p=argparse.ArgumentParser()
    p.add_argument("--ibge-ref",type=Path,required=True)
    p.add_argument("--out",type=Path,required=True); p.add_argument("--manifest",type=Path,required=True)
    a=p.parse_args(argv)
    ages,total=read_ref(a.ibge_ref); daily,ratio=daily_distribution(ages)
    if sum(daily.values())!=total: raise ValueError("IBGE REF: distribuicao diaria nao conserva o total")
    rows=[{"date":k.isoformat(),"births":v} for k,v in sorted(daily.items()) if v>0]
    obj={"schema_version":SCHEMA,"source":"IBGE_PROJECAO_POPULACAO_REVISAO_2024_REF","reference_period":"2026-07-01","geography":"UF_SP","rows":rows}
    a.out.parent.mkdir(parents=True,exist_ok=True); a.out.write_text(json.dumps(obj,ensure_ascii=False,separators=(",",":"))+"\n",encoding="utf-8")
    manifest={"schemaVersion":2,"referenceCode":"SYNTH_BIRTH_SP_PROJECTION2024_2026_E2_V1","output":{"path":a.out.name,"schemaVersion":SCHEMA,"sha256":sha(a.out),"rowCount":len(rows)},"sources":[{"kind":"IBGE_PROJECAO_POPULACAO_REVISAO_2024","officialFileName":SOURCE_XLSX,"officialFileSha256":SOURCE_XLSX_SHA256,"refPath":str(a.ibge_ref),"refSha256":sha(a.ibge_ref),"geography":"UF_SP","sourceSexLabel":"Ambos","semanticSex":"Total","referenceDate":"2026-07-01","populationWeight":total}],"model":{"ageWindow":"idade k em 01/07/2026 => 02/07/(2026-k-1)..01/07/(2026-k)","withinWindow":"UNIFORM_DAY_LARGEST_REMAINDER","open90Plus":{"sourceAgeLabel":"90","semantic":"90+","method":"GEOMETRIC_DECAY_FROM_AGE_89_OVER_88","ratio":ratio,"externalDemographicParameter":False,"maxSyntheticAge":MAX_SYNTHETIC_AGE,"maxSyntheticAgeSemantics":"VERSIONED_PLAUSIBILITY_GUARD_NOT_IBGE_OBSERVATION"}},"runtimeNetworkDependency":False}
    a.manifest.parent.mkdir(parents=True,exist_ok=True); a.manifest.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    return 0
if __name__=="__main__": raise SystemExit(main())
