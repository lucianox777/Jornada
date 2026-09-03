#!/usr/bin/env python3
from __future__ import annotations
import argparse
import hashlib
from pathlib import Path


def read_info(path: Path) -> dict[str, str]:
    result: dict[str, str] = {}
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        k, v = line.split("=", 1)
        result[k.strip()] = v.strip()
    return result


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def main() -> None:
    ap = argparse.ArgumentParser(
        description="Verifica SHA-256 dos artefatos de origem declarados em RELEASE_INFO; em modo estrito, predecessor não materializado bloqueia promoção."
    )
    ap.add_argument("--release-info", default=str(Path(__file__).resolve().parents[2] / "RELEASE_INFO.txt"))
    ap.add_argument("--artifact", action="append", default=[], metavar="KEY=PATH",
                    help="Associa a chave RELEASE_INFO (ex.: origem_engenharia_anterior_2) ao ZIP materializado.")
    ap.add_argument("--require-all-predecessors", action="store_true",
                    help="Falha se qualquer origem_engenharia_anterior_N estiver declarada como materializada=false.")
    args = ap.parse_args()

    info = read_info(Path(args.release_info))
    supplied: dict[str, Path] = {}
    for item in args.artifact:
        if "=" not in item:
            raise SystemExit(f"--artifact inválido: {item}")
        key, value = item.split("=", 1)
        supplied[key] = Path(value)

    origins = sorted(k for k in info if k.startswith("origem_engenharia_anterior_") and k.rsplit("_", 1)[-1].isdigit())
    if not origins:
        raise SystemExit("RELEASE_INFO não declara origem_engenharia_anterior_N.")

    checked = 0
    missing = []
    for key in origins:
        materialized = info.get(f"{key}_materializada", "").lower() == "true"
        if not materialized:
            missing.append(key)
            continue
        expected = info.get(f"{key}_sha256", "").lower()
        if len(expected) != 64 or any(c not in "0123456789abcdef" for c in expected):
            raise SystemExit(f"{key}: SHA-256 esperado ausente/inválido.")
        path = supplied.get(key)
        if path is None:
            raise SystemExit(f"{key}: artefato materializado foi declarado, mas não foi fornecido via --artifact.")
        if not path.is_file():
            raise SystemExit(f"{key}: arquivo não encontrado: {path}")
        actual = sha256(path)
        if actual != expected:
            raise SystemExit(f"{key}: SHA-256 divergente: esperado={expected} obtido={actual}")
        checked += 1

    if args.require_all_predecessors and missing:
        raise SystemExit("Predecessor(es) não materializado(s); promoção bloqueada: " + ", ".join(missing))

    note = f"; não materializados={','.join(missing)}" if missing else ""
    print(f"PREDECESSOR INTEGRITY GATE: OK ({checked} artefato(s) verificado(s){note})")


if __name__ == "__main__":
    main()
