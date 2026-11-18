# GOV-V9-E2E-PREFLIGHT-DIAGNOSTICO-20261118

## Contexto e justificativa
Após a integração da PR #1716, o handoff V9 de arquivo pinado abria conexão antes de rejeitar limites básicos inválidos da coorte e do tamanho de lote. Para preparar o E2E com falha fechada, o envelope deve ser rejeitado antes de qualquer acesso SQL.

## Decisão e precedência
Validar cancelamento, lote entre 1 e 128 e coorte sintética entre 2 e 128 observações antes de operationalSql.OpenAsync no método RehearseV9PinnedFileNonPublishingAsync. A auditoria CompareTestAsync mantém as verificações independentes de partição TEST, unicidade, grafo físico, modelo pinado e banco descartável JornadaHarness. Complementa o handoff #1716 e as restrições de runtime imutável.

## Impactos, limites e aceite
Falhas básicas deixam de abrir conexão SQL; teste estrutural comprova ordenação. Não altera TF4, scorer, Resolve, ciência, parâmetros, publicação, RunAsync produtivo ou certificação E2E. CI deve aprovar antes de merge. Persistência operacional e rollback continuam pendentes. GOV-DOC-01; referência #1716.
