#!/usr/bin/env python3
from __future__ import annotations
import argparse, json, subprocess, tempfile
from pathlib import Path

METHODS={'get','post','put','patch','delete','options','head'}


def fail(msg): raise SystemExit(f'CONTRACT BACKWARD COMPATIBILITY GATE: FAIL: {msg}')
def run(*a): return subprocess.check_output(a,text=True)
def info(path:Path):
    d={}
    for l in path.read_text(encoding='utf-8').splitlines():
        if '=' in l and not l.lstrip().startswith('#'):
            k,v=l.split('=',1); d[k.strip()]=v.strip()
    return d

def git_json(repo:Path, ref:str, rel:str):
    try: return json.loads(run('git','-C',str(repo),'show',f'{ref}:{rel}'))
    except subprocess.CalledProcessError: fail(f'artefato predecessor ausente: {ref}:{rel}')


def resolve_pointer(document, ref):
    if not isinstance(ref,str) or not ref.startswith('#/'): return None
    cur=document
    for raw in ref[2:].split('/'):
        token=raw.replace('~1','/').replace('~0','~')
        if isinstance(cur,dict) and token in cur: cur=cur[token]
        elif isinstance(cur,list) and token.isdigit() and int(token)<len(cur): cur=cur[int(token)]
        else: return None
    return cur

def deref(document,node,seen=None):
    if not isinstance(node,dict) or '$ref' not in node: return node
    ref=node.get('$ref'); seen=set() if seen is None else set(seen)
    if ref in seen: return node
    target=resolve_pointer(document,ref)
    if target is None: return node
    seen.add(ref)
    merged=dict(target)
    for k,v in node.items():
        if k!='$ref': merged[k]=v
    return deref(document,merged,seen)

def types(s):
    t=s.get('type') if isinstance(s,dict) else None
    if t is None: return None
    return set(t if isinstance(t,list) else [t])

def nullable(s:dict) -> bool:
    t=types(s)
    return bool(s.get('nullable')) or (t is not None and 'null' in t)

def numeric(v): return isinstance(v,(int,float)) and not isinstance(v,bool)

def _changed(old,new,key):
    return key in old and key in new and old[key] != new[key]

def compatible_acceptance(old,new,path,errs):
    """Tudo que o request antigo aceitava deve continuar aceito pelo novo contrato."""
    if not isinstance(old,dict) or not isinstance(new,dict): return
    ot,nt=types(old),types(new)
    if ot is not None and nt is not None and not ot.issubset(nt): errs.append(f'{path}: type restringido {sorted(ot)} -> {sorted(nt)}')
    if nullable(old) and not nullable(new): errs.append(f'{path}: nullable true -> false')
    oe,ne=old.get('enum'),new.get('enum')
    if isinstance(oe,list) and isinstance(ne,list) and not set(map(repr,oe)).issubset(set(map(repr,ne))): errs.append(f'{path}: enum foi estreitado')
    if old.get('format') and new.get('format') != old.get('format'): errs.append(f'{path}: format alterado/removido ({old.get("format")}->{new.get("format")})')
    for key in ('minLength','minimum','minItems','minProperties'):
        if numeric(old.get(key)) and numeric(new.get(key)) and new[key]>old[key]: errs.append(f'{path}: {key} mais restritivo ({old[key]}->{new[key]})')
    for key in ('maxLength','maximum','maxItems','maxProperties'):
        if numeric(old.get(key)) and numeric(new.get(key)) and new[key]<old[key]: errs.append(f'{path}: {key} mais restritivo ({old[key]}->{new[key]})')
    # Exclusive bounds in Draft 2020-12/OpenAPI 3.1 numeric form; boolean form is compared conservadoramente.
    if 'exclusiveMinimum' in old or 'exclusiveMinimum' in new:
        ov,nv=old.get('exclusiveMinimum'),new.get('exclusiveMinimum')
        if numeric(ov) and numeric(nv) and nv>ov: errs.append(f'{path}: exclusiveMinimum mais restritivo ({ov}->{nv})')
        elif type(ov) is bool and type(nv) is bool and not ov and nv: errs.append(f'{path}: exclusiveMinimum ativado')
        elif ov is None and nv not in (None,False): errs.append(f'{path}: exclusiveMinimum novo')
    if 'exclusiveMaximum' in old or 'exclusiveMaximum' in new:
        ov,nv=old.get('exclusiveMaximum'),new.get('exclusiveMaximum')
        if numeric(ov) and numeric(nv) and nv<ov: errs.append(f'{path}: exclusiveMaximum mais restritivo ({ov}->{nv})')
        elif type(ov) is bool and type(nv) is bool and not ov and nv: errs.append(f'{path}: exclusiveMaximum ativado')
        elif ov is None and nv not in (None,False): errs.append(f'{path}: exclusiveMaximum novo')
    if old.get('pattern') and new.get('pattern') != old.get('pattern'): errs.append(f'{path}: pattern alterado/removido')
    if 'multipleOf' in new and old.get('multipleOf') != new.get('multipleOf'): errs.append(f'{path}: multipleOf novo/alterado exige revisão')
    op,np=old.get('properties') or {},new.get('properties') or {}
    oldreq=set(old.get('required') or []); newreq=set(new.get('required') or [])
    added_req=newreq-oldreq
    if added_req: errs.append(f'{path}: propriedades passaram a obrigatórias: {sorted(added_req)}')
    for k,v in op.items():
        if k not in np:
            if new.get('additionalProperties',True) is False: errs.append(f'{path}.{k}: propriedade aceita antes foi removida')
        else: compatible_acceptance(v,np[k],f'{path}.{k}',errs)
    if old.get('additionalProperties',True) is not False and new.get('additionalProperties',True) is False:
        errs.append(f'{path}: additionalProperties passou de permitido para proibido')
    if isinstance(old.get('items'),dict) and isinstance(new.get('items'),dict): compatible_acceptance(old['items'],new['items'],path+'[]',errs)
    for key in ('oneOf','anyOf','allOf'):
        if key in old or key in new:
            if json.dumps(old.get(key),sort_keys=True)!=json.dumps(new.get(key),sort_keys=True): errs.append(f'{path}: {key} alterado; revisão explícita necessária')


def compatible_response(old,new,path,errs):
    """Tudo que o contrato antigo garantia na resposta deve continuar garantido."""
    if not isinstance(old,dict) or not isinstance(new,dict): return
    ot,nt=types(old),types(new)
    if ot is not None and nt is not None and ot!=nt: errs.append(f'{path}: tipo de resposta alterado')
    if not nullable(old) and nullable(new): errs.append(f'{path}: resposta passou a nullable')
    if old.get('format') != new.get('format') and (old.get('format') or new.get('format')): errs.append(f'{path}: format de resposta alterado')
    if old.get('enum') is not None and new.get('enum') is not None and old.get('enum')!=new.get('enum'): errs.append(f'{path}: enum de resposta alterado')
    op,np=old.get('properties') or {},new.get('properties') or {}
    for k,v in op.items():
        if k not in np: errs.append(f'{path}.{k}: propriedade de resposta removida')
        else: compatible_response(v,np[k],f'{path}.{k}',errs)
    oldreq=set(old.get('required') or []); newreq=set(new.get('required') or [])
    if not oldreq.issubset(newreq): errs.append(f'{path}: propriedade antes garantida deixou de ser obrigatória: {sorted(oldreq-newreq)}')
    if isinstance(old.get('items'),dict) and isinstance(new.get('items'),dict): compatible_response(old['items'],new['items'],path+'[]',errs)
    for key in ('oneOf','anyOf','allOf'):
        if key in old or key in new:
            if json.dumps(old.get(key),sort_keys=True)!=json.dumps(new.get(key),sort_keys=True): errs.append(f'{path}: composição de resposta {key} alterada')


def schema_for_media(media): return (media or {}).get('schema') or {}

def effective_security(document, operation):
    if 'security' in operation: return operation.get('security') or []
    return document.get('security') or []

def security_covers(old_req:dict,new_req:dict):
    # Um requisito novo é no máximo tão restritivo quanto o antigo se exige subconjunto de schemes/scopes.
    if not set(new_req).issubset(set(old_req)): return False
    for scheme,new_scopes in new_req.items():
        old_scopes=old_req.get(scheme) or []
        if not set(new_scopes or []).issubset(set(old_scopes)): return False
    return True

def compare_security(old_sec,new_sec,path,errs):
    old_sec=old_sec or []; new_sec=new_sec or []
    if not old_sec: # antes anônimo; qualquer requisito novo quebra clientes anônimos
        if new_sec: errs.append(f'{path}: segurança passou de anônima para autenticada')
        return
    if not new_sec: return # ficou menos restritivo
    for old_req in old_sec:
        if not any(security_covers(old_req,new_req) for new_req in new_sec):
            errs.append(f'{path}: requisito de segurança ficou mais restritivo para alternativa {old_req}')


def param_map(op, document):
    out={}
    for raw in op.get('parameters',[]):
        p=deref(document,raw) if isinstance(raw,dict) else raw
        if isinstance(p,dict) and p.get('in') and p.get('name'): out[(p.get('in'),p.get('name'))]=p
    return out

def compare_parameter(oldp,newp,path,errs):
    if oldp.get('required',False) is False and newp.get('required',False) is True:
        errs.append(f'{path}: parâmetro existente passou de opcional para obrigatório')
    if oldp.get('style') and newp.get('style') != oldp.get('style'): errs.append(f'{path}: style alterado')
    if 'explode' in oldp and newp.get('explode') != oldp.get('explode'): errs.append(f'{path}: explode alterado')
    if oldp.get('allowEmptyValue') and not newp.get('allowEmptyValue'): errs.append(f'{path}: allowEmptyValue true -> false')
    compatible_acceptance(oldp.get('schema') or {},newp.get('schema') or {},path+'.schema',errs)


def compare_headers(oldh,newh,path,errs):
    oldh=oldh or {}; newh=newh or {}
    for name,oh in oldh.items():
        nh=next((v for k,v in newh.items() if k.lower()==name.lower()),None)
        if nh is None: errs.append(f'{path}: header de resposta removido {name}')
        else: compatible_response((oh or {}).get('schema') or {},(nh or {}).get('schema') or {},f'{path}.header[{name}]',errs)


def compare_openapi(old,new,errs):
    for path,olditem in old.get('paths',{}).items():
        if path not in new.get('paths',{}): errs.append(f'OpenAPI: path removido {path}'); continue
        for method,oldop in olditem.items():
            if method.lower() not in METHODS: continue
            newop=new['paths'][path].get(method)
            if newop is None: errs.append(f'OpenAPI: método removido {method.upper()} {path}'); continue
            oldparams=param_map(oldop,old); newparams=param_map(newop,new)
            for key,oparam in oldparams.items():
                if key not in newparams: errs.append(f'OpenAPI: parâmetro removido {key} em {method.upper()} {path}')
                else: compare_parameter(oparam,newparams[key],f'OpenAPI param {method.upper()} {path} {key}',errs)
            for key,p in newparams.items():
                if p.get('required') and key not in oldparams: errs.append(f'OpenAPI: novo parâmetro obrigatório {key} em {method.upper()} {path}')
            compare_security(effective_security(old,oldop),effective_security(new,newop),f'OpenAPI security {method.upper()} {path}',errs)
            orb,nrb=oldop.get('requestBody'),newop.get('requestBody')
            if not orb and nrb and nrb.get('required'): errs.append(f'OpenAPI: novo requestBody obrigatório em {method.upper()} {path}')
            elif orb and not nrb: errs.append(f'OpenAPI: requestBody documentado foi removido em {method.upper()} {path}')
            elif orb and nrb:
                if not orb.get('required',False) and nrb.get('required',False): errs.append(f'OpenAPI: requestBody passou a obrigatório em {method.upper()} {path}')
                for c,om in (orb.get('content') or {}).items():
                    nm=(nrb.get('content') or {}).get(c)
                    if nm is None: errs.append(f'OpenAPI: content-type de request removido {c} em {method.upper()} {path}')
                    else: compatible_acceptance(deref(old,schema_for_media(om)),deref(new,schema_for_media(nm)),f'OpenAPI request {method.upper()} {path} {c}',errs)
            oldres=oldop.get('responses') or {}; newres=newop.get('responses') or {}
            for code,orv in oldres.items():
                if code not in newres: errs.append(f'OpenAPI: response {code} removida de {method.upper()} {path}'); continue
                nrv=newres[code]
                compare_headers(orv.get('headers'),nrv.get('headers'),f'OpenAPI response {code} {method.upper()} {path}',errs)
                for c,om in (orv.get('content') or {}).items():
                    nm=(nrv.get('content') or {}).get(c)
                    if nm is None: errs.append(f'OpenAPI: content-type de response removido {c} ({code}) {method.upper()} {path}')
                    else: compatible_response(deref(old,schema_for_media(om)),deref(new,schema_for_media(nm)),f'OpenAPI response {code} {method.upper()} {path} {c}',errs)


def selftest():
    errs=[]
    compare_parameter({'required':False,'schema':{'type':'string'}},{'required':True,'schema':{'type':'string'}},'p',errs)
    if not errs: fail('self-test não detectou parâmetro opcional -> obrigatório')
    errs=[]
    compare_security([], [{'ApiKey':[]}], 'security', errs)
    if not errs: fail('self-test não detectou autenticação nova')
    errs=[]
    compare_headers({'X-Old':{'schema':{'type':'string'}}},{},'resp',errs)
    if not errs: fail('self-test não detectou header removido')
    errs=[]
    compatible_acceptance({'type':'string','nullable':True},{'type':'string','nullable':False},'nullable',errs)
    if not errs: fail('self-test não detectou nullable mais restritivo')
    errs=[]
    compatible_acceptance({'type':'string','format':'uuid'},{'type':'string','format':'email'},'format',errs)
    if not errs: fail('self-test não detectou format alterado')
    errs=[]
    compatible_acceptance({'type':'number','exclusiveMinimum':0},{'type':'number','exclusiveMinimum':1},'exclusive',errs)
    if not errs: fail('self-test não detectou exclusiveMinimum mais restritivo')
    errs=[]
    compatible_acceptance({'type':'string','enum':['A']},{'type':'string','enum':['A','B']},'enum',errs)
    if errs: fail('self-test rejeitou enum ampliado de request')
    print('CONTRACT BACKWARD COMPATIBILITY GATE SELF-TEST: OK')


def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--repo',default='.'); ap.add_argument('--release-info',default='RELEASE_INFO.txt'); ap.add_argument('--summary'); ap.add_argument('--self-test',action='store_true'); a=ap.parse_args()
    if a.self_test: return selftest()
    repo=Path(a.repo).resolve(); ri=info(repo/a.release_info); pred=ri.get('source_git_predecessor_tag')
    if not pred: fail('source_git_predecessor_tag ausente')
    errs=[]
    old=git_json(repo,pred,'Solution/openapi/jornada-v1.openapi.json'); new=json.loads((repo/'Solution/openapi/jornada-v1.openapi.json').read_text(encoding='utf-8'))
    compare_openapi(old,new,errs)
    oldfiles=run('git','-C',str(repo),'ls-tree','-r','--name-only',pred,'Solution/config/contracts').splitlines()
    schemas=[x for x in oldfiles if x.endswith('.schema.json')]
    for rel in schemas:
        cur=repo/rel
        if not cur.is_file(): errs.append(f'JSON Schema removido: {rel}'); continue
        compatible_acceptance(git_json(repo,pred,rel),json.loads(cur.read_text(encoding='utf-8')),rel,errs)
    status='PASS' if not errs else 'FAIL'
    summary={'status':status,'predecessorTag':pred,'openApiPathsChecked':len(old.get('paths',{})),'jsonSchemasChecked':len(schemas),'breakingChanges':errs,
             'checks':['existing-parameter-requiredness','parameter-schema/style','security/scopes','response-headers','request/response-media','nullable/format/bounds','json-schema-acceptance']}
    if a.summary:
        Path(a.summary).parent.mkdir(parents=True,exist_ok=True); Path(a.summary).write_text(json.dumps(summary,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
    if errs: fail('; '.join(errs[:12]))
    print(f'CONTRACT BACKWARD COMPATIBILITY GATE: OK ({len(old.get("paths",{}))} paths; {len(schemas)} JSON schemas; predecessor={pred}; checks=extended)')
if __name__=='__main__': main()
