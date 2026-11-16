# GOV-V9-RUNASYNC-PARIDADE-FISICA-LOTES-100-200-20261116

## Contexto
A issue #1584 exige evidência física, não apenas inspeção estrutural, da invariância de decisões do Runner V9 com candidato SQL real e vencedor TF4 congelado. O teste SQL existente usa 200 observações sintéticas, dois pares de candidatos positivos entre páginas, modelo pinado e lotes 100+100.

## Alteração técnica proposta
Estender `V9FullSchemaNonPublishingRunnerTests.Full_schema_RunAsync_200_two_batches_no_publication_and_second_batch_failure` para repetir o **verdadeiro** `ProbabilisticLinkageBatchRunner.RunAsync` sobre o mesmo universo descartável, vencedor imutável e grafo físico SQL, mas com lotes de 200 (uma página). Conferir hashes do grafo físico e `blocking_chave`, decisões persistidas **por observação** contra a referência congelada, reconciliação de 200 registros, status `CONCLUIDO_SEM_PUBLICACAO` e fingerprint de Gold/Serving inalterado. O teste integra o job SQL físico `full-schema-runasync-nonpublish` existente.

## Justificativa e impactos
Compara duas fronteiras reais de persistência SQL em vez de apenas simulação read-only. Custo adicional: uma execução sintética de 200 observações e um lote no SQL descartável. Não altera algoritmo, produção, workflow científico ou dados pessoais. Preserva o ensaio de falha injetada no segundo lote.

## Limites, precedência e rastreabilidade
A evidência é restrita a 200 registros sintéticos e dois pares TF positivos; não certifica todos os formatos TF4, abstention ou a integração de produção completa. Não liberar `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED`; não iniciar TRAIN nem TEST científico. GOV-DOC-01; issue #1584; PRs #1585–#1587; arquivo `Solution/tests/Jornada.Tests/Unit/V9FullSchemaNonPublishingRunnerTests.cs`; workflow `.github/workflows/v9-operational-model-sql-smoke.yml`. Estado: proposta em PR, aguardando execução e aprovação física.
