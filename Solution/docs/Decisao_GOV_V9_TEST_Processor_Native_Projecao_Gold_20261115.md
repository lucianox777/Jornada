# GOV-V9-TEST-PROCESSOR-NATIVE-PROJECAO-GOLD-20261115

**Autoridade:** continuidade solicitada pelo Product Owner em 15/11/2026. **Política:** GOV-DOC-01. **Estado:** IMPLEMENTADO E INTEGRADO NA MASTER pela PR #1568, squash `8a02fa3995f83c48532d815be3309e3e0d5f7778`, required-fast #43749665252 3/3 e SQL físico #43749665254 2/2 SUCCESS. **TEST científico selado de 100 mil registros: BLOQUEADO e NÃO CONSUMIDO.**

## Contexto e lacuna verificada

As PRs #1562 e #1565 comprovaram, em SQL descartável, a consulta operacional de candidatos em `identidade.blocking_chave` e o caminho Gold sintético persistido -> `V9CalibrationBlockingProjection.Project` -> SQL -> `ProbabilisticLinkageBatchRunner.RunAsync` (TF4/81 e 200 observações em dois gestores). Contudo, a rotina materializadora V9 era isolada do componente `BlockingProjectionPersistence` que o Processor e o Runner usam para Gold e histórico Silver. O próximo gate requer compartilhar esse **leitor de Gold/histórico** sem ativar V9 indevidamente em produção ou retrocarimbar chaves legadas.

## Decisão técnica e justificativa

1. Tornar `BlockingProjectionPersistence` uma classe estática `partial` e adicionar modo explícito e **opt-in** à montagem de snapshots, mantendo por padrão o comportamento legado. No modo V9, nomes e nomes maternos (atuais e históricos) e nascimento são projetados pelo mesmo `V9CalibrationBlockingProjection.Project` dos Contracts, com prefixos `name_prefix_K` e `mother_name_prefix_K` versionados como aliases; as features transversais do catálogo legado não ingressam no manifesto V9. O caminho legado continua usando a projeção V2 e as suas regras históricas e atributos transversais, sem novos parâmetros obrigatórios.
2. Expor `BlockingProjectionPersistence.MaterializeNewPinnedV9SqlServerAsync`, no próprio componente usado pelo Processor, mas somente com chamada explícita, transação do chamador e banco **exatamente** `JornadaHarness`. Não registrar método na DI, não chamá-lo no ingestão normal, não ativar modelos e não alterar publicação, Gold/Serving ou `RefreshSqlServerAsync/RefreshSqlServerBatchAsync`.
3. Validar projeção de prefixos e fingerprint SHA esperados, Gold com CPF ausente e estado `REFERENCIA/COMPLETO`, pessoa `ATIVO`, ausência de qualquer índice prévio para UUID, limite duro de 2.048 chaves por referência e integridade temporal. Recusar agressivamente sem fallback e **não substituir/editar chaves LEGACY existentes**. Apenas inserir novas chaves com `projection_schema_version=V9_CALIBRATION_BLOCKING_PROJECTION_V1`, `projection_fingerprint_sha256` pinado, normalização e aliases históricos corretos. O próprio chamador controla COMMIT/ROLLBACK.
4. No teste físico completo, depois de semear 200 Gold sintéticos, executar este caminho Processor-native em transação **shadow com rollback obrigatório**: negativa de SHA errado; sucesso para uma referência com manifesto exato em `identidade.blocking_chave`; verificação de semântica VERSIONED_ALIAS e fingerprint; recusa à reentrada; chamada real ao `RefreshSqlServerAsync` legado em **outra referência** na mesma transação e confirmação da projeção V2; rollback e zero chaves residuais. A execução física separada de 200 Gold, paridade SQL bilateral, TF4/81, lotes 100+100 e não publicação segue em seguida.
5. Fazer disparar a CI SQL física quando o código de persistência muda, além dos checks e testes de contrato.

## Segurança, limites e impactos

O método novo é **um ponto de integração do componente operacional**, não uma alteração de comportamento do Processor em produção. Exige o mesmo SQL schema de projeção e uma transação existente e só roda em banco descartável. Ao contrário do caminho anterior, compartilha o leitor de Gold e histórico Silver com o Processor LEGACY e prova sua projeção distinta sem interferir na V2. O modo V9 não inclui atributos transversais externos ao esquema V9 congelado. **Não constitui certificação de publicação nem de alias histórico com vínculos reais de Silver**: o fixture atual não possui esses vínculos canônicos para os 200 UUIDs diagnósticos. A migração produtiva, o modelo V9 nativo sem anchor RASCUNHO, contagem de 100 mil, performance P95, autorização one-use/LOCK, replay/idempotência, invariância forte de Gold/Serving e autorização científica permanecem pendentes. Limites de TRAIN, 324 hipóteses, quatro TFs/81 células, FDR/recall e bloqueios científicos não são alterados.

**Precedência:** [GOV-V9-TEST-MATERIALIZACAO-GOLD-BACKED-BLOCKING-20261115](Decisao_GOV_V9_TEST_Materializacao_Gold_Backed_Blocking_20261115.md), [GOV-V9-TEST-PARIDADE-BLOCKING-CHAVE-OPERACIONAL-20261115](Decisao_GOV_V9_TEST_Paridade_Blocking_Chave_Operacional_20261115.md). Issues #1488/#1478 seguem abertas. Nenhuma aprovação de ativação produtiva é inferida da continuidade de desenvolvimento.

## Arquivos oficiais e critérios de aceite

- Contrato Gold/histórico compartilhado: `Solution/src/Jornada.Operational.Sql/BlockingProjectionPersistence.cs`
- Entrada Processor-native bloqueada: `Solution/src/Jornada.Operational.Sql/BlockingProjectionPersistence.V9.cs`
- Teste físico SQL completo: `Solution/tests/Jornada.Tests/Unit/V9FullSchemaNonPublishingRunnerTests.cs`
- Teste de regressão/contrato: `Solution/tests/scripts/test_v9_fullschema_runasync_smoke.py`
- Workflow: `.github/workflows/v9-operational-model-sql-smoke.yml`
- Aceite: CI `jornada-pr-required-fast` 3/3 e SQL `V9 lossless operational SQL model smoke` 2/2 no HEAD, prova shadow rollback V9+V2 e atualização de GOV-DOC-01/índices na `master`. Nunca remover guard `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED` neste PR.

## Recibo integrado — 15/11/2026

- [PR #1568](https://github.com/lucianox777/Jornada/pull/1568), HEAD do código efetivamente testado `cf92ada3dc031b5c1131af8df34b96bb4deb0605`, merge squash `8a02fa3995f83c48532d815be3309e3e0d5f7778` em master.
- [CI rápida #43749665252](https://github.com/lucianox777/Jornada/actions/runs/43749665252) **3/3 SUCCESS** (`security-guards`, `unit-runtime`, `deterministic-build`).
- [SQL físico #43749665254](https://github.com/lucianox777/Jornada/actions/runs/43749665254) **2/2 SUCCESS** (`one-winner-sql-smoke` e `full-schema-runasync-nonpublish`). Log do NUnit: `V9_PROCESSOR_NATIVE_V9_PROJECTION_SHADOW_PASS: Gold/history reader -> V9 SQL keys; legacy V2 remains unchanged; shadow rollback leaves no keys; TEST 100k blocked`. Testes Python de contrato **8/8 PASS**, NUnit de schema operacional **1/1 PASS**. Após o rollback do shadow, materialização separada de 200 Gold V9, grafo com dois pares positivos interlotes e ausência de publicação continuam aprovados.
- A primeira execução da PR #1568, [SQL #43749512131](https://github.com/lucianox777/Jornada/actions/runs/43749512131), falhou na compilação por três lambdas `static` que capturavam `Temporal`. A revisão substituiu as lambdas por capturas locais válidas, e o HEAD corrigido foi revalidado integralmente nos dois pipelines indicados.
- **Escopo protegido:** a produção **continua chamando `BlockingProjectionPersistence.RefreshSqlServerAsync/BatchAsync` legados**, sem registro do novo método V9 no Processor, sem seleção de modelo produtivo e sem qualquer mudança na regra de publicação. A prova de aliases históricos foi somente a reutilização do leitor, não um corpus com vínculos históricos efetivos. Gold/Serving produtivos, binding V9 nativo, LOCK/one-use e TEST independente 100k permanecem pendentes; guards científicos preservados. GOV-DOC-01.
