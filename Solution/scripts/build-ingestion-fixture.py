#!/usr/bin/env python3
from __future__ import annotations
import argparse, hashlib, json, zipfile
from pathlib import Path

p=argparse.ArgumentParser()
p.add_argument('--fixture',required=True)
p.add_argument('--gestor',required=True)
p.add_argument('--output-dir',default='.local/e2e/packages')
a=p.parse_args()
fixture=Path(a.fixture).resolve()
manifest=json.loads((fixture/'manifest.json').read_text(encoding='utf-8'))
outdir=Path(a.output_dir).resolve(); outdir.mkdir(parents=True,exist_ok=True)
tmp=outdir/'package.tmp.zip'
with zipfile.ZipFile(tmp,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9) as z:
    for name in ('manifest.json','pessoas.jsonl','registros.jsonl'):
        info=zipfile.ZipInfo(name,date_time=(1980,1,1,0,0,0))
        info.compress_type=zipfile.ZIP_DEFLATED
        info.external_attr=0
        z.writestr(info,(fixture/name).read_bytes(),compress_type=zipfile.ZIP_DEFLATED,compresslevel=9)
sha=hashlib.sha256(tmp.read_bytes()).hexdigest()
filename=f"ENTREGA_{a.gestor.upper()}_{manifest['codigoSistemaOrigem']}_v{manifest['formatoVersao']}_{sha}.zip"
target=outdir/filename
tmp.replace(target)
print(target)
