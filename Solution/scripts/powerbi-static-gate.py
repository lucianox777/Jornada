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
TABLES = MODEL / "tables"
BASELINE = ROOT / "database" / "Jornada_Fase1_v3.70.sql"

EXPECTED_PAGES = {
    "Visão Geral",
    "Ingestão e Atualidade",
    "Qualidade dos Pacotes",
    "Processamento",
    "Qualidade da Pessoa",
    "Qualidade de Identidade por Origem",
    "Carga Inicial",
    "Identidade, Linkage e Convergência",
    "Qualidade da Resolução",
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

MEASURE_DECL = re.compile(
    r"(?m)^\s*measure\s+(?:'((?:''|[^'])+)'|([^\s=]+))\s*="
)
COLUMN_DECL = re.compile(
    r"(?m)^\s*column\s+(?:'((?:''|[^'])+)'|([^\s=]+))(?=\s|=|$)"
)
SERVING_SOURCE = re.compile(
    r'Schema\s*=\s*"serving"\s*,\s*Item\s*=\s*"(?P<view>v_bi_[^"]+)"',
    re.IGNORECASE,
)
SQLCMD_INCLUDE = re.compile(r"(?im)^\s*:r\s+(?P<path>.+?)\s*$")
VIEW_DECL = re.compile(
    r"(?im)^\s*CREATE\s+(?:OR\s+ALTER\s+)?VIEW\s+"
    r"(?:\[?serving\]?\.)\[?(?P<view>v_bi_[A-Za-z0-9_]+)\]?"
)


def fail(message: str) -> None:
    raise SystemExit(f"ERRO: {message}")


def tmdl_name(quoted: str | None, bare: str | None) -> str:
    if quoted is not None:
        return quoted.replace("''", "'")
    if bare is not None:
        return bare
    raise ValueError("declaração TMDL sem nome")


def collect_measures() -> tuple[dict[str, set[str]], int]:
    measures_by_table: dict[str, set[str]] = {}
    global_names: dict[str, list[tuple[str, str, Path]]] = {}
    count = 0

    for table_file in sorted(TABLES.glob("*.tmdl")):
        table = table_file.stem
        text = table_file.read_text(encoding="utf-8")
        declared: set[str] = set()
        local_keys: set[str] = set()
        column_names: dict[str, str] = {}

        for match in COLUMN_DECL.finditer(text):
            name = tmdl_name(match.group(1), match.group(2))
            column_names[name.casefold()] = name

        for match in MEASURE_DECL.finditer(text):
            name = tmdl_name(match.group(1), match.group(2))
            key = name.casefold()
            if key in local_keys:
                fail(
                    f"Measure duplicado na tabela {table}: {name!r} "
                    f"({table_file.relative_to(ROOT)})"
                )
            if key in column_names:
                fail(
                    f"Measure conflita com coluna na tabela {table}: {name!r} "
                    f"x coluna {column_names[key]!r} ({table_file.relative_to(ROOT)})"
                )
            local_keys.add(key)
            declared.add(name)
            global_names.setdefault(key, []).append((table, name, table_file))
            count += 1

        measures_by_table[table] = declared

    duplicates = [entries for entries in global_names.values() if len(entries) > 1]
    if duplicates:
        details = []
        for entries in sorted(duplicates, key=lambda item: item[0][1].casefold()):
            display = entries[0][1]
            owners = ", ".join(sorted(entry[0] for entry in entries))
            details.append(f"{display!r} -> {owners}")
        fail("nomes globais de Measure duplicados: " + "; ".join(details))

    return measures_by_table, count


def collect_sqlcmd_install_surface(path: Path, seen: set[Path] | None = None) -> str:
    if seen is None:
        seen = set()

    resolved = path.resolve()
    root_resolved = ROOT.resolve()
    try:
        resolved.relative_to(root_resolved)
    except ValueError:
        fail(f"include sqlcmd fora da Solution: {path}")

    if resolved in seen:
        return ""
    if not resolved.is_file():
        fail(f"include sqlcmd ausente no baseline: {resolved.relative_to(root_resolved)}")

    seen.add(resolved)
    text = resolved.read_text(encoding="utf-8-sig")
    chunks = [text]
    for match in SQLCMD_INCLUDE.finditer(text):
        raw = match.group("path").strip()
        if raw.startswith('"') and raw.endswith('"'):
            raw = raw[1:-1]
        if "$(" in raw:
            fail(f"include sqlcmd parametrizado não suportado pelo gate: {raw}")
        include = ROOT / raw.replace("\\", "/")
        chunks.append(collect_sqlcmd_install_surface(include, seen))
    return "\n".join(chunks)


def iter_measure_refs(node: object):
    if isinstance(node, dict):
        measure = node.get("Measure")
        if isinstance(measure, dict):
            expression = measure.get("Expression")
            source_ref = expression.get("SourceRef") if isinstance(expression, dict) else None
            entity = source_ref.get("Entity") if isinstance(source_ref, dict) else None
            prop = measure.get("Property")
            if isinstance(entity, str) and isinstance(prop, str):
                yield entity, prop
        for value in node.values():
            yield from iter_measure_refs(value)
    elif isinstance(node, list):
        for value in node:
            yield from iter_measure_refs(value)


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
        BASELINE,
    ]:
        if not required.is_file():
            fail(f"artefato Power BI/SQL ausente: {required.relative_to(ROOT)}")

    page_files = sorted(PAGES.glob("*/page.json"))
    if len(page_files) != 23:
        fail(f"esperadas 23 páginas PBIR; encontradas {len(page_files)}")

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
    if not isinstance(order, list) or len(order) != 23 or set(order) != set(names):
        fail("pages.json não referencia exatamente as 23 páginas")
    if metadata.get("activePageName") not in names:
        fail("activePageName não referencia página válida")

    visual_files = sorted(PAGES.glob("*/visuals/*/visual.json"))
    parsed_visuals: list[tuple[Path, object]] = []
    for visual_file in visual_files:
        visual = json.loads(visual_file.read_text(encoding="utf-8"))
        if "$schema" not in visual:
            fail(f"visual sem $schema: {visual_file.relative_to(ROOT)}")
        parsed_visuals.append((visual_file, visual))

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
        'Schema="serving", Item="v_bi_qualidade_resolucao_operacional"',
        'Schema="serving", Item="v_bi_qualidade_resolucao_calibrada"',
    ]
    for source in required_serving:
        if source not in all_tmdl:
            fail(f"fonte serving ausente do TMDL: {source}")
    if re.search(r"(?i)password\s*=|pwd\s*=|access[_-]?token\s*=", all_tmdl):
        fail("possível segredo versionado no TMDL")

    semantic_serving_views = {
        match.group("view") for match in SERVING_SOURCE.finditer(all_tmdl)
    }
    if not semantic_serving_views:
        fail("modelo TMDL não referencia nenhuma view serving.v_bi_*")

    install_sql = collect_sqlcmd_install_surface(BASELINE)
    installed_serving_views = {
        match.group("view").casefold() for match in VIEW_DECL.finditer(install_sql)
    }
    missing_install = sorted(
        view
        for view in semantic_serving_views
        if view.casefold() not in installed_serving_views
    )
    if missing_install:
        fail(
            "views serving exigidas pelo TMDL não instaladas pelo baseline canônico: "
            + ", ".join(f"serving.{view}" for view in missing_install)
        )

    measures_by_table, measure_count = collect_measures()
    for visual_file, visual in parsed_visuals:
        for entity, prop in iter_measure_refs(visual):
            declared = measures_by_table.get(entity)
            if declared is None:
                fail(
                    f"visual referencia tabela de Measure inexistente: {entity!r} "
                    f"em {visual_file.relative_to(ROOT)}"
                )
            if prop not in declared:
                fail(
                    f"visual referencia Measure inexistente: {entity}.{prop} "
                    f"em {visual_file.relative_to(ROOT)}"
                )

    print(
        "POWER BI STATIC GATE: OK "
        f"(23 páginas; {len(tmdl_files)} TMDL; {measure_count} measures globais únicas; "
        f"{len(semantic_serving_views)} fontes serving instaláveis; "
        "catálogo/ordem/visuais/referências/nomes locais coerentes)"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (json.JSONDecodeError, OSError, ValueError) as exc:
        print(f"ERRO: {exc}", file=sys.stderr)
        raise SystemExit(2)
