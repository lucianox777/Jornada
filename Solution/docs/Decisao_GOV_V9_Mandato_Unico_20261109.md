# GOV-V9-MANDATO-UNICO-20261109 — coordenação pós-integração

**Autoridade:** Product Owner, mandato consolidado de 09/11/2026. **Estado:** DECIDIDO / EXECUÇÃO PARCIAL; não equivale a IMPLEMENTADO, MEDIDO, VALIDADO ou ATIVADO.

## Contexto e fundamento

A V9 alcançou fechamento técnico no commit `e0e98f99c6290ac76162a8ca1852690b897e954a`, a master era `b7d9136b7457fcb9cf5ca7b67e77b9e394d7bc61` no início da nova rodada. Provas Windows, cluster, validação independente, SQL 100k V8 e HTTP 1k/10k já existiam. Restaram OOM #1302, credenciais #1253, tag/release, inventário, escala V9 real 100k e planejamento científico. A multiplicação de vigílias e mandatos gerava duplicações e extrapolações de teste.

## Decisão e precedência

O mandato de 09/11 **substitui apenas instruções operacionais anteriores**. Não revoga contratos estatísticos, segurança, GOV-DOC-01 nem direitos de decisão do PO. Ordem: prioridade zero #1302 → preflight #1253 → tag anotada/release `v9.0.0` **em `e0e98f99`**, sem sobrescrever tag → triagem documental D → prova física V9 100k C → manifesto LOCK proposto em **PR DRAFT NÃO MESCLAR** B → melhorias #1362/#1363 planejadas.

Proibidos push direto na master, merge fora do HEAD e gates, novos testes físicos redundantes, ativação, TEST cego, alteração de HML/PRD, manipulação de #1352/#1356, parâmetros científicos silenciosos e falhas de confidencialidade. Painel único [#1365](https://github.com/lucianox777/Jornada/issues/1365); acompanhamento horário deve editar o mesmo comentário e não substituir documentos oficiais. O limite é três pushes por PR.

## Impacto e rastreabilidade

- #1302: [reconciliação](Relatorio_V9_1302_Reconciliacao_OOM_20261109.md), engenharia RAM-first. Evidência histórica margem 0 × contrato positivo 0,0001 preservada.
- #1253: [preflight](Relatorio_V9_Preflight_Seguranca_20261109.md), logs históricos ainda pendentes.
- Frente A: **TAG/RELEASE NÃO VERIFICADAS COMO EXISTENTES no preflight** (REST 404); nenhuma publicada por esta PR, não declarar congelamento final.
- Frente D: [triagem de 56 issues](Triagem_PosIntegracao_V9_20261109.md) e [trilha proposta de HML](Plano_Proposto_Implantacao_V9_20261109.md).
- Frente C: #1349 **NÃO COBERTO**, V8 100k != Calibrador V9 100k.
- Frente B: preparo independente sem execução nem consumo de seed.
- #1362/#1363: DECIDIDO / IMPLEMENTAÇÃO PENDENTE na PR #1364.

**Critério documental:** incorporar documentos e índice por PR protegida, conferir SHA/gates pós-merge, só então declarar registrado integralmente. O marco histórico `e0e98f99` não deve ser alterado para acomodar documentação nova. GOV-DOC-01.
