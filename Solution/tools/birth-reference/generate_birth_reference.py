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
CENSUS_SP_2022_TOTAL=44411238
CENSUS_SP_2022_100_PLUS=5095

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

def read_tail_benchmark(path:Path):
    rows={}
    with path.open(encoding="utf-8-sig",newline="") as f:
        for row in csv.DictReader(f):
            try: metric=row["metric"]; value=int(row["value"])
            except (KeyError,ValueError) as e: raise ValueError("IBGE benchmark: linha invalida") from e
            if metric in rows: raise ValueError(f"IBGE benchmark: metrica duplicada: {metric}")
            rows[metric]=value
    expected={"population_total":CENSUS_SP_2022_TOTAL,"population_100_plus":CENSUS_SP_2022_100_PLUS}
    if rows!=expected: raise ValueError(f"IBGE benchmark: conteudo diverge do congelado: {rows}")
    return rows

def apportion(total:int,weights):
    s=sum(weights)
    if total<0 or not weights or s<=0: raise ValueError("rateio invalido")
    raw=[total*w/s for w in weights]; out=[math.floor(x) for x in raw]
    for i in sorted(range(len(raw)),key=lambda i:(-(raw[i]-out[i]),i))[:total-sum(out)]: out[i]+=1
    return out

def expand_90_plus(ages):
    # The projection publishes one open 90+ cell. Calibrate a smooth geometric
    # disaggregation so its 100+ share matches the observed SP Censo 2022
    # centenarian share, applied to the 2026 projected total. This uses Censo
    # only to shape the open cell; the Projection remains the population stock.
    target_100_plus=round(EXPECTED_TOTAL*CENSUS_SP_2022_100_PLUS/CENSUS_SP_2022_TOTAL)
    n=MAX_SYNTHETIC_AGE-90+1
    def allocate(r): return apportion(ages[90],[r**i for i in range(n)])
    lo,hi=0.01,0.999
    for _ in range(80):
        mid=(lo+hi)/2
        if sum(allocate(mid)[10:]) < target_100_plus: lo=mid
        else: hi=mid
    ratio=hi; allocated=allocate(ratio)
    if sum(allocated[10:])!=target_100_plus:
        raise ValueError("IBGE REF: calibracao 100+ nao converge ao benchmark congelado")
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
    p.add_argument("--tail-benchmark",type=Path,required=True)
    p.add_argument("--out",type=Path,required=True); p.add_argument("--manifest",type=Path,required=True)
    a=p.parse_args(argv)
    ages,total=read_ref(a.ibge_ref); read_tail_benchmark(a.tail_benchmark); daily,ratio=daily_distribution(ages)
    if sum(daily.values())!=total: raise ValueError("IBGE REF: distribuicao diaria nao conserva o total")
    rows=[{"date":k.isoformat(),"births":v} for k,v in sorted(daily.items()) if v>0]
    obj={"schema_version":SCHEMA,"source":"IBGE_PROJECAO_POPULACAO_REVISAO_2024_REF","reference_period":"2026-07-01","geography":"UF_SP","rows":rows}
    a.out.parent.mkdir(parents=True,exist_ok=True); a.out.write_text(json.dumps(obj,ensure_ascii=False,separators=(",",":"))+"\n",encoding="utf-8")
    manifest={"schemaVersion":2,"referenceCode":"SYNTH_BIRTH_SP_PROJECTION2024_2026_E2_V1","output":{"path":a.out.name,"schemaVersion":SCHEMA,"sha256":sha(a.out),"rowCount":len(rows)},"sources":[{"kind":"IBGE_PROJECAO_POPULACAO_REVISAO_2024","officialFileName":SOURCE_XLSX,"officialFileSha256":SOURCE_XLSX_SHA256,"refPath":a.ibge_ref.name,"refSha256":sha(a.ibge_ref),"geography":"UF_SP","sourceSexLabel":"Ambos","semanticSex":"Total","referenceDate":"2026-07-01","populationWeight":total},{"kind":"IBGE_CENSO_2022_SIDRA_9514_TAIL_BENCHMARK","refPath":a.tail_benchmark.name,"refSha256":sha(a.tail_benchmark),"geography":"UF_SP","sex":"Total","age":"100+","declaration":"Total","populationTotal":CENSUS_SP_2022_TOTAL,"population100Plus":CENSUS_SP_2022_100_PLUS,"role":"AUXILIARY_SHAPE_CALIBRATION_ONLY"}],"model":{"ageWindow":"idade k em 01/07/2026 => 02/07/(2026-k-1)..01/07/(2026-k)","withinWindow":"UNIFORM_DAY_LARGEST_REMAINDER","open90Plus":{"sourceAgeLabel":"90","semantic":"90+","method":"GEOMETRIC_DECAY_CALIBRATED_TO_SP_CENSO2022_100_PLUS","ratio":ratio,"ratioParameterSource":"CALIBRATED_TO_FROZEN_CENSO2022_SP_100_PLUS_SHARE","census2022SpPopulation":CENSUS_SP_2022_TOTAL,"census2022Sp100Plus":CENSUS_SP_2022_100_PLUS,"target2026Sp100Plus":round(total*CENSUS_SP_2022_100_PLUS/CENSUS_SP_2022_TOTAL),"maxSyntheticAge":MAX_SYNTHETIC_AGE,"maxSyntheticAgeParameterSource":"DC_SYN_01_E2_VERSIONED_PLAUSIBILITY_GUARD","maxSyntheticAgeSemantics":"NOT_IBGE_OBSERVATION_NOT_STRUCTURAL_LIMIT"}},"runtimeNetworkDependency":False}
    a.manifest.parent.mkdir(parents=True,exist_ok=True); a.manifest.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    return 0
if __name__=="__main__": raise SystemExit(main())
