# GOV-V9-LEDGER-ROLLBACK-CANCELAMENTO-20261118

## Contexto
A persistência sintética V9 já usa uma transação serializable no banco descartável JornadaHarness, mas uma falha ao tentar rollback pode encobrir o erro que iniciou a falha. É necessário reforçar a rastreabilidade de falhas e o cancelamento antes do commit.

## Decisão e justificativa
Adicionar verificação explícita de cancelamento imediatamente antes do commit e preservar a exceção original caso o rollback falhe por SqlException ou InvalidOperationException. A falha de rollback não é considerada sucesso; a conexão pode estar interrompida e a confirmação do estado deve ser feita pelo banco. Adicionar testes estruturais da transação única, guard de banco descartável, rollback e ausência de publicação produtiva.

## Impactos, precedência e limites
Complementa #1716 e #1717, sem alterar o fluxo científico, TF4, parâmetros, modelo pinado ou RunAsync produtivo. Testes estruturais não substituem injeção real de falhas SQL nem comprovam rollback físico; esses testes E2E continuam pendentes. O gate RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED permanece obrigatório. Aprovação depende da CI e do merge desta PR. GOV-DOC-01.
