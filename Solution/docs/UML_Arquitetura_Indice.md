# Índice de diagramas — Jornada do Cidadão

**Atualização:** 21/09/2026  
**Status:** documentação técnica versionada  
**Notação:** UML 2.x quando aplicável; DER/ER permanece modelagem de dados, não UML.

## Regra de documentação

A entrega institucional deve permanecer legível em DOCX/PDF, com diagramas incorporados como figuras. O leitor final não depende de PlantUML, Mermaid ou outro renderer.

No repositório:
- PlantUML continua aceito como fonte textual de diagramas UML autônomos;
- Mermaid pode ser embutido em Markdown para revisão/rastreabilidade, desde que a semântica UML seja respeitada quando o diagrama for UML;
- ER/DER deve ser identificado como modelo de dados, não como UML;
- divergência entre diagrama e contrato executável deve ser reconciliada no mesmo change-set.

## Visões correntes

| Fonte | Tipo | Finalidade |
|---|---|---|
| `Invariantes_Arquitetura.md` | Fluxo + ER | Dependências principais, catálogo de invariantes e núcleo físico de identidade. |
| `Sequencias_Identidade_Linkage.md` SQ-01..SQ-06 | Sequência | Fato resolvido, CPF em conflito, sem CPF/Linkage, correção governada, ledger e fila governada. |
| `uml/Jornada_Arquitetura_Componentes.puml` | UML Componentes | Visão lógica de componentes. |
| `uml/Jornada_Implantacao.puml` | UML Implantação | Runtime, CI, storage e fontes externas. |
| `uml/Identidade_Progressiva_Estados.puml` | UML Estados | Ciclo de vida da identidade progressiva. |
| `uml/Linkage_Calibrador_Avaliador_IBGE.puml` | UML Sequência | Calibrador/Avaliador/IBGE. |
| `uml/Linkage_Dynamic_Blocking_Sequence.puml` | UML Sequência | Blocking dinâmico no runtime. |

## Sequências históricas 01..04

Os arquivos sob `diagrams/sequence/01..04` foram produzidos antes das mudanças de ledger, fila governada, publicação progressiva e conferência. Permanecem versionados para rastreabilidade até limpeza controlada, mas **`Sequencias_Identidade_Linkage.md` é a visão de revisão corrente**. Não manter dois diagramas como fontes normativas concorrentes.

## Rastreabilidade/frescor

`Diagramas_Rastreabilidade.json` liga cada visão corrente a âncoras de código/DDL e ao baseline de implementação revisado.

Nesta etapa o manifesto é **informativo**. Não há gate CI de frescor: a moratória de gates exige primeiro observar risco real de drift. Cada mudança material em uma âncora listada deve obrigar o autor/revisor a avaliar o diagrama no mesmo change-set; checkpoints em `Documentos/Revisoes/` registram a verificação.

## Convenção

Todo diagrama novo deve declarar: propósito, fonte, baseline ou versão de contrato e se representa estado implementado, planejado ou lacuna conhecida. Planejamento não pode ser desenhado como estado já implementado.
