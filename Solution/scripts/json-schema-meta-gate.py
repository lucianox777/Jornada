#!/usr/bin/env python3
from __future__ import annotations
import argparse, json, re, tempfile
from pathlib import Path
from urllib.parse import urlparse

DRAFT = 'https://json-schema.org/draft/2020-12/schema'
SCHEMA_KEYWORDS = {
    '$schema','$id','$ref','$anchor','$defs','$comment',
    'type','enum','const','multipleOf','maximum','exclusiveMaximum','minimum','exclusiveMinimum',
    'maxLength','minLength','pattern','maxItems','minItems','uniqueItems','maxContains','minContains',
    'maxProperties','minProperties','required','dependentRequired','properties','patternProperties',
    'additionalProperties','propertyNames','unevaluatedProperties','items','prefixItems','contains',
    'allOf','anyOf','oneOf','not','if','then','else','dependentSchemas','unevaluatedItems',
    'title','description','default','deprecated','readOnly','writeOnly','examples','format','contentEncoding',
    'contentMediaType','contentSchema'
}
TYPE_NAMES={'null','boolean','object','array','number','string','integer'}
FORMATS={'date-time','date','time','duration','email','hostname','ipv4','ipv6','uuid','uri','uri-reference','iri','iri-reference','regex'}


def fail(msg: str):
    raise SystemExit(f'JSON SCHEMA META GATE: FAIL: {msg}')


def walk_schema(node, path: str, errors: list[str], refs: list[tuple[str,str]], known_ids: set[str]):
    if isinstance(node, bool):
        return
    if not isinstance(node, dict):
        errors.append(f'{path}: schema deve ser objeto ou boolean')
        return

    for key, value in node.items():
        # Em properties/$defs/patternProperties/dependentSchemas as chaves filhas são nomes, não keywords.
        if key not in SCHEMA_KEYWORDS:
            errors.append(f'{path}: keyword desconhecida {key!r}')

        if key == '$ref':
            if not isinstance(value, str) or not value.strip(): errors.append(f'{path}.$ref: referência inválida')
            else: refs.append((path, value))
        elif key == '$id':
            if not isinstance(value, str) or not value.strip(): errors.append(f'{path}.$id: valor inválido')
            elif not urlparse(value).scheme: errors.append(f'{path}.$id: deve ser URI absoluta')
        elif key == 'type':
            vals = value if isinstance(value, list) else [value]
            if not vals or any(v not in TYPE_NAMES for v in vals): errors.append(f'{path}.type: tipo inválido {value!r}')
            if len(vals) != len(set(vals)): errors.append(f'{path}.type: tipos duplicados')
        elif key == 'required':
            if not isinstance(value, list) or any(not isinstance(v,str) or not v for v in value): errors.append(f'{path}.required: lista de strings esperada')
            elif len(value) != len(set(value)): errors.append(f'{path}.required: nomes duplicados')
        elif key in {'minLength','maxLength','minItems','maxItems','minProperties','maxProperties','minContains','maxContains'}:
            if not isinstance(value, int) or isinstance(value,bool) or value < 0: errors.append(f'{path}.{key}: inteiro não-negativo esperado')
        elif key in {'minimum','maximum','exclusiveMinimum','exclusiveMaximum','multipleOf'}:
            if not isinstance(value,(int,float)) or isinstance(value,bool): errors.append(f'{path}.{key}: número esperado')
            elif key == 'multipleOf' and value <= 0: errors.append(f'{path}.multipleOf: deve ser > 0')
        elif key == 'pattern':
            if not isinstance(value,str): errors.append(f'{path}.pattern: string esperada')
            else:
                try: re.compile(value)
                except re.error as ex: errors.append(f'{path}.pattern: regex inválida ({ex})')
        elif key == 'format':
            if not isinstance(value,str) or not value: errors.append(f'{path}.format: string esperada')
        elif key in {'additionalProperties','unevaluatedProperties','items','contains','propertyNames','not','if','then','else','contentSchema','unevaluatedItems'}:
            walk_schema(value, f'{path}.{key}', errors, refs, known_ids)
        elif key in {'allOf','anyOf','oneOf','prefixItems'}:
            if not isinstance(value,list) or not value: errors.append(f'{path}.{key}: array não-vazio esperado')
            else:
                for i, child in enumerate(value): walk_schema(child, f'{path}.{key}[{i}]', errors, refs, known_ids)
        elif key in {'properties','patternProperties','$defs','dependentSchemas'}:
            if not isinstance(value,dict): errors.append(f'{path}.{key}: objeto esperado')
            else:
                for name, child in value.items():
                    if not isinstance(name,str) or not name: errors.append(f'{path}.{key}: nome vazio')
                    walk_schema(child, f'{path}.{key}[{name!r}]', errors, refs, known_ids)
        elif key == 'dependentRequired':
            if not isinstance(value,dict): errors.append(f'{path}.dependentRequired: objeto esperado')
            else:
                for name, deps in value.items():
                    if not isinstance(deps,list) or any(not isinstance(x,str) for x in deps): errors.append(f'{path}.dependentRequired[{name!r}]: lista de strings esperada')


def resolve_pointer(doc, pointer: str):
    if pointer in ('', '#'): return doc
    if not pointer.startswith('#/'): return None
    cur = doc
    for raw in pointer[2:].split('/'):
        token=raw.replace('~1','/').replace('~0','~')
        if isinstance(cur,dict) and token in cur: cur=cur[token]
        elif isinstance(cur,list) and token.isdigit() and int(token) < len(cur): cur=cur[int(token)]
        else: return None
    return cur


def validate_catalog(root: Path):
    files=sorted(root.rglob('*.schema.json'))
    if not files: fail(f'nenhum *.schema.json em {root}')
    docs={}
    ids={}
    errors=[]
    all_refs=[]
    for f in files:
        try: doc=json.loads(f.read_text(encoding='utf-8'))
        except Exception as ex:
            errors.append(f'{f}: JSON inválido ({ex})'); continue
        docs[f]=doc
        if doc.get('$schema') != DRAFT: errors.append(f'{f}: $schema deve ser {DRAFT}')
        sid=doc.get('$id')
        if not isinstance(sid,str) or not sid: errors.append(f'{f}: $id obrigatório')
        elif sid in ids: errors.append(f'$id duplicado: {sid} em {ids[sid]} e {f}')
        else: ids[sid]=f
        refs=[]
        walk_schema(doc, str(f), errors, refs, set(ids))
        all_refs.extend((f,p,r) for p,r in refs)

    for f,path,ref in all_refs:
        if ref.startswith('#'):
            if resolve_pointer(docs[f], ref) is None: errors.append(f'{path}: $ref local não resolvido: {ref}')
            continue
        base, sep, fragment = ref.partition('#')
        target=ids.get(base)
        if target is None:
            errors.append(f'{path}: $ref externo não corresponde a $id do catálogo: {ref}')
            continue
        if sep and resolve_pointer(docs[target], '#'+fragment) is None:
            errors.append(f'{path}: fragmento de $ref não resolvido: {ref}')

    return files, ids, all_refs, errors


def selftest():
    with tempfile.TemporaryDirectory() as td:
        root=Path(td)
        good={'$schema':DRAFT,'$id':'https://example.test/good','type':'object','properties':{'x':{'type':'string'}},'required':['x']}
        (root/'good.schema.json').write_text(json.dumps(good),encoding='utf-8')
        _,_,_,errs=validate_catalog(root)
        if errs: fail('self-test rejeitou schema válido: '+repr(errs))
        bad={'$schema':DRAFT,'$id':'https://example.test/bad','type':'object','propertiez':{}}
        (root/'bad.schema.json').write_text(json.dumps(bad),encoding='utf-8')
        _,_,_,errs=validate_catalog(root)
        if not any('keyword desconhecida' in e for e in errs): fail('self-test não detectou keyword inválida')
        (root/'bad.schema.json').write_text(json.dumps({'$schema':DRAFT,'$id':'https://example.test/good','type':'string'}),encoding='utf-8')
        _,_,_,errs=validate_catalog(root)
        if not any('$id duplicado' in e for e in errs): fail('self-test não detectou $id duplicado')
        (root/'bad.schema.json').write_text(json.dumps({'$schema':DRAFT,'$id':'https://example.test/bad-ref','$ref':'#/missing'}),encoding='utf-8')
        _,_,_,errs=validate_catalog(root)
        if not any('$ref local não resolvido' in e for e in errs): fail('self-test não detectou $ref local quebrado')
    print('JSON SCHEMA META GATE SELF-TEST: OK')


def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--root',default=str(Path(__file__).resolve().parent.parent/'config/contracts'))
    ap.add_argument('--summary')
    ap.add_argument('--self-test',action='store_true')
    a=ap.parse_args()
    if a.self_test: return selftest()
    files,ids,refs,errors=validate_catalog(Path(a.root).resolve())
    summary={'status':'PASS' if not errors else 'FAIL','schemaCount':len(files),'uniqueIds':len(ids),'referenceCount':len(refs),'draft':DRAFT,'errors':errors}
    if a.summary:
        p=Path(a.summary); p.parent.mkdir(parents=True,exist_ok=True); p.write_text(json.dumps(summary,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
    if errors: fail('; '.join(errors[:12]))
    print(f'JSON SCHEMA META GATE: OK ({len(files)} schemas; {len(ids)} $id únicos; {len(refs)} $ref)')

if __name__=='__main__': main()
