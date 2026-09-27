#!/usr/bin/env python3
import hashlib
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
support = json.loads((root / "config/governance/schema-approvals.SEHAB.json").read_text())
main = json.loads((root.parent / "Solution/config/governance/schema-approvals.json").read_text())
sehab = support["contracts"]
pessoa = [item for item in sehab if "/gestores/SEHAB/pessoa/" in item["path"]]
extra = [item for item in sehab if item["path"] == "config/contracts/registros/AA01/v1/registro.json"]
assert len(pessoa) == 7 and len(extra) == 1 and len(sehab) == 8, "Sete contratos Pessoa e metadado específico AA01"
assert not any("SEHAB/" in c["path"] or c["path"] == extra[0]["path"] for c in main["contracts"]), "SEHAB no inventário principal"
for entry in sehab:
    relative = entry["path"]
    file = root / relative
    assert file.exists(), f"Contrato ausente: {file}"
    actual = hashlib.sha256(file.read_bytes()).hexdigest()
    assert actual == entry["sha256"], f"Schema alterado: {relative}"
    assert entry["status"] == "PENDENTE" and entry["approval"] is None, "Aprovação inventada"
print("OK: 7 contratos Pessoa e 1 metadado AA01 SEHAB íntegros e ausentes do inventário principal")
