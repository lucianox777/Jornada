from pathlib import Path
import hashlib, json

managers = ["SEHAB", "SMADS", "SMDET", "SMS"]
hashes = {}
for manager in managers:
    path = Path(f"Solution/config/contracts/gestores/{manager}/pessoa/v3/pessoa.schema.json")
    text = path.read_text(encoding="utf-8")
    marker = '  "$schema": "https://json-schema.org/draft/2020-12/schema",\n'
    schema_id = f'  "$id": "https://jornada.pmspsp.local/contracts/gestores/{manager}/pessoa/v3/pessoa.schema.json",\n'
    if '"$id"' not in text:
        if text.count(marker) != 1:
            raise SystemExit(f"{manager}: marcador $schema inválido")
        text = text.replace(marker, marker + schema_id)
        path.write_text(text, encoding="utf-8")
    hashes[manager] = hashlib.sha256(path.read_bytes()).hexdigest()

approval_path = Path("Solution/config/governance/schema-approvals.json")
approvals = json.loads(approval_path.read_text(encoding="utf-8"))
for item in approvals["contracts"]:
    for manager in managers:
        expected = f"config/contracts/gestores/{manager}/pessoa/v3/pessoa.schema.json"
        if item["path"] == expected:
            item["sha256"] = hashes[manager]
approval_path.write_text(json.dumps(approvals, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

case = "CASE g.codigo " + " ".join(f"WHEN '{m}' THEN 0x{hashes[m]}" for m in managers) + " END"
seed_path = Path("Solution/database/Jornada_Seed_Dev.sql")
seed = seed_path.read_text(encoding="utf-8")
old_hash = "0x2cec7a0ccda3e55770e2fe35b042904ed19ae0f55ca36694104e01a859276306"
if seed.count(old_hash) != 2:
    raise SystemExit(f"Seed: esperado hash compartilhado v3 em 2 ocorrências, encontrado {seed.count(old_hash)}")
seed = seed.replace(old_hash, case)
seed_path.write_text(seed, encoding="utf-8")

print(json.dumps(hashes, sort_keys=True))
