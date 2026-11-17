# GOV-V9-GRAFO-OPERACIONAL-RUNTIME-20261117 — PROPOSTA

## Decisão
O grafo SQL operacional reconstruído integralmente, com duas direções comprovadas por aresta, é entregue ao `V9OperationalPinnedCohortRuntime.ReplayOperationalGraph`. O runtime valida IDs, unicidade, canonicidade e igualdade exata com o grafo físico SQL independente antes de executar uma única vez o kernel congelado de coorte. Divergência falha fechado; não existe reparo de arestas, truncamento ou fallback para modelo legado.

## Escopo e limites
A mudança conecta a auditoria `V9OperationalCandidateGraphParityAudit` ao runtime científico de coorte. Preserva o vencedor por SHA, TF4/81, parâmetros, agrupamento e ausência de publicação. Não altera TRAIN, AUDIT, VALIDATION, seleção de modelo ATIVO, REPLAY legado, nem autoriza TEST científico 100k. A prova completa de `ProbabilisticLinkageBatchRunner.RunAsync` continua pendente nas issues #1584 e #1488. `ProductionBatchPipelineCertified=false` permanece obrigatório.

## Aceite
Executar teste de contrato, CI obrigatória e smoke SQL descartável `JornadaHarness`, com arestas atravessando lotes e casos negativos de aresta ausente, extra, duplicada e fora da coorte. Não declarar paridade física E2E até os resultados serem observados. GOV-DOC-01: sincronizar índice de decisões vigentes antes do merge.
