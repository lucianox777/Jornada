#!/usr/bin/env python3
"""Generate the frozen JORNADA_SYNTH_BIRTH_DAILY_V1 reference (DC-SYN-01-E1).

Inputs are deliberately local snapshots: a SIDRA 9514 CSV exported for municipality
3550308 (sex=Total, age declaration=Total) and zero or more SINASC CSV snapshots.
No network access is performed by this tool.
"""
from __future__ import annotations
import argparse,csv,hashlib,json,re,sys
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
    rows=[]
    with path.open(encoding='utf-8-sig',newline='') as f:
        r=csv.DictReader(f)
        for x in r:
            age=(x.get('Idade') or x.get('idade') or '').strip()
            val=x.get('Valor') or x.get('valor') or x.get('População residente') or x.get('Populacao residente')
            if not age or val in (None,'','-','...'): continue
            if age.lower()=='total' or 'mes' in age.lower(): continue
            m=AGE_RE.match(age)
            if m:
                rows.append((0 if age.lower().startswith('menos') else int(m.group('age')),None,_int(val),age)); continue
            m=RANGE_RE.match(age)
            if m:
                rows.append((int(m.group('a')),int(m.group('b')),_int(val),age)); continue
            if age.lower()=='100 anos ou mais': rows.append((100,122,_int(val),age)); continue
    # Prefer simple ages; ranges are fallback only.
    simple={a:(v,label) for a,b,v,label in rows if b is None}
    out=[]
    covered=set(simple)
    for a,(v,label) in sorted(simple.items()): out.append((a,a,v,label))
    for a,b,v,label in rows:
        if b is None or all(i in covered for i in range(a,b+1)): continue
        out.append((a,b,v,label)); covered.update(range(a,b+1))
    return sorted(out)

def apportion(total:int,n:int):
    q,r=divmod(total,n)
    return [q+(i<r) for i in range(n)]

def census_daily(groups):
    d=defaultdict(int); total=0
    for a,b,pop,label in groups:
        ages=list(range(a,b+1)); age_weights=apportion(pop,len(ages))
        for age,w in zip(ages,age_weights):
            start=date(2021-age,8,1); end=date(2022-age,7,31); days=(end-start).days+1
            for i,x in enumerate(apportion(w,days)):
                if x: d[start+timedelta(days=i)]+=x
        total+=pop
    return d,total

def read_sinasc(paths):
    d=defaultdict(int)
    for path in paths:
        with path.open(encoding='utf-8-sig',newline='') as f:
            r=csv.DictReader(f)
            fields={x.lower():x for x in (r.fieldnames or [])}
            key=next((fields[k] for k in ('dtnasc','data_nascimento','data nascimento') if k in fields),None)
            if not key: raise ValueError(f'{path}: coluna de data de nascimento não encontrada')
            for x in r:
                s=x[key].strip()
                parsed=None
                for fmt in ('%d%m%Y','%d/%m/%Y','%Y-%m-%d'):
                    try:
                        from datetime import datetime
                        parsed=datetime.strptime(s,fmt).date(); break
                    except ValueError: pass
                if parsed and parsed>=CENSUS_DATE: d[parsed]+=1
    return d

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest().upper()

def main(argv=None):
    p=argparse.ArgumentParser()
    p.add_argument('--sidra-9514',type=Path,required=True); p.add_argument('--sinasc',type=Path,nargs='*',default=[])
    p.add_argument('--out',type=Path,required=True); p.add_argument('--manifest',type=Path,required=True)
    a=p.parse_args(argv)
    groups=read_sidra(a.sidra_9514)
    if not groups or groups[0][0]!=0 or not any(g[0]==100 for g in groups): raise SystemExit('SIDRA sem cobertura 0 e 100+ esperada')
    daily,census_total=census_daily(groups); post=read_sinasc(a.sinasc)
    for k,v in post.items(): daily[k]=v
    rows=[{'date':k.isoformat(),'births':v} for k,v in sorted(daily.items()) if v>0]
    obj={'schema_version':SCHEMA,'source':'IBGE_CENSO_2022_SIDRA_9514_PLUS_SINASC_SP','reference_period':'CENSO_2022_PLUS_SINASC_SNAPSHOT','geography':'MUNICIPIO_SAO_PAULO_3550308','rows':rows}
    a.out.parent.mkdir(parents=True,exist_ok=True); a.out.write_text(json.dumps(obj,ensure_ascii=False,separators=(',',':'))+'\n',encoding='utf-8')
    manifest={'schemaVersion':1,'referenceCode':'SYNTH_BIRTH_SP_CENSO2022_SINASC_V1','output':{'path':a.out.name,'schemaVersion':SCHEMA,'sha256':sha(a.out),'rowCount':len(rows)},'sources':[{'kind':'IBGE_SIDRA_9514','path':str(a.sidra_9514),'sha256':sha(a.sidra_9514),'municipality':'3550308','referenceDate':'2022-08-01','populationWeight':census_total}]+[{'kind':'SINASC_SP','path':str(x),'sha256':sha(x)} for x in a.sinasc],'model':{'censusAgeWindow':'idade k em 31/07/2022 => 01/08/(2021-k)..31/07/(2022-k)','withinWindow':'UNIFORM_DAY_LARGEST_REMAINDER','groupedAge':'UNIFORM_AGE_LARGEST_REMAINDER','centenarianTail':'100+ represented uniformly over ages 100..122; upper bound is a declared modelling cap, not an IBGE age distribution','postCensus':'observed SINASC date replaces census-derived value on/after 2022-08-01'},'runtimeNetworkDependency':False}
    a.manifest.parent.mkdir(parents=True,exist_ok=True); a.manifest.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    return 0
if __name__=='__main__': raise SystemExit(main())
