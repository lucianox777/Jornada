#!/usr/bin/env python3
"""Build the institutional v1.1 requirements baseline from v1.0 + v1.1 addenda.

The top-level v1.1 RF/RNF/traceability documents are self-contained delivery
artifacts. The original additive v1.1 markdown is preserved under
Documentos/Requisitos/Historico so the consolidation remains auditable without
forcing SEI/PRODAM readers to combine two files per subject.
"""
from __future__ import annotations

import argparse
import importlib.util
import re
import shutil
import subprocess
import tempfile
from pathlib import Path

from docx import Document


def normalize_rnf_ids(text: str) -> str:
    """Normalize historical RNF01..RNF33 references to RNF-001..RNF-033."""
    def repl(match: re.Match[str]) -> str:
        return f"RNF-{int(match.group(1)):03d}"

    text = re.sub(r"\bRNF(\d{2})\b", repl, text)
    return text.replace("SGM/SEPE", "governança institucional").replace("SGM/SPE", "governança institucional")


def replace_line(text: str, prefix: str, replacement: str) -> str:
    pattern = rf"(?m)^{re.escape(prefix)}.*$"
    if not re.search(pattern, text):
        raise RuntimeError(f"metadata line not found: {prefix}")
    return re.sub(pattern, replacement, text, count=1)


def update_common_header(text: str, kind: str, status: str) -> str:
    text = normalize_rnf_ids(text)
    text = replace_line(text, "**Versão do documento:**", "**Versão do documento:** 1.1  ")
    text = replace_line(text, "**Data:**", "**Data:** 10/09/2026  ")
    text = replace_line(text, "**SolutionSchema:**", "**SolutionSchema:** SolutionSchema v3.70  ")
    if "**Release de incorporação:**" in text:
        text = replace_line(
            text,
            "**Release de incorporação:**",
            "**Estado de incorporação:** candidato técnico à consolidação Solution Engenharia v5.00; release/tag ainda não cortada  ",
        )
    text = replace_line(text, "**Status:**", f"**Status:** {status}")
    note = (
        f"\n> **Leitura institucional.** Esta versão 1.1 é o baseline {kind} consolidado e autossuficiente. "
        "O arquivo v1.0 permanece no repositório apenas para rastreabilidade histórica; não é necessário "
        "lê-lo cumulativamente com este documento. Referências históricas `RNF01` a `RNF33` foram "
        "normalizadas para `RNF-001` a `RNF-033` sem mudança semântica.\n"
    )
    marker = f"**Status:** {status}"
    return text.replace(marker, marker + note, 1)


def extract_from(text: str, marker: str) -> str:
    pos = text.find(marker)
    if pos < 0:
        raise RuntimeError(f"additive marker not found: {marker}")
    return text[pos:].strip()


def preserve_additive(current: Path, historical: Path) -> None:
    historical.parent.mkdir(parents=True, exist_ok=True)
    if historical.exists():
        return
    payload = current.read_text(encoding="utf-8")
    if "Aditivo v1.1" not in payload and "COMPLEMENTO" not in payload:
        raise RuntimeError(f"cannot preserve additive source from already-consolidated file: {current}")
    historical.write_text(payload, encoding="utf-8")


def build_rf(req: Path) -> None:
    baseline = (req / "02_Requisitos_Funcionais_Jornada_v1.0.md").read_text(encoding="utf-8")
    additive = (req / "Historico" / "02_Requisitos_Funcionais_Jornada_Aditivo_v1.1.md").read_text(encoding="utf-8")
    baseline = update_common_header(baseline, "funcional", "BASELINE FUNCIONAL CONSOLIDADO DA FASE 1")
    extra = normalize_rnf_ids(extract_from(additive, "## RF-051"))
    extra = re.sub(r"(?m)^## (RF-05[1-6] - )", r"### \1", extra)
    extra = extra.replace("## Governança comum", "## Governança dos requisitos incorporados na v1.1")
    out = baseline.rstrip() + "\n\n## Requisitos funcionais incorporados na v1.1\n\n" + extra + "\n"
    (req / "02_Requisitos_Funcionais_Jornada_v1.1.md").write_text(out, encoding="utf-8")


def build_rnf(req: Path) -> None:
    baseline = (req / "03_Requisitos_Nao_Funcionais_Jornada_v1.0.md").read_text(encoding="utf-8")
    additive = (req / "Historico" / "03_Requisitos_Nao_Funcionais_Jornada_Aditivo_v1.1.md").read_text(encoding="utf-8")
    baseline = update_common_header(baseline, "não funcional", "BASELINE NÃO FUNCIONAL CONSOLIDADO DA FASE 1")
    extra = normalize_rnf_ids(extract_from(additive, "## RNF12"))
    extra = extra.replace(
        "## RNF-012 - Testabilidade e regressão - complemento vigente",
        "### Complemento v1.1 de RNF-012 - Testabilidade e regressão",
    )
    extra = re.sub(r"(?m)^## (RNF34-[A-D] - )", r"### \1", extra)
    extra = extra.replace("## Aplicação ao Linkage", "## Aplicação consolidada ao Linkage")
    intro = (
        "## Complementos não funcionais incorporados na v1.1\n\n"
        "Os quatro identificadores aditivos `RNF34-A` a `RNF34-D` permanecem estáveis nesta versão para "
        "não quebrar rastreabilidade já publicada no change-set. A política canônica para os 33 requisitos "
        "históricos é `RNF-001` a `RNF-033`. Uma futura renumeração integral dos aditivos, se decidida, deve "
        "ser feita como rebaseline explícito e com atualização conjunta da matriz.\n\n"
    )
    out = baseline.rstrip() + "\n\n" + intro + extra + "\n"
    (req / "03_Requisitos_Nao_Funcionais_Jornada_v1.1.md").write_text(out, encoding="utf-8")


def update_matrix_header(text: str) -> str:
    text = normalize_rnf_ids(text)
    text = text.replace("# Matriz de Rastreabilidade", "# Matriz de Rastreabilidade - Jornada do Cidadão - Fase 1", 1)
    if "**Versão do documento:**" not in text:
        title_end = text.find("\n")
        text = text[: title_end + 1] + "\n**Versão do documento:** 1.1  \n" + text[title_end + 1 :]
    else:
        text = replace_line(text, "**Versão do documento:**", "**Versão do documento:** 1.1  ")
    text = replace_line(text, "**Data:**", "**Data:** 10/09/2026  ")
    if "**SolutionSchema:**" in text:
        text = replace_line(text, "**SolutionSchema:**", "**SolutionSchema:** SolutionSchema v3.70  ")
    if "**Release de incorporação:**" in text:
        text = replace_line(
            text,
            "**Release de incorporação:**",
            "**Estado de incorporação:** candidato técnico à consolidação Solution Engenharia v5.00; release/tag ainda não cortada  ",
        )
    if "**Status:**" in text:
        text = replace_line(text, "**Status:**", "**Status:** MATRIZ CONSOLIDADA E AUTOSSUFICIENTE DA FASE 1")
    note = (
        "\n> **Leitura institucional.** Esta matriz v1.1 contém a rastreabilidade histórica e os aditivos "
        "vigentes em um único artefato. A matriz v1.0 permanece somente como histórico e não precisa ser "
        "consultada em conjunto. Referências `RNF01` a `RNF33` foram normalizadas para `RNF-001` a "
        "`RNF-033` sem alteração de significado.\n"
    )
    first_blank = text.find("\n\n", text.find("**Status:**"))
    if first_blank >= 0:
        text = text[:first_blank] + note + text[first_blank:]
    return text


def build_matrix(req: Path) -> None:
    baseline = (req / "05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.0.md").read_text(encoding="utf-8")
    additive = (req / "Historico" / "05_Matriz_Rastreabilidade_Requisitos_Jornada_Aditivo_v1.1.md").read_text(encoding="utf-8")
    baseline = update_matrix_header(baseline)
    extra = normalize_rnf_ids(extract_from(additive, "## Novos requisitos funcionais"))
    out = baseline.rstrip() + "\n\n## Incorporações da versão 1.1\n\n" + extra + "\n"
    (req / "05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1.md").write_text(out, encoding="utf-8")


def build_index(req: Path) -> None:
    content = """# Índice Mestre de Requisitos - Jornada do Cidadão - Fase 1

**Versão do documento:** 1.1  
**Data:** 10/09/2026  
**Base normativa:** Especificação Técnica Jornada v3.62  
**SolutionSchema:** SolutionSchema v3.70  
**Estado de incorporação:** candidato técnico à consolidação Solution Engenharia v5.00; release/tag ainda não cortada  
**Status:** PORTA DE ENTRADA DO BASELINE INSTITUCIONAL CONSOLIDADO

## 1. Regra de leitura institucional

Para tramitação, revisão pela PRODAM e juntada ao SEI, a leitura corrente é feita por **um único documento por número**, sempre na versão 1.1:

1. `01_Requisitos_de_Negocio_Jornada_v1.1`
2. `02_Requisitos_Funcionais_Jornada_v1.1`
3. `03_Requisitos_Nao_Funcionais_Jornada_v1.1`
4. `04_Requisitos_Tecnicos_Jornada_v1.1`
5. `05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1`

Os arquivos v1.0 permanecem no repositório exclusivamente como baselines históricos e **não devem ser lidos cumulativamente** com os v1.1. Os antigos aditivos usados para formar 02, 03 e 05 foram preservados em `Documentos/Requisitos/Historico/` apenas para auditoria da consolidação.

## 2. Estrutura adotada

```text
RN - Requisitos de Negócio
        ↓
RF - Requisitos Funcionais ─────┐
        ↓                        │
RNF - Requisitos Não Funcionais │
        ↓                        │
RT - Requisitos Técnicos <──────┘
        ↓
Testes / Gates / Evidências
```

A seta representa rastreabilidade, não dependência de versão. Um RN pode produzir vários RF; um RNF pode afetar diversos RF; um RT pode realizar simultaneamente RF e RNF.

## 3. Baselines correntes da Fase 1

| Camada | Documento corrente | Versão | Escopo corrente | Regra de leitura |
|---|---|---:|---|---|
| RN | Requisitos de Negócio Jornada | 1.1 | 36 RN | Documento completo |
| RF | Requisitos Funcionais Jornada | 1.1 | 56 RF | Documento completo e consolidado |
| RNF | Requisitos Não Funcionais Jornada | 1.1 | 33 históricos + 4 aditivos | Documento completo e consolidado |
| RT | Requisitos Técnicos Jornada | 1.1 | baseline técnico vigente | Documento completo |
| Matriz | Matriz de Rastreabilidade | 1.1 | cadeias históricas + incorporações v1.1 | Documento completo e consolidado |

## 4. Política de identificadores RNF

A forma canônica dos 33 requisitos não funcionais históricos é `RNF-001` a `RNF-033`. Os arquivos v1.0 podem exibir a forma legada `RNF01` a `RNF33`; trata-se apenas de diferença de codificação, sem diferença semântica. Os documentos consolidados v1.1 normalizam essas referências.

Os identificadores `RNF34-A`, `RNF34-B`, `RNF34-C` e `RNF34-D` foram introduzidos como aditivos e são preservados nesta consolidação para não quebrar rastreabilidade já estabelecida. Eventual renumeração integral deve ocorrer apenas em rebaseline explícito, com atualização conjunta de requisitos, matriz, testes e documentação afetada.

## 5. Regra de governança

- **RN** muda por decisão de negócio/institucional.
- **RF** muda quando o comportamento esperado da solução muda.
- **RNF** muda quando qualidade, capacidade, segurança, compatibilidade ou restrição normativa muda.
- **RT** pode evoluir por arquitetura, plataforma, segurança, operação ou implementação sem alterar necessariamente RN/RF.
- Mudança material deve atualizar a Matriz de Rastreabilidade e indicar impacto nas camadas relacionadas.
- O baseline institucional corrente deve permanecer autossuficiente; aditivos de engenharia podem existir como histórico, mas não podem obrigar o destinatário institucional a montar manualmente o documento vigente.

## 6. Relação com os demais artefatos

A família de requisitos não substitui a Especificação Técnica v3.62, DDL, OpenAPI, JSON Schemas, documentação de arquitetura, runbooks ou evidências de teste. Ela organiza a intenção e a rastreabilidade entre esses artefatos. O estado técnico desta branch usa SolutionSchema v3.70, mas a release/tag v5.00 somente passa a existir quando for efetivamente cortada.

## 7. Controle de versão

| Versão | Data | Síntese | Incorporação |
|---|---|---|---|
| 1.0 | 03/09/2026 | Institui a hierarquia RN/RF/RNF/RT e a matriz única de rastreabilidade para a Fase 1. | Solution Engenharia v3.98 |
| 1.1 | 10/09/2026 | Consolida 02, 03 e 05 em artefatos autossuficientes, atualiza SolutionSchema para 3.70 e explicita a política de leitura e identificadores legados. | Candidato técnico à consolidação v5.00; release/tag ainda não cortada |
"""
    (req / "00_Indice_Mestre_Requisitos_Jornada_v1.1.md").write_text(content, encoding="utf-8")


def load_document_helper(root: Path):
    path = root / "Solution" / "scripts" / "generate-document-deliverables.py"
    spec = importlib.util.spec_from_file_location("jornada_document_deliverables", path)
    if spec is None or spec.loader is None:
        raise RuntimeError("cannot load generate-document-deliverables.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def convert_pdf(path: Path) -> Path:
    subprocess.run(
        ["libreoffice", "--headless", "--convert-to", "pdf", "--outdir", str(path.parent), str(path)],
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )
    pdf = path.with_suffix(".pdf")
    if not pdf.is_file() or pdf.stat().st_size == 0:
        raise RuntimeError(f"PDF conversion failed: {path}")
    return pdf


def render_deliverables(root: Path) -> None:
    req = root / "Documentos" / "Requisitos"
    helper = load_document_helper(root)
    with tempfile.TemporaryDirectory(prefix="jornada-req-consolidated-") as tmp:
        work = Path(tmp)
        reference = work / "reference_portrait.docx"
        helper.make_reference(reference, landscape=False)

        ordinary = [
            req / "00_Indice_Mestre_Requisitos_Jornada_v1.1.md",
            req / "02_Requisitos_Funcionais_Jornada_v1.1.md",
            req / "03_Requisitos_Nao_Funcionais_Jornada_v1.1.md",
        ]
        for md in ordinary:
            docx = md.with_suffix(".docx")
            subprocess.run(
                ["pandoc", str(md), "--reference-doc=" + str(reference), "-o", str(docx)],
                check=True,
            )
            convert_pdf(docx)

        matrix_md = req / "05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1.md"
        matrix_docx = matrix_md.with_suffix(".docx")
        helper.build_matrix(matrix_md, matrix_docx)
        convert_pdf(matrix_docx)

    expected = [
        "00_Indice_Mestre_Requisitos_Jornada_v1.1",
        "02_Requisitos_Funcionais_Jornada_v1.1",
        "03_Requisitos_Nao_Funcionais_Jornada_v1.1",
        "05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1",
    ]
    for stem in expected:
        md = req / f"{stem}.md"
        docx = req / f"{stem}.docx"
        pdf = req / f"{stem}.pdf"
        payload = md.read_text(encoding="utf-8")
        if "autossuficiente" not in payload.lower() and stem != "00_Indice_Mestre_Requisitos_Jornada_v1.1":
            raise RuntimeError(f"missing self-contained marker: {md}")
        Document(docx)
        if not pdf.is_file() or pdf.stat().st_size < 1000:
            raise RuntimeError(f"invalid PDF: {pdf}")

    rf = (req / "02_Requisitos_Funcionais_Jornada_v1.1.md").read_text(encoding="utf-8")
    rnf = (req / "03_Requisitos_Nao_Funcionais_Jornada_v1.1.md").read_text(encoding="utf-8")
    matrix = (req / "05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1.md").read_text(encoding="utf-8")
    index = (req / "00_Indice_Mestre_Requisitos_Jornada_v1.1.md").read_text(encoding="utf-8")
    for marker in ["RF-001", "RF-050", "RF-051", "RF-056"]:
        if marker not in rf:
            raise RuntimeError(f"RF consolidation missing {marker}")
    for marker in ["RNF-001", "RNF-033", "RNF34-A", "RNF34-D"]:
        if marker not in rnf:
            raise RuntimeError(f"RNF consolidation missing {marker}")
    for marker in ["RF-051", "RNF34-C", "RNF-001"]:
        if marker not in matrix:
            raise RuntimeError(f"traceability consolidation missing {marker}")
    for marker in ["um único documento por número", "RF-001", "RNF-001", "SolutionSchema v3.70"]:
        if marker not in index:
            raise RuntimeError(f"master index missing {marker}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--render", action="store_true", help="also generate DOCX/PDF deliverables")
    args = parser.parse_args()
    root = args.root.resolve()
    req = root / "Documentos" / "Requisitos"
    hist = req / "Historico"

    preserve_additive(req / "02_Requisitos_Funcionais_Jornada_v1.1.md", hist / "02_Requisitos_Funcionais_Jornada_Aditivo_v1.1.md")
    preserve_additive(req / "03_Requisitos_Nao_Funcionais_Jornada_v1.1.md", hist / "03_Requisitos_Nao_Funcionais_Jornada_Aditivo_v1.1.md")
    preserve_additive(req / "05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1.md", hist / "05_Matriz_Rastreabilidade_Requisitos_Jornada_Aditivo_v1.1.md")

    build_rf(req)
    build_rnf(req)
    build_matrix(req)
    build_index(req)

    if args.render:
        render_deliverables(root)

    print("REQUIREMENTS CONSOLIDATION: OK (00/02/03/05 v1.1 self-contained; additive sources preserved under Historico)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
