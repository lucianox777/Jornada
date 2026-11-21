# GOV-V9-LIBERACAO-CONDICIONADA-GATES-20261121 — decisão do PO

**Data:** 21/11/2026. **Autoridade:** Product Owner (PO). **Estado:** AUTORIZAÇÃO CONDICIONAL APROVADA; condições ainda não comprovadas. **Governança:** GOV-DOC-01.

## Manifestação e contexto

O PO esclareceu, após retirar a interpretação equivocada que originou a PR #1818 (encerrada sem merge): “Não, mas depois que a CI acabar verde e todos os testes, esse último merge, a calibração tá liberada.” O último merge referido é a PR #1817, squash SHA `cd20ace78d3af8b633a3d77007bed031783a0acc`, que corrigiu o Runner E2E sintético DT-05. A CI obrigatória da PR e o teste Three Waves passaram no HEAD da PR.

## Decisão, justificativa e efeitos

O PO **aprova condicionalmente a liberação da calibração V9 após** comprovação de CI verde e **todos os testes/gates aplicáveis** ao último merge, sem exigir nova aprovação genérica de liberação depois de cumpridas as condições. A aprovação **não é declaração de que os testes já passaram** e não autoriza executar campanhas científicas antes de satisfazer os gates. Justificativa: a correção operacional está integrada, faltando comprovação objetiva pós-merge e integridade científica.

## Critérios verificáveis e precedência

1. Confirmar `jornada-ci` no SHA integrado da master, sem tratar checks de PR como prova pós-merge.
2. Confirmar `v9-physical-seed42-probe` e o classificador acumulativo, executando o físico 30k apenas quando indicado pela política vigente.
3. Confirmar integridade independente do gerador e dos digests científicos LOCKED no SHA master, com evidência reproduzível.
4. Satisfazer os demais critérios de aceite da P0-A #1734 e testes científicos/operacionais pertinentes, registrando resultados e artefatos.
5. Registrar comprovação e estado no acervo oficial e índice antes de declarar LIBERADO.

Esta decisão esclarece a autorização **condicional** de liberação da calibração V9; preserva GOV-OPS-CORRECAO-SQUASH-CI-MASTER-20261111, GOV-DOC-01, regras LOCKED, separação TRAIN/VALIDATION/TEST e proteção de master. Não dispensa gates, não autoriza automaticamente TEST/Gold, campanha 100k, publicação ou ativação produtiva. A execução concreta deve respeitar os parâmetros, escopo e autorizações específicos vigentes.

## Rastreabilidade

- PR #1817: https://github.com/lucianox777/Jornada/pull/1817 (merge `cd20ace78d3af8b633a3d77007bed031783a0acc`).
- PR #1818: https://github.com/lucianox777/Jornada/pull/1818 (encerrada sem merge por interpretação incorreta).
- P0-A #1734: https://github.com/lucianox777/Jornada/issues/1734.
- CI PR #44867393618; E2E Three Waves PR #44867393709: SUCCESS; não substituem evidência master.
