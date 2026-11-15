# GOV-V9-TEST-MATERIALIZACAO-GOLD-BACKED-BLOCKING-20261115

**Governança:** GOV-DOC-01, continuidade expressa pelo Product Owner em 15/11/2026. **Estado:** implementação em PR, CI ainda a verificar. **TEST científico independente de 100k permanece BLOQUEADO**; não abrir/destravar nem consumir.

## Situação que motivou a decisão

A PR #1562 demonstrou a paridade exata das consultas produtivas de candidatos no SQL Server com a tabela identidade.blocking_chave, usando o modelo V9 TF4/81 congelado, o grafo físico de assinaturas e dois pares intergestor cruzando lotes 100+100. Entretanto a tabela de blocking era populada diretamente pelo fixture, com valores projetados das observações Silver, e não a partir de linhas reais Gold. Assim a recuperação SQL estava provada, mas a origem materializada dos valores não.

## Decisão incremental e implementação

- Criar uma **rotina opt-in, não produtiva e restrita a JornadaHarness** dentro do módulo real `Jornada.Operational.Sql`, denominada `V9DisposableGoldBlockingProjectionMaterializer`. Não alterar nem registrar esta rotina na DI do Processor; não mudar `BlockingProjectionPersistence.RefreshSqlServerAsync/BatchAsync` nem o contrato LEGACY; não executar em qualquer base real. Somente SQL Server físico descartável, Gold sintético completo e habilitação explícita pelo teste.
- O fixture passa a persistir fisicamente **200 referências Gold**, cada uma vinculada à identidade diagnóstica e com os nomes, nomes da mãe e data de nascimento correspondentes às observações Silver. **O fixture não pode inserir diretamente chaves em identidade.blocking_chave.**
- A rotina aceita conjunto único exato de 2..512 UUIDs Gold, versão de prefixo e fingerprint SHA do plano V9 congelado. Valida nome de banco, existência exata de referências `identidade.pessoa` e `gold.pessoa`, inexistência de chaves anteriores, formato íntegro de Gold (REFERENCIA, SEM_CPF, COMPLETO), limite 16.384 chaves e ausência de dados extras. Toda a leitura é set-based via tabela temporária de IDs e `JOIN gold.pessoa` físico, com locks na admissão.
- Projetar com **o mesmo `V9CalibrationBlockingProjection.Project` oficial dos Contracts**; gravar versão de normalização, semântica temporal `STABLE_IDENTITY_DATUM`/ `VERSIONED_ALIAS`, dados completos, `projection_schema_version` e `projection_fingerprint_sha256`, usando `SqlBulkCopy` para tempdb e um `INSERT ... SELECT` na **tabela operacional real**. A transação é atômica; a ausência de uma referência, Gold inválido, tentativa de segunda materialização ou divergência de SHA deve falhar fechada sem nenhuma chave parcial.
- A trilha RUNASYNC TEST_ONLY aprovada anteriormente continua intacta e, em seguida, reconcilia todas as chaves produzidas a partir de Gold contra o plano de projeção derivado do Silver congelado; compara o grafo por SQL de assinaturas e consulta operacional `blocking_chave`, duas direções para dois pares positivos, replay TF4/81, persistência em lotes 100+100 e ausência de publicação. A prova **não usa truth labels como entrada do scorer**.
- Testes negativos novos: alterar uma referência Gold para `INDEFINIDA`, tentar materializar, exigir zero linhas no blocking e rollback, restaurar, materializar, exigir SHA/cobertura e negar reentrada. O bloqueio científico `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED` deve permanecer; o TEST 100k selado não é aberto.

## Limites e precedência

Este resultado demonstra **Gold persistido → projeção oficial V9 → materialização física de blocking_chave → consulta operacional → coorte RunAsync**, em ambiente SQL descartável. A rotina de projeção é **um adaptador diagnóstico opt-in**, não o caminho produtivo do Processor; não autentica aliases históricos de Silver vinculados a Gold nem atributos versionados/temporais reais. Não comprova identidade de publicação de Gold/Serving, nem desempenho 100k, regra de modelo V9 nativo no FK sem anchor de fixture, LOCK/one-use científico, end-to-end com dados reais, estatística FDR/recall ou latência P95. Em produção, o método LEGACY ainda não escreve prefixos V9, e não deve fazê-lo sem governança de esquema e migração própria. Nenhuma decisão anterior de calibração, 324 hipóteses, TF4, TRAIN/VALIDATION/AUDIT, thresholds e orçamento de risco foi alterada.

Precedência: [GOV-V9-TEST-PARIDADE-BLOCKING-CHAVE-OPERACIONAL-20261115](Decisao_GOV_V9_TEST_Paridade_Blocking_Chave_Operacional_20261115.md), [GOV-V9-TEST-GRAFO-POSITIVO-SQL-TF4-CROSSBATCH-20261115](Decisao_GOV_V9_TEST_Grafo_Positivo_SQL_TF4_Crossbatch_20261115.md). Issues #1488/#1478 abertas. Próximos gates: materialização governada em Processor usando contrato V9 pinado, Gold/Serving não vazios com hashes antes/depois e não publicação, binding nativo sem anchor, idempotência, proteção one-use, escalabilidade e ciência 100k independente.

## Rastreabilidade e critério de aceite

- Código: `Solution/src/Jornada.Operational.Sql/V9DisposableGoldBlockingProjectionMaterializer.cs`.
- Teste físico: `Solution/tests/Jornada.Tests/Unit/V9FullSchemaNonPublishingRunnerTests.cs`.
- Contrato preventivo: `Solution/tests/scripts/test_v9_fullschema_runasync_smoke.py`.
- CI SQL: `.github/workflows/v9-operational-model-sql-smoke.yml`.
- Aceite de integração: required-fast 3/3 e SQL físico 2/2 no HEAD, testes negativos atômicos e materiais do Gold, merge squash na master, recibo e dois índices oficiais GOV-DOC-01 sincronizados. Não declarar TEST 100k liberado.
