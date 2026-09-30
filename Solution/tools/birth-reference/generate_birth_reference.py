#!/usr/bin/env python3
"""Generate JORNADA_SYNTH_BIRTH_DAILY_V1 from a frozen SIDRA 9514 snapshot.

DC-SYN-01-E1 deliberately uses one demographic source: Censo 2022 / SIDRA
table 9514 for municipality 3550308, sex=Total, age declaration=Total.
Post-Census dates are a declared extrapolation of the age-zero daily rate.
No network access is performed.
"""
from __future__ import annotations
import argparse,csv,hashlib,json,re
from collections import defaultdict
from datetime import date,timedelta
from pathlib import Path

SCHEMA="JORNADA_SYNTH_BIRTH_DAILY_V1"
CENSUS_DATE=date(2022,8,1)
AGE_RE=re.compile(r"^(?:Menos de 1 ano|(?P<age>\d+) anos?)$",re.I)
RANGE_RE=re.compile(r"^(?P<a>\d+) a (?P<b>\d+) anos$",re.I)

def _int(v:str)->int:
    return int(v.strip().replace('.','').replace(' ',''))

def read_sidra(path:Path):
    rows=[]; declared_total=None
    with path.open(encoding='utf-8-sig',newline='') as f:
        for x in csv.DictReader(f):
            age=(x.get('Idade') or x.get('idade') or '').strip()
            val=x.get('Valor') or x.get('valor') or x.get('População residente') or x.get('Populacao residente')
            if not age or val in (None,'','-','...'): continue
            if age.lower()=='total': declared_total=_int(val); continue
            if 'mes' in age.lower(): continue
            m=AGE_RE.match(age)
            if m:
                rows.append((0 if age.lower().startswith('menos') else int(m.group('age')),None,_int(val),age)); continue
            m=RANGE_RE.match(age)
            if m:
                rows.append((int(m.group('a')),int(m.group('b')),_int(val),age)); continue
            if age.lower()=='100 anos ou mais':
                rows.append((100,105,_int(val),age)); continue
    simple={a:(v,label) for a,b,v,label in rows if b is None}
    out=[]; covered=set(simple)
    for a,(v,label) in sorted(simple.items()): out.append((a,a,v,label))
    for a,b,v,label in rows:
        if b is None or all(i in covered for i in range(a,b+1)): continue
        out.append((a,b,v,label)); covered.update(range(a,b+1))
    out=sorted(out); covered=set()
    for a,b,_,_ in out:
        for age in range(a,b+1):
            if age in covered: raise ValueError(f'SIDRA: idade {age} coberta mais de uma vez')
            covered.add(age)
    missing=[age for age in range(100) if age not in covered]
    if missing: raise ValueError(f'SIDRA: idades ausentes: {missing}')
    if 100 not in covered: raise ValueError('SIDRA: categoria 100 anos ou mais ausente')
    computed=sum(v for _,_,v,_ in out)
    if declared_total is None: raise ValueError('SIDRA: linha Total ausente')
    if computed != declared_total: raise ValueError(f'SIDRA: soma etária {computed} difere do Total {declared_total}')
    return out

def apportion(total:int,n:int):
    q,r=divmod(total,n)
    return [q+(i<r) for i in range(n)]

def census_daily(groups):
    d=defaultdict(int); total=0; age_zero_weight=None
    for a,b,pop,label in groups:
        ages=list(range(a,b+1)); age_weights=apportion(pop,len(ages))
        for age,w in zip(ages,age_weights):
            if age==0: age_zero_weight=w
            start=date(2021-age,8,1); end=date(2022-age,7,31); days=(end-start).days+1
            for i,x in enumerate(apportion(w,days)):
                if x: d[start+timedelta(days=i)]+=x
        total+=pop
    if age_zero_weight is None: raise ValueError('SIDRA: peso da idade zero ausente')
    return d,total,age_zero_weight

def extend_post_census(daily,age_zero_weight:int,cutoff:date, today:date|None=None):
    if cutoff < CENSUS_DATE: raise ValueError('cutoff não pode preceder 2022-08-01')
    current_date=today or date.today()
    if cutoff > current_date: raise ValueError(f'cutoff não pode estar no futuro: {cutoff.isoformat()} > {current_date.isoformat()}')
    # Declared approximation: repeat the age-zero average daily rate. Integer
    # largest-remainder allocation is deterministic and conserves the implied
    # total over the extrapolated interval.
    days=(cutoff-CENSUS_DATE).days+1
    annual_days=365
    implied_total=round(age_zero_weight*days/annual_days)
    for i,w in enumerate(apportion(implied_total,days)):
        if w: daily[CENSUS_DATE+timedelta(days=i)]=w

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest().upper()

def main(argv=None):
    p=argparse.ArgumentParser()
    p.add_argument('--sidra-9514',type=Path,required=True)
    p.add_argument('--post-census-cutoff',type=date.fromisoformat,required=True)
    p.add_argument('--out',type=Path,required=True); p.add_argument('--manifest',type=Path,required=True)
    a=p.parse_args(argv)
    groups=read_sidra(a.sidra_9514)
    daily,census_total,age_zero_weight=census_daily(groups)
    extend_post_census(daily,age_zero_weight,a.post_census_cutoff)
    rows=[{'date':k.isoformat(),'births':v} for k,v in sorted(daily.items()) if v>0]
    obj={'schema_version':SCHEMA,'source':'IBGE_CENSO_2022_SIDRA_9514_DECLARED_EXTRAPOLATION','reference_period':f'CENSO_2022_PLUS_EXTRAPOLATION_TO_{a.post_census_cutoff.isoformat()}','geography':'MUNICIPIO_SAO_PAULO_3550308','rows':rows}
    a.out.parent.mkdir(parents=True,exist_ok=True)
    a.out.write_text(json.dumps(obj,ensure_ascii=False,separators=(',',':'))+'\n',encoding='utf-8')
    manifest={'schemaVersion':1,'referenceCode':'SYNTH_BIRTH_SP_CENSO2022_E1_V1','output':{'path':a.out.name,'schemaVersion':SCHEMA,'sha256':sha(a.out),'rowCount':len(rows)},'sources':[{'kind':'IBGE_SIDRA_9514','path':str(a.sidra_9514),'sha256':sha(a.sidra_9514),'municipality':'3550308','sex':'Total','ageDeclaration':'Total','referenceDate':'2022-08-01','populationWeight':census_total}],'model':{'censusAgeWindow':'idade k em 31/07/2022 => 01/08/(2021-k)..31/07/(2022-k)','withinWindow':'UNIFORM_DAY_LARGEST_REMAINDER','groupedAge':'UNIFORM_AGE_LARGEST_REMAINDER','centenarianTail':'100+ represented uniformly over ages 100..105; declared technical convention, not an IBGE age distribution','postCensus':{'method':'AGE_ZERO_DAILY_RATE_EXTRAPOLATION','start':'2022-08-01','cutoff':a.post_census_cutoff.isoformat(),'ageZeroPopulation':age_zero_weight,'allocation':'UNIFORM_DAY_LARGEST_REMAINDER'}},'runtimeNetworkDependency':False}
    a.manifest.parent.mkdir(parents=True,exist_ok=True)
    a.manifest.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    return 0
if __name__=='__main__': raise SystemExit(main())
