#!/usr/bin/env python3
"""Gate 6: fail-closed boundary between Jornada product and SEHAB support.

The principal product may carry byte-identical JSON contract copies for runtime interoperability.
Code, projects, governance ownership and SEHAB-specific product configuration remain segregated.
"""
from __future__ import annotations

import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

MAIN_INVENTORY = "Solution/config/governance/schema-approvals.json"
SUPPORT_INVENTORY = "ApoioSecretarias/config/governance/schema-approvals.SEHAB.json"
SEHAB_SCHEMA_PREFIX = "config/contracts/gestores/sehab/"
SEHAB_AA01_METADATA = "config/contracts/registros/aa01/v1/registro.json"
FORBIDDEN_PRODUCT_PATHS = (
    "Solution/config/gestores/SEHAB",
    "Solution/config/contracts/registros/AA01/v1/registro.json",
    "Solution/src/Jornada.Integrador.CSharp",
    "Solution/tests/Jornada.Integrador.Tests",
)
EXPECTED_SUPPORT = {
    *(
        f"config/contracts/gestores/SEHAB/pessoa/v{version}/{name}"
        for version, names in (
            (1, ("pessoa.json", "pessoa.schema.json")),
            (2, ("pessoa.json", "pessoa.schema.json")),
            (3, ("pessoa.schema.json",)),
            (4, ("pessoa.schema.json",)),
            (5, ("pessoa.schema.json",)),
        )
        for name in names
    ),
    "config/contracts/registros/AA01/v1/registro.json",
}
REFERENCES = {"ProjectReference", "Compile", "Content", "None", "EmbeddedResource"}


def inventory(root: Path, relative: str, errors: list[str]) -> list[dict]:
    path = root / relative
    try:
        obj = json.loads(path.read_text(encoding="utf-8"))
        contracts = obj["contracts"]
        if not isinstance(contracts, list) or not all(isinstance(c, dict) and
            isinstance(c.get("path"), str) for c in contracts):
            raise ValueError("contracts inválido")
        names = [c["path"] for c in contracts]
        if len(names) != len(set(names)):
            errors.append(f"{relative}: paths duplicados")
        return contracts
    except (OSError, ValueError, KeyError, TypeError) as exc:
        errors.append(f"{relative}: inventário indisponível/inválido: {exc}")
        return []


def audit(root: Path) -> list[str]:
    root = Path(root)
    errors: list[str] = []

    for rel in FORBIDDEN_PRODUCT_PATHS:
        path = root / rel
        if path.is_file() or path.is_symlink() or (path.is_dir() and any(path.rglob("*"))):
            errors.append(f"Artefato externo reintroduzido no produto: {rel}")

    main_sln = root / "Solution/Jornada.sln"
    support_sln = root / "ApoioSecretarias/SolucaoApoioSecretarias.sln"
    if not main_sln.is_file():
        errors.append("Solution/Jornada.sln ausente")
    elif re.search(r"Jornada[.]Integrador|ApoioSecretarias", main_sln.read_text(encoding="utf-8"), re.I):
        errors.append("Solution/Jornada.sln inclui integrador ou solução de apoio")
    if not support_sln.is_file():
        errors.append("Solução de Apoio independente ausente")
    elif "Jornada.Integrador.CSharp" not in support_sln.read_text(encoding="utf-8"):
        errors.append("Transmissor ausente da Solução de Apoio")

    # Runtime copies are allowed only when byte-identical to the support-owned contracts.
    runtime_copy = root / "Solution/config/contracts/gestores/SEHAB"
    support_copy = root / "ApoioSecretarias/config/contracts/gestores/SEHAB"
    for support_file in support_copy.rglob("*.json"):
        relative = support_file.relative_to(support_copy)
        product_file = runtime_copy / relative
        if not product_file.is_file():
            errors.append(f"Cópia runtime SEHAB ausente: {product_file.relative_to(root)}")
        elif product_file.read_bytes() != support_file.read_bytes():
            errors.append(f"Cópia runtime SEHAB divergente: {product_file.relative_to(root)}")

    main = inventory(root, MAIN_INVENTORY, errors)
    for entry in main:
        path = entry["path"].replace("\\", "/").lower()
        if path.startswith(SEHAB_SCHEMA_PREFIX) or path == SEHAB_AA01_METADATA:
            errors.append(f"Inventário principal contém contrato externo: {entry['path']}")
    support = inventory(root, SUPPORT_INVENTORY, errors)
    support_paths = {entry["path"] for entry in support}
    if support_paths != EXPECTED_SUPPORT or len(support) != len(EXPECTED_SUPPORT):
        errors.append("Inventário do apoio perdeu a titularidade exata dos 7 contratos Pessoa e AA01")

    # Contrato factual de apoio é cópia derivada byte a byte do schema canônico.
    # O hash pinado não representa aprovação; detectar drift em ambos os lados.
    index_path = root / "ApoioSecretarias/config/governance/factual-schema-sources.json"
    try:
        derived = json.loads(index_path.read_text(encoding="utf-8"))
        entries = derived["contracts"]
        if len(entries) != 1 or any(entry.get(k) != v for k, v in
            {"natureza": "BENEFICIO", "codigoTipo": "AA01", "tipoVersao": 1}.items()
            for entry in entries):
            errors.append("Inventário factual do apoio tem tipo/versão inesperado")
        for entry in entries:
            expected = entry["sha256"]
            for rel in ("ApoioSecretarias/" + entry["path"], entry["canonicalSource"]):
                actual = hashlib.sha256((root / rel).read_bytes()).hexdigest()
                if actual != expected:
                    errors.append(f"Schema factual divergente: {rel}")
    except (OSError, KeyError, ValueError, TypeError) as exc:
        errors.append(f"Contrato factual de apoio indisponível/inválido: {exc}")

    source_projects = list((root / "Solution/src").rglob("*.csproj"))
    if not source_projects:
        errors.append("Nenhum .csproj do produto encontrado (gate não executado)")
    for project in source_projects:
        try:
            document = ET.parse(project)
            for element in document.iter():
                tag = element.tag.rsplit("}", 1)[-1]
                if tag not in REFERENCES:
                    continue
                include = element.attrib.get("Include", "").replace("\\", "/").lower()
                if ("apoiosecretarias" in include
                        or "jornada.integrador.csharp" in include
                        or SEHAB_SCHEMA_PREFIX in include
                        or SEHAB_AA01_METADATA in include):
                    errors.append(f"{project.relative_to(root)}: referência externa proibida: {include}")
        except (ET.ParseError, OSError) as exc:
            errors.append(f"{project.relative_to(root)}: csproj inválido: {exc}")

    migration = root / "Solution/database/migrations/20260921_Pessoa_V5_Contrato_371.sql"
    if not migration.is_file():
        errors.append("Migration principal de Pessoa v5 ausente")
    else:
        sql = migration.read_text(encoding="utf-8")
        sql = re.sub(r"/\*.*?\*/", "", sql, flags=re.S)
        sql = re.sub(r"--[^\n]*", "", sql)
        if re.search(r"\bSEHAB\b", sql, re.I):
            errors.append("Migration principal registra SEHAB; registro v5 pertence ao apoio")

    return errors


if __name__ == "__main__":
    repository = Path(__file__).resolve().parents[2]
    findings = audit(repository)
    for finding in findings:
        print("FALHA SEGREGAÇÃO: " + finding, file=sys.stderr)
    if findings:
        sys.exit(1)
    print("OK: Gate 6 — inventários, projetos, contrato v5 e solução de apoio segregados")
