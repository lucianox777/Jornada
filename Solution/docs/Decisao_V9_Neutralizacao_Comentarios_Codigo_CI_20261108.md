# NEUT-DEV-CODE-CI-20261108 — neutralização de comentários de código, script e CI

**Decisão funcional/editorial do Product Owner:** 07/11/2026, mandato V9 A–L, frente H/#1328. **Implementação:** proposta na mesma PR H; não alterar comportamento operacional, código executável, hashes selados ou histórico Git. **Governança:** GOV-DOC-01, índice de decisões vigentes.

## Contexto, justificativa e precedência

O PO determinou substituir a referência nominal ao antigo fornecedor pelo termo literal **DESENVOLVEDOR**, com ajustes somente de concordância gramatical quando necessários, em textos atuais, comentários de código/CI e artefatos editáveis. A migração do valor de domínio `ENRIQUECIMENTO_DESENVOLVEDOR` pertence à frente G/#1334 e deve ser **forward-only** sobre o schema 3.70 selado; documentos editáveis e binários pertencem às frentes K/L/F. Esta frente H não atua em qualquer desses outros domínios.

O escopo técnico isolado de H corrige somente comentários/strings de documentação de três caminhos:

- `.github/workflows/ci.yml`: rótulo textual de autenticação e separação ambiental no relatório institucional de gates;
- `Solution/scripts/consolidate-requirements.py`: docstring e texto do índice institucional produzido pelo script, com ajuste gramatical para “pelo DESENVOLVEDOR”;
- `Solution/src/Jornada.Processor.Worker/GeographicEnrichment.cs`: comentário XML de responsabilidade de territorialização, sem modificar o algoritmo nem qualquer enum de origem.

## Contrato de aceitação e limites

1. As quatro ocorrências anteriores presentes nestes três arquivos são substituídas; em seus HEADs atualizados, nenhum texto nominal legado deve restar (busca case-insensitive). O diff não deve incluir novas regras de negócio, segredo, valor de domínio nem alteração de migração selada.
2. O HEAD da PR H exige checks rápidos aplicáveis; o merge só ocorre em fila única após os gates completos da master pós-E, no SHA exato. Após merge, verificar a CI da master e o 30k se for exigido pelo classificador.
3. A varredura total de texto e dos 45 arquivos Office/PDF, a prova de renderização DOCX, a lista de PDFs a regenerar e o valor novo de domínio SQL são **frentes separadas**; nenhum sucesso de H atesta neutralização integral do repositório.
4. Sem campanha científica, TEST/Gold/DT-15, habilitação institucional ou ativação; a implementação de H não modifica a calibração, o P95, o FDR ou a dívida técnica [#1341](https://github.com/lucianox777/Jornada/issues/1341).

## Rastreabilidade

- [PR H/#1328](https://github.com/lucianox777/Jornada/pull/1328) — três arquivos, quatro substituições, checks da PR e eventual merge.
- [PR G/#1334](https://github.com/lucianox777/Jornada/pull/1334) — extensão SQL forward-only do valor de domínio.
- [PR K/#1318](https://github.com/lucianox777/Jornada/pull/1318) e [PR L/#1333](https://github.com/lucianox777/Jornada/pull/1333) — consolidação da especificação e substituição documental.
- [PR F/#1332](https://github.com/lucianox777/Jornada/pull/1332) — auditoria e eventual reescrita controlada DOCX/PDF.
- [Índice de decisões vigentes](Indice_Decisoes_Vigentes.md) — governança GOV-DOC-01.

**Status:** registro proposto na PR H; não declarar merge ou validação física antes dos recibos exatos.
