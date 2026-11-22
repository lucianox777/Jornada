# V9 — contrato verificável de paridade do Runner (proposta técnica, NÃO APROVADA)

Estado: proposta de implementação, sem decisão do PO. Issues: #1584 e #1575. Este documento **não** altera a política GOV-DOC-01, não autoriza TRAIN/VALIDATION/TEST e não substitui a decisão formal e atualização de `Solution/docs/Indice_Decisoes_Vigentes.md` após aprovação.

## Evidências na master

- `ProbabilisticLinkageBatchRunner.RunAsync` rejeita `ModelVersion=9` antes do pipeline com `RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED`.
- `IV9NonPublishingFrozenCohortScorer.PrepareAsync` já materializa todo o universo de `linkage_run_item` antes de `ScoreBatchAsync`; o scorer de fixture limita 512 observações e exige `ModelVersion=1`, exclusivamente para diagnóstico.
- `V9PinnedSyntheticGraphCohortRunScorer.PrepareAsync` lê gestores de Silver, verifica grafo físico contra `V9OperationalBlockingChaveCohortParity.VerifyAsync`, compara contagem/hash de arestas e faz agrupamento global antes de persistir. O modo físico tem fonte de arestas em `V9FrozenCohortSqlCandidateGraph.ReadAsync`, enquanto a consulta operacional é o verificador.
- `V9OperationalBlockingChaveCohortParity.VerifyAsync` consulta `BlockingProjectionCandidateQueryBuilder` contra `identidade.blocking_chave` e Gold diagnóstica, com manifesto integral e banco `JornadaHarness`; não certifica Gold produtiva nem TEST 100k.
- `V9ReadOnlyTestBatchRunner` e `V9OperationalCandidateGraphParityAudit` permanecem explicitamente não certificadores da pipeline completa.

## Contrato de aceite proposto (não implementado)

1. Entrada `TEST_ONLY` explicitamente autorizada e não publicável, vencedor V9 por `modelId + SHA256` com leitura byte-exata; rejeitar seleção `ATIVO`, fallback legado, refit e reutilização de execução.
2. Fonte primária de candidatos: consulta operacional `BlockingProjectionCandidateQueryBuilder` sobre universo congelado; o grafo físico independente deve ser apenas **oráculo de comparação**, nunca entrada externa não autenticada para declarar paridade.
3. Invariantes antes da primeira persistência: `DB_NAME() = JornadaHarness`, `Publish=false`, partição TEST, universo completo e ordenado, gestores válidos, ausência de CPF, SHA e projeção imutáveis, arestas sem truncamento, ausência de leitura de rótulos de verdade e dados TRAIN/VALIDATION.
4. Executar o **mesmo** `ProbabilisticLinkageBatchRunner.RunAsync` e persistir resultados somente no harness; preparar FS/complete-link globalmente antes de qualquer lote, preservando abstention e desempate.
5. Prova positiva E2E por observação: conjunto de candidatos, blocking, score, tie-break, decisão, abstention, grupos e hashes, em pelo menos dois tamanhos de lote; reconciliar contadores e registros SQL com oráculo independente.
6. Provas negativas: SHA/modelo divergente, banco errado, `Publish=true`, Gold/projeção obsoletas, duplicatas, aresta intra-gestor, páginas truncadas, mistura de partições, falha no meio do lote, acesso a dados científicos proibidos.
7. Limites 128/200/512 são apenas fixtures; comprovação de escala e custo 100k é etapa separada. Não elevar `ProductionBatchPipelineCertified` por equivalência de fixture.
8. Preservar os dois gates `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED` até implementação completa, CI obrigatória e aprovação de preflight do PO. Nenhuma execução de 324 hipóteses antes disso.

## Sequência de PRs sugerida

- PR A: contrato/testes negativos e separação explícita entre grafo operacional (fonte) e grafo físico (oráculo), sem modificar o workflow.
- PR B: integração de execução TEST_ONLY no RunAsync e provas E2E em SQL descartável, mantendo fail-closed.
- PR C: escala 100k, auditoria de isolamento, aprovação do PO, documentação GOV-DOC-01 e só então avaliação da liberação condicionada do workflow.

## Precedência e rastreabilidade

Este texto é uma especificação de trabalho, não uma decisão vigente. Em caso de conflito, prevalecem os bloqueios existentes, as decisões formais do repositório e os critérios de aceite de #1584/#1575. A aprovação arquitetural, caso ocorra, deverá ser registrada em documento de decisão e no índice oficial antes da integração da mudança correspondente.
