# GOV-V9-TEST-GRAFO-POSITIVO-SQL-TF4-CROSSBATCH-20261115

**Autoridade:** continuação do mandato do Product Owner, 15/11/2026, de implementar a camada TEST do Calibrador V9. **Estado:** em desenvolvimento/PR, CI e SQL físico a confirmar. **Política:** GOV-DOC-01. **TEST científico independente de 100k: BLOQUEADO.**

## Contexto e problema

A [PR #1558](https://github.com/lucianox777/Jornada/pull/1558) incorporou preparação da coorte integral ao verdadeiro ProbabilisticLinkageBatchRunner.RunAsync. O smoke com 200 observações e 100+100 lotes, entretanto, tinha **grafo explicitamente vazio**, fornecido pelo fixture. Por isso, não demonstrava recuperação positiva de candidatos, nenhum dos quatro TF com valores efetivamente correspondentes, nem travessia de limites de lote.

## Escopo incremental aprovado pelo mandato

- Introduzir no projeto Runner um **censo SQL físico descartável de assinaturas** V9, derivadas **somente** do plano congelado V9FittedSnapshotExecutionRuntime.Plan e do par (pass-id, sequência de valores), com SHA-256 calculado conforme a assinatura do Worker físico. Materializar em tabela temporária **local** no SQL Server e recuperar uma única união SQL DISTINCT de pares **entre gestores diferentes**; não entregar arestas calculadas antecipadamente pela fixture. Registrar digest SHA256 e contagem das arestas. O scorer global invoca o censo ANTES do primeiro lote e nunca retoca o modelo.
- Preservar limites duros de 512 fontes, 8.192 assinaturas e 25.000 incidências de pares; pré-contar combinações e recusar em vez de truncar. Permitir exclusivamente banco JornadaHarness descartável, sempre sem publicação e sem uso da coorte TEST selada. Para prova física, duas origens completas fictícias (gestores, sistemas, bases, entrega, lote, Silver) com 200 observações.
- Fixar pares positivos **(1,101)** e **(2,102)** entre lotes de 100 registros, com nomes construídos de maneira que somente esses dois pares coincidam nos passes de prefixo do vencedor V9. O primeiro par usa FULL/exact first (prenome TF de pessoa e mãe); o segundo exact last (sobrenome TF de pessoa e mãe). Os demais registros têm prefixos divergentes entre gestores e servem de controle negativo.
- Verificar dentro do verdadeiro RunAsync que o SQL retorna exatamente essas duas arestas, recalcula o agrupamento da coorte inteira, persiste 200 decisões por ID físico em linkage_resultado e reconcilia os campos com ReplayTest independente no mesmo fit V9 carregado por ID/SHA. Comparar também as arestas com o SQL do Worker físico para o subconjunto de 128 observações que contém os dois pares; **não alterar o limite de 128 do auditor diagnóstico já existente**.
- O esquema real tem FKs de linkage_resultado para identidade.pessoa. Sem alterar produção, o fixture cria **somente no banco SQL descartável** registros técnicos de referência com GUID diagnóstico e uma Gold fictícia já existente, mede a situação Gold/Serving antes e depois, e confirma que RunAsync com Publish=false não publica. Verificar a falha induzida na observação 101 e a ausência de resultados parciais do segundo lote.
- O caminho legado e os guards de TEST 100k permanecem intactos. Não alterar ciências/calibração, TF, hipótese 324, LOCK, seleção do vencedor, TRAIN, AUDIT, VALIDATION ou serviço produtivo/DI.

## Limite da prova e riscos remanescentes

Este censo é SQL **físico**, mas opera em tabelas temporárias com assinaturas calculadas em C# a partir do mesmo plano V9: **não é a recuperação produtiva** de candidatos por identidade.blocking_chave (BlockingProjectionCandidateQueryBuilder), nem constitui equivalência completa de candidato Gold. A correspondência com o Worker físico no subconjunto até 128 é uma prova de projeção/assinatura, não substitui o teste operacional completo. Gold do fixture começa com uma referência sintética não vazia, mas a publicação continua desligada e Serving permanece sem conteúdo. GUIDs diagnósticos artificiais não devem ser usados em identidade real. O contrato ainda depende de anchor RASCUNHO exclusivamente de fixture, e o limite de 512 impede uso com 100k.

**Pendências para liberar TEST científico:** candidato operacional por blocking_chave e Gold com paridade de candidatos e de agrupamento V9, suporte de modelo V9 lossless nativo sem anchor, cenários Gold/Serving não vazios e controlo de não publicação, execução global em escala sem limite diagnóstico, integridade de batches e falhas, replay/idempotência, LOCK/one-use, aprovação de segurança, validação estatística e recibo físico no SHA aprovado. Issues #1488/#1478 em aberto. Os guards V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED devem permanecer.

## Rastreabilidade e critérios de aceite

- Censo: Solution/src/Jornada.Linkage.Runner/V9FrozenCohortSqlCandidateGraph.cs
- Scorer opt-in: Solution/src/Jornada.Linkage.Runner/V9PinnedSyntheticGraphCohortRunScorer.cs
- E2E físico: Solution/tests/Jornada.Tests/Unit/V9FullSchemaNonPublishingRunnerTests.cs
- Testes de proteção: Solution/tests/scripts/test_v9_fullschema_runasync_smoke.py
- Pipeline: .github/workflows/v9-operational-model-sql-smoke.yml, job full-schema-runasync-nonpublish.
- Decisões precedentes: [Preparo global no RunAsync](Decisao_GOV_V9_TEST_Preparo_Global_Coorte_RunAsync_20261115.md), [RunAsync full-schema sem publicação](Decisao_GOV_V9_TEST_RunAsync_FullSchema_Sem_Publicacao_20261115.md). Não revoga limites científicos anteriores.
- Aceite: required-fast 3/3, SQL smoke físico completo 2/2, CI/SQL do HEAD da PR aprovados sem falso PASS; squash merge e atualização dos índices. Até comprovação, **não declarar IMPLEMENTADO NA MASTER**.
