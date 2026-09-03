#!/usr/bin/env python3
from __future__ import annotations
import argparse, datetime, hashlib, json, re, sys, tempfile
from pathlib import Path
OPS={"PRESENTE","AUSENTE","IGUAL","DIFERENTE","EM","NUMERO_MAIOR_IGUAL","NUMERO_MENOR_IGUAL","DATA_MAIOR_IGUAL","DATA_MENOR_IGUAL"}

def load(path: Path):
    obj=json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(obj,dict): raise ValueError("raiz deve ser objeto")
    return obj

def validate(obj: dict, require_approved: bool=False, base: Path|None=None):
    errors=[]
    if obj.get("schemaVersion")!=1: errors.append("schemaVersion deve ser 1")
    status=obj.get("status")
    if status not in {"PENDENTE","APROVADO"}: errors.append("status inválido")
    if not isinstance(obj.get("catalogVersion"),str) or not obj.get("catalogVersion","").strip(): errors.append("catalogVersion obrigatório")
    if require_approved and status!="APROVADO": errors.append("catálogo de Possibilidades ainda não está APROVADO")
    rules=obj.get("rules")
    if not isinstance(rules,list): errors.append("rules deve ser array"); rules=[]
    seen=set()
    for i,r in enumerate(rules):
        if not isinstance(r,dict): errors.append(f"rules[{i}] deve ser objeto"); continue
        key=(r.get("natureza"),r.get("codigo"),r.get("versao"))
        if key[0] not in {"BENEFICIO","SERVICO"}: errors.append(f"rules[{i}].natureza inválida")
        if not isinstance(key[1],str) or not re.fullmatch(r"[A-Z0-9]{4}",key[1]): errors.append(f"rules[{i}].codigo deve ter 4 caracteres A-Z/0-9")
        if not isinstance(key[2],int) or isinstance(key[2],bool) or key[2]<=0: errors.append(f"rules[{i}].versao inválida")
        if key in seen: errors.append(f"rules[{i}] duplicada: {key}")
        seen.add(key)
        impl=r.get("implementacaoVersao")
        if not isinstance(impl,str) or not impl.strip() or len(impl)>80: errors.append(f"rules[{i}].implementacaoVersao inválida")
        groups=[]
        for g in ("allOf","anyOf"):
            cs=r.get(g,[])
            if not isinstance(cs,list): errors.append(f"rules[{i}].{g} deve ser array"); cs=[]
            groups+=cs
        if not groups: errors.append(f"rules[{i}] sem condições")
        for j,c in enumerate(groups):
            if not isinstance(c,dict): errors.append(f"rules[{i}] condição {j} inválida"); continue
            op=c.get("operator")
            if not c.get("fact"): errors.append(f"rules[{i}] condição {j} sem fact")
            if op not in OPS: errors.append(f"rules[{i}] condição {j} operador inválido")
            if op=="EM" and not isinstance(c.get("expectedAny"),list): errors.append(f"rules[{i}] EM exige expectedAny")
            if op not in {"PRESENTE","AUSENTE","EM"} and "expected" not in c: errors.append(f"rules[{i}] {op} exige expected")
    approval=obj.get("approval")
    if status=="PENDENTE":
        if rules: errors.append("status=PENDENTE não pode distribuir regras como vigentes")
        if approval not in (None,{}): errors.append("status=PENDENTE não pode ter approval")
    if status=="APROVADO":
        if not rules: errors.append("status=APROVADO exige ao menos uma regra")
        if not isinstance(approval,dict): errors.append("status=APROVADO exige approval"); approval={}
        for k in ("approvedAtUtc","approvedBy","evidencePath","evidenceSha256"):
            if not approval.get(k): errors.append(f"approval.{k} ausente")
        if approval.get("approvedAtUtc"):
            try: datetime.datetime.fromisoformat(str(approval["approvedAtUtc"]).replace("Z","+00:00"))
            except ValueError: errors.append("approval.approvedAtUtc inválido")
        ep_raw=str(approval.get("evidencePath") or "")
        if ep_raw.startswith("/") or "\\" in ep_raw or ":" in ep_raw or ".." in ep_raw.split("/"): errors.append("approval.evidencePath deve ser relativo portável sem traversal")
        sha=approval.get("evidenceSha256")
        if sha and not re.fullmatch(r"[0-9a-f]{64}",str(sha)): errors.append("approval.evidenceSha256 inválido")
        if base and approval.get("evidencePath"):
            ep=(base/approval["evidencePath"]).resolve()
            try: ep.relative_to(base.resolve())
            except ValueError: errors.append("evidencePath escapa da raiz"); ep=None
            if ep and not ep.is_file(): errors.append("evidencePath ausente")
            elif ep and sha and hashlib.sha256(ep.read_bytes()).hexdigest()!=sha: errors.append("evidenceSha256 não confere")
    return errors

def selftest():
    ok={"schemaVersion":1,"status":"PENDENTE","catalogVersion":"TEST","rules":[],"approval":None}
    assert not validate(ok)
    bad={"schemaVersion":1,"status":"APROVADO","catalogVersion":"TEST","rules":[],"approval":{}}
    assert validate(bad,True)
    good={"schemaVersion":1,"status":"APROVADO","catalogVersion":"TEST","rules":[{"natureza":"SERVICO","codigo":"TEST","versao":1,"implementacaoVersao":"TEST.v1","allOf":[{"fact":"A","operator":"PRESENTE"}]}],"approval":{"approvedAtUtc":"2026-09-01T00:00:00Z","approvedBy":"TEST","evidencePath":"e.json","evidenceSha256":"0"*64}}
    assert not [e for e in validate(good,True) if not e.startswith("evidence")]
    print("POSSIBILITY RULES GATE SELFTEST: OK")

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument("catalog", nargs="?")
    ap.add_argument("--require-approved",action="store_true")
    ap.add_argument("--root",default=".")
    ap.add_argument("--summary")
    ap.add_argument("--self-test",action="store_true")
    a=ap.parse_args()
    if a.self_test: selftest(); return 0
    if not a.catalog: print("ERRO: catalog obrigatório",file=sys.stderr); return 2
    path=Path(a.catalog).resolve(); obj=load(path); errors=validate(obj,a.require_approved,Path(a.root).resolve())
    summary={"schemaVersion":1,"status":"FAIL" if errors else "PASS","catalog":str(path),"catalogStatus":obj.get("status"),"ruleCount":len(obj.get("rules") or []),"errors":errors}
    if a.summary:
        out=Path(a.summary); out.parent.mkdir(parents=True,exist_ok=True); out.write_text(json.dumps(summary,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    if errors:
        for e in errors: print("ERRO:",e,file=sys.stderr)
        return 2
    print(f"POSSIBILITY RULES GATE: OK (status={obj.get('status')}; rules={len(obj.get('rules') or [])})")
    return 0
if __name__=="__main__": raise SystemExit(main())
