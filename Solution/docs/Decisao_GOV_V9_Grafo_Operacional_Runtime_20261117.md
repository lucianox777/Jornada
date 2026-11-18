# GOV-V9-GRAFO-OPERACIONAL-RUNTIME-20261117 — integração diagnóstica

**Estado:** implementação proposta em PR, CI e ensaio físico pendentes; não certificada para produção. A decisão científica e o gate produtivo permanecem inalterados.

## Contexto e justificativa

A PR histórica #1654 propôs transportar o grafo operacional SQL para o replay congelado de coorte, mas permaneceu aberta após integrações posteriores (#1655, #1677, #1680). A auditoria da master de 18/11/2026 confirmou que o método de reconciliação/replay ainda não estava presente; é necessário reaplicar somente esse incremento sobre a master atual.

## Implementação e limites

A auditoria `V9OperationalCandidateGraphParityAudit` entrega as arestas operacionais ao `V9OperationalPinnedCohortRuntime.ReplayOperationalGraph`, com o grafo SQL físico como oráculo obrigatório de completude. IDs, partição TEST, gestores, arestas canônicas, duplicatas e igualdade de conjuntos são verificados antes do replay global. Divergência falha fechado, sem reparo, truncamento ou fallback.

Esta entrega é **diagnóstica**: não modifica o caminho produtivo `RunAsync`, não habilita `ProductionBatchPipelineCertified`, não publica resultados, não libera TEST científico nem altera TRAIN/AUDIT/VALIDATION ou o vencedor. A paridade física E2E e a certificação produtiva continuam pendentes.

## Aceite e rastreabilidade

Exigir CI, testes de contrato, smoke SQL descartável e evidência de casos negativos antes de integração. PR histórica #1654, auditoria [V9 Runner Beta](V9_Auditoria_Integracao_Runner_Beta_20261118.md), issues #1584 e #1488. GOV-DOC-01.
