#!/usr/bin/env python3
"""Generate JORNADA_SYNTH_BIRTH_DAILY_V1 from the frozen IBGE 2024 population projection XLSX.

The tool is deliberately offline. It reads the official "populacao por sexo e idade
simples" workbook, selects UF=SP, sexo=Total and 01/07/2026, validates ages 0..89
plus 90+, and deterministically converts age stocks into birth-date weights.
"""
from __future__ import annotations
import argparse,hashlib,json,math,re,unicodedata,zipfile
from collections import defaultdict
from datetime import date,timedelta
from pathlib import Path
from xml.etree import ElementTree as ET

SCHEMA="JORNADA_SYNTH_BIRTH_DAILY_V1"
REFERENCE_DATE=date(2026,7,1)
SOURCE_FILE="projecoes_2024_tab1_idade_simples.xlsx"
NS="{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"

def norm(v):
    s=unicodedata.normalize("NFKD",str(v or "")).encode("ascii","ignore").decode().lower().strip()
    return re.sub(r"[^a-z0-9]+"," ",s).strip()

def _xlsx_rows(path:Path):
    if path.name != SOURCE_FILE:
        raise ValueError(f"IBGE: arquivo esperado {SOURCE_FILE}, recebido {path.name}")
    with zipfile.ZipFile(path) as z:
        shared=[]
        if "xl/sharedStrings.xml" in z.namelist():
            root=ET.fromstring(z.read("xl/sharedStrings.xml"))
            shared=["".join(t.text or "" for t in si.iter(NS+"t")) for si in root]
        sheets=sorted(n for n in z.namelist() if re.fullmatch(r"xl/worksheets/sheet\d+\.xml",n))
        if not sheets: raise ValueError("IBGE: XLSX sem planilha")
        for sheet in sheets:
            root=ET.fromstring(z.read(sheet))
            for row in root.iter(NS+"row"):
                vals=[]
                for c in row.findall(NS+"c"):
                    ref=c.get("r","A1"); col=0
                    for ch in re.match(r"[A-Z]+",ref).group(0): col=col*26+ord(ch)-64
                    while len(vals)<col: vals.append("")
                    typ=c.get("t"); v=c.find(NS+"v")
                    if typ=="inlineStr":
                        x="".join(t.text or "" for t in c.iter(NS+"t"))
                    elif v is None: x=""
                    elif typ=="s": x=shared[int(v.text)]
                    else: x=v.text or ""
                    vals[col-1]=x
                if any(str(x).strip() for x in vals): yield vals

def _integer(v):
    s=str(v).strip().replace(".","").replace(" ","")
    if s.endswith(",0"): s=s[:-2]
    return int(s)

def read_projection(path:Path, year=2026):
    rows=list(_xlsx_rows(path)); target=str(year)
    for hi,h in enumerate(rows):
        hh=[norm(x) for x in h]
        age_i=next((i for i,x in enumerate(hh) if x=="idade"),None)
        sex_i=next((i for i,x in enumerate(hh) if x=="sexo"),None)
        geo_i=next((i for i,x in enumerate(hh) if x in {"uf","unidade da federacao","local","localidade"}),None)
        year_i=next((i for i,x in enumerate(hh) if x=="ano"),None)
        pop_i=next((i for i,x in enumerate(hh) if x in {"populacao","populacao projetada","valor"}),None)
        wide_i=next((i for i,x in enumerate(hh) if x==target),None)
        if age_i is None or sex_i is None or geo_i is None or (wide_i is None and (year_i is None or pop_i is None)): continue
        ages={}; declared_total=None
        for r in rows[hi+1:]:
            def get(i): return r[i] if i is not None and i<len(r) else ""
            geo,sex,age=norm(get(geo_i)),norm(get(sex_i)),norm(get(age_i))
            if geo not in {"sp","sao paulo"} or sex not in {"total","ambos os sexos","ambos"}: continue
            if wide_i is None and norm(get(year_i))!=target: continue
            raw=get(wide_i if wide_i is not None else pop_i)
            if str(raw).strip() in {"","-","..."}: continue
            value=_integer(raw)
            if age=="total": declared_total=value; continue
            m=re.fullmatch(r"(\d+)(?: anos?)?",age)
            if m and int(m.group(1))<=89: ages[int(m.group(1))]=value; continue
            if age in {"90 ou mais","90 anos ou mais","90+"}: ages[90]=value
        missing=[a for a in range(90) if a not in ages]
        if missing: raise ValueError(f"IBGE: idades ausentes para SP/Total/{year}: {missing}")
        if 90 not in ages: raise ValueError(f"IBGE: categoria 90+ ausente para SP/Total/{year}")
        if declared_total is not None and sum(ages.values())!=declared_total:
            raise ValueError(f"IBGE: soma etaria {sum(ages.values())} difere do Total {declared_total}")
        return ages, declared_total or sum(ages.values())
    raise ValueError(f"IBGE: estrutura SP/Total/{year} nao encontrada no XLSX")

def apportion(total:int, weights):
    s=sum(weights)
    raw=[total*w/s for w in weights]; out=[math.floor(x) for x in raw]
    for i in sorted(range(len(raw)),key=lambda i:(-(raw[i]-out[i]),i))[:total-sum(out)]: out[i]+=1
    return out

def expand_90_plus(ages):
    if ages[88]<=0 or ages[89]<=0: raise ValueError("IBGE: idades 88/89 invalidas para derivar cauda 90+")
    ratio=ages[89]/ages[88]
    if not 0<ratio<1: raise ValueError(f"IBGE: razao de decaimento 89/88 deve estar entre 0 e 1; obtido {ratio}")
    n=max(1,math.ceil(math.log(0.5*(1-ratio)/ages[90],ratio)))
    weights=[ratio**i for i in range(n)]
    allocated=apportion(ages[90],weights)
    return [(90+i,v) for i,v in enumerate(allocated) if v],ratio

def daily_distribution(ages):
    d=defaultdict(int)
    expanded=[(a,ages[a]) for a in range(90)]
    tail,ratio=expand_90_plus(ages); expanded+=tail
    for age,pop in expanded:
        start=date(REFERENCE_DATE.year-age-1,7,2); end=date(REFERENCE_DATE.year-age,7,1)
        days=(end-start).days+1
        for i,w in enumerate(apportion(pop,[1]*days)):
            if w: d[start+timedelta(days=i)]+=w
    return d,ratio

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest().upper()

def main(argv=None):
    p=argparse.ArgumentParser()
    p.add_argument("--ibge-projection-xlsx",type=Path,required=True)
    p.add_argument("--out",type=Path,required=True); p.add_argument("--manifest",type=Path,required=True)
    a=p.parse_args(argv)
    ages,total=read_projection(a.ibge_projection_xlsx)
    daily,ratio=daily_distribution(ages)
    if sum(daily.values())!=total: raise ValueError("IBGE: distribuicao diaria nao conserva o total")
    rows=[{"date":k.isoformat(),"births":v} for k,v in sorted(daily.items()) if v>0]
    obj={"schema_version":SCHEMA,"source":"IBGE_PROJECAO_POPULACAO_REVISAO_2024","reference_period":"2026-07-01","geography":"UF_SP","rows":rows}
    a.out.parent.mkdir(parents=True,exist_ok=True)
    a.out.write_text(json.dumps(obj,ensure_ascii=False,separators=(",",":"))+"\n",encoding="utf-8")
    manifest={"schemaVersion":2,"referenceCode":"SYNTH_BIRTH_SP_PROJECTION2024_2026_E2_V1","output":{"path":a.out.name,"schemaVersion":SCHEMA,"sha256":sha(a.out),"rowCount":len(rows)},"sources":[{"kind":"IBGE_PROJECAO_POPULACAO_REVISAO_2024","path":str(a.ibge_projection_xlsx),"fileName":SOURCE_FILE,"sha256":sha(a.ibge_projection_xlsx),"geography":"UF_SP","sex":"Total","referenceDate":"2026-07-01","populationWeight":total}],"model":{"ageWindow":"idade k em 01/07/2026 => 02/07/(2026-k-1)..01/07/(2026-k)","withinWindow":"UNIFORM_DAY_LARGEST_REMAINDER","open90Plus":{"method":"GEOMETRIC_DECAY_FROM_AGE_89_OVER_88","ratio":ratio,"externalDemographicParameter":False}},"runtimeNetworkDependency":False}
    a.manifest.parent.mkdir(parents=True,exist_ok=True); a.manifest.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    return 0
if __name__=="__main__": raise SystemExit(main())
