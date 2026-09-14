#!/usr/bin/env python3
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BI = ROOT / "bi"
REPORT = BI / "Jornada.Report" / "definition"
PAGES = REPORT / "pages"
MODEL = BI / "Jornada.SemanticModel" / "definition"

EXPECTED_PAGES = {
    "Visão Geral",
    "Ingestão e Atualidade",
    "Qualidade dos Pacotes",
    "Processamento",
    "Qualidade da Pessoa",
    "Qualidade de Identidade por Origem",
    "Carga Inicial",
    "Identidade, Linkage e Convergência",
    "Atraso de Recebimento",
    "Executivo de Benefícios Concedidos",
    "Executivo de Serviços Prestados",
    "Registros",
    "Monetário",
    "Possibilidades Compatíveis",
    "Territorialização",
    "Pendências de Identidade",
    "Qualidade — Benefícios Concedidos",
    "Qualidade — Serviços Prestados",
    "Catálogo",
    "Quality Control",
    "API de Ingestão",
    "Manutenção da Bronze",
}


def fail(message: str) -> None:
    raise SystemExit(f"ERRO: {message}")


def main() -> int:
    for required in [
        BI / "Jornada.pbip",
        BI / "Jornada.Report" / "definition.pbir",
        REPORT / "report.json",
        REPORT / "version.json",
        PAGES / "pages.json",
        BI / "Jornada.SemanticModel" / "definition.pbism",
        MODEL / "model.tmdl",
        MODEL / "database.tmdl",
    ]:
        if not required.is_file():
            fail(f"artefato Power BI ausente: {required.relative_to(ROOT)}")

    page_files = sorted(PAGES.glob("*/page.json"))
    if len(page_files) != 22:
        fail(f"esperadas 22 páginas PBIR; encontradas {len(page_files)}")

    names = {}
    display_names = set()
    for page_file in page_files:
        page = json.loads(page_file.read_text(encoding="utf-8"))
        name = page.get("name")
        display = page.get("displayName")
        if name != page_file.parent.name:
            fail(f"page.name diverge do diretório: {page_file}")
        if not display or display in display_names:
            fail(f"displayName ausente/duplicado: {display!r}")
        visuals = list((page_file.parent / "visuals").glob("*/visual.json"))
        if not visuals:
            fail(f"página sem visual: {display}")
        names[name] = display
        display_names.add(display)

    if display_names != EXPECTED_PAGES:
        missing = sorted(EXPECTED_PAGES - display_names)
        extra = sorted(display_names - EXPECTED_PAGES)
        fail(f"catálogo de páginas diverge; missing={missing}; extra={extra}")

    metadata = json.loads((PAGES / "pages.json").read_text(encoding="utf-8"))
    order = metadata.get("pageOrder")
    if not isinstance(order, list) or len(order) != 22 or set(order) != set(names):
        fail("pages.json não referencia exatamente as 22 páginas")
    if metadata.get("activePageName") not in names:
        fail("activePageName não referencia página válida")

    for visual_file in PAGES.glob("*/visuals/*/visual.json"):
        visual = json.loads(visual_file.read_text(encoding="utf-8"))
        if "$schema" not in visual:
            fail(f"visual sem $schema: {visual_file.relative_to(ROOT)}")

    tmdl_files = sorted(MODEL.rglob("*.tmdl"))
    if len(tmdl_files) < 20:
        fail(f"modelo TMDL inesperadamente pequeno: {len(tmdl_files)} arquivos")
    all_tmdl = "\n".join(p.read_text(encoding="utf-8") for p in tmdl_files)
    required_serving = [
        'Schema="serving", Item="v_bi_registros"',
        'Schema="serving", Item="v_bi_beneficios_concedidos"',
        'Schema="serving", Item="v_bi_servicos_prestados"',
        'Schema="serving", Item="v_bi_possibilidades"',
        'Schema="serving", Item="v_bi_territorializacao"',
    ]
    for source in required_serving:
        if source not in all_tmdl:
            fail(f"fonte serving ausente do TMDL: {source}")
    if re.search(r"(?i)password\s*=|pwd\s*=|access[_-]?token\s*=", all_tmdl):
        fail("possível segredo versionado no TMDL")

    print(f"POWER BI STATIC GATE: OK (22 páginas; {len(tmdl_files)} TMDL; catálogo/ordem/visuais coerentes)")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (json.JSONDecodeError, OSError) as exc:
        print(f"ERRO: {exc}", file=sys.stderr)
        raise SystemExit(2)
