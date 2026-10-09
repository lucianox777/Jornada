#!/usr/bin/env python3
"""Create one unique synthetic CI-only ZIP fixture; no SQL/Docker access."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
ORIGINAL = ROOT / "tests/fixtures/ingestao/AA01_v2"


def synthetic_cpf(key: str) -> str:
    digest = hashlib.sha256(key.encode("ascii")).digest()
    digits = [int(x) for x in f"{int.from_bytes(digest[:8], 'big') % (10**9):09d}"]
    if len(set(digits)) == 1:
        digits[0] = (digits[0] + 1) % 10

    def check(prefix: list[int], multiplier: int) -> int:
        rem = (sum(d * (multiplier - idx) for idx, d in enumerate(prefix)) * 10) % 11
        return 0 if rem == 10 else rem

    digits.append(check(digits, 10))
    digits.append(check(digits, 11))
    return "".join(map(str, digits))


def main() -> None:
    run = os.environ.get("GITHUB_RUN_ID", "")
    attempt = os.environ.get("GITHUB_RUN_ATTEMPT", "")
    assert (os.environ.get("GITHUB_ACTIONS") == "true"
            and os.environ.get("CI") == "true"
            and os.environ.get("GITHUB_REPOSITORY") == "lucianox777/Jornada"
            and os.environ.get("JORNADA_WORKERS_E2E_RUNTIME_TEST") == "true"
            and re.fullmatch(r"[0-9]{6,16}", run)
            and re.fullmatch(r"[0-9]{1,3}", attempt)
            and os.environ.get("JORNADA_WORKERS_E2E_ID") == f"ci{run}{attempt}"), "private GitHub E2E only"
    assert len(sys.argv) == 2, "output must be supplied"
    out = Path(sys.argv[1]).resolve()
    root = (ROOT / ".local/e2e/c3-2f2c-recovery").resolve()
    assert out == root / "fixture", "no arbitrary output destinations"
    assert not out.is_symlink() and not out.exists(), "refuse to overwrite existing fixture"
    out.mkdir(parents=True, exist_ok=False)

    manifest = (ORIGINAL / "manifest.json").read_text(encoding="utf-8")
    person = json.loads((ORIGINAL / "pessoas.jsonl").read_text(encoding="utf-8").strip())
    fact = json.loads((ORIGINAL / "registros.jsonl").read_text(encoding="utf-8").strip())
    suffix = f"CI-REC-{run}-{attempt}"
    person["cpf"] = synthetic_cpf(suffix)
    person["cpfAusenteMotivo"] = None
    person["nomeCompleto"] = "Pessoa Sintética " + suffix
    person["nomeMae"] = "Mae Sintetica CI"
    person["idPessoaEntrega"] = suffix
    person["sourceTransactionId"] = suffix + "-TX"
    for idx, attribute in enumerate(person.get("atributosTransversais", [])):
        attribute["sourceRecordId"] = suffix + "-AT-" + str(idx)
    fact["idPessoaEntrega"] = suffix
    fact["codigoRegistroOrigem"] = suffix + "-REG"
    (out / "manifest.json").write_text(manifest, encoding="utf-8")
    for name, data in (("pessoas.jsonl", person), ("registros.jsonl", fact)):
        (out / name).write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    # stdout is only an output directory, no real identity / access key.
    print(out)


if __name__ == "__main__":
    main()
