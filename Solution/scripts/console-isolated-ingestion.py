#!/usr/bin/env python3
"""Fail-closed ingestion transport for disposable Console browser E2E only."""
import hashlib
import json
import os
from pathlib import Path
import re
from urllib import request

ROOT = Path(__file__).resolve().parents[1]


def main():
    if os.environ.get("JORNADA_E2E_CONSOLE_ZIP") != "true":
        raise RuntimeError("Isolated ingestion requires explicit E2E opt-in")
    if os.environ.get("JORNADA_E2E_SQL_DATABASE") != "JornadaE2E" or os.environ.get("JORNADA_SQL_DATABASE_OVERRIDE", "JornadaE2E") != "JornadaE2E":
        raise RuntimeError("Isolated ingestion requires disposable JornadaE2E database")
    url = os.environ.get("JORNADA_E2E_API_URL")
    if url != "http://127.0.0.1:5088":
        raise RuntimeError("Isolated ingestion requires dedicated loopback API at port 5088")
    zip_path = Path(os.environ["JORNADA_E2E_INGESTION_ZIP"]).resolve(strict=True)
    expected_root = (ROOT / ".local" / "e2e" / "packages").resolve()
    if not zip_path.is_relative_to(expected_root) or not zip_path.is_file() or zip_path.is_symlink():
        raise RuntimeError("ZIP must reside inside disposable E2E packages directory")
    match = re.fullmatch(r"ENTREGA_SEHAB_[A-Za-z0-9_-]+_v2_([0-9a-f]{64})\.zip", zip_path.name)
    if not match:
        raise RuntimeError("Invalid synthetic ZIP filename")
    data = zip_path.read_bytes()
    sha = hashlib.sha256(data).hexdigest()
    if sha != match.group(1):
        raise RuntimeError("ZIP SHA-256 mismatch")
    credentials = json.loads((ROOT / "config/security/test-access-keys.json").read_text(encoding="utf-8"))["credentials"]
    keys = [c["accessKey"] for c in credentials if c.get("type") == "GESTOR"
            and c.get("publicCode") == "SEHAB" and "jornada.ingestao.write" in c.get("scopes", [])]
    if len(keys) != 1:
        raise RuntimeError("Missing unique synthetic SEHAB ingestion credential")
    idem = os.environ.get("JORNADA_E2E_IDEMPOTENCY_KEY", "sha256:" + sha)
    if not re.fullmatch(r"[A-Za-z0-9:_-]{1,100}", idem):
        raise RuntimeError("Invalid isolated idempotency key")
    req = request.Request(url + "/api/v1/ingestao/entregas", data=data, method="POST", headers={
        "X-Jornada-Gestor": "SEHAB", "X-Jornada-Access-Key": keys[0],
        "Idempotency-Key": idem, "Content-Type": "application/zip",
        "Content-Disposition": "attachment; filename=" + zip_path.name,
    })
    with request.urlopen(req, timeout=30) as response:
        receipt = json.loads(response.read())
        if response.status != 202 or not receipt.get("entregaId"):
            raise RuntimeError("API did not return a valid delivery receipt")
    target = ROOT / ".local" / "dev-console" / "last-ingestion.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps({"zip": str(zip_path), "gestor": "SEHAB",
                                  "database": "JornadaE2E", "receipt": receipt}, indent=2) + "\n",
                      encoding="utf-8")
    print("Entrega registrada: " + str(receipt["entregaId"]))
    print("ARTEFATO: " + str(target))


if __name__ == "__main__":
    main()
