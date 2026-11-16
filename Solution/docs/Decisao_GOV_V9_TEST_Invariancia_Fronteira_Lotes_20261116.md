# GOV-V9-TEST-INVARIANCIA-FRONTEIRA-LOTES-20261116

## Contexto e justificativa
A issue #1584 exige evidência executável de paridade operacional do TEST científico V9. O ensaio de SQL descartável já reconstrói o grafo global antes de decidir identidades, mas uma única paginação não detecta dependência indevida das fronteiras dos lotes.

## Decisão operacional implementada
O ensaio opt-in `V9ReadOnlyTestBatchRunner.RunAsync` executa a auditoria física duas vezes, com o tamanho solicitado e com tamanho alternativo (1, ou 2 quando o solicitado é 1). Exige igualdade de SHA do vencedor, esquema e fingerprint da projeção, hash canônico de arestas, hashes de decisões e grupos, contadores de resultado e ausência de certificação produtiva. Divergência interrompe a execução em modo fail-closed.

## Impactos, limites e precedência
A segunda leitura SQL aumenta o custo do ensaio de coorte pequena, sem alterar dados ou publicar identidades. Mantém a restrição a `JornadaHarness`, TEST sintético, vencedor pinado e limite de observações. Esta prova **não executa** o `ProbabilisticLinkageBatchRunner.RunAsync` produtivo e **não certifica** paridade de publicação/produção. Não libera TRAIN, TEST científico, ativação, Gold ou Serving. Preserva precedência de GOV-DOC-01 e dos gates V9 existentes; nenhuma exceção ao bloqueio `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED`.

## Rastreabilidade e verificação
Issue #1584; PR de implementação a registrar; método `Solution/src/Jornada.Linkage.Runner/V9ReadOnlyTestBatchRunner.cs`. Estado: proposta implementada em branch, sujeita a CI, revisão e integração; não considerar decisão vigente em master antes da integração.
