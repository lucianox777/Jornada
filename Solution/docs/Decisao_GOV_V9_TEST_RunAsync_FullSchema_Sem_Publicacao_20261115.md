# GOV-V9-TEST-RUNASYNC-FULL-SCHEMA-SEM-PUBLICACAO-20261115

**Autoridade/contexto:** Product Owner, 15/11/2026: “Implementar a implementação do que falta para testar, para executar a camada teste do calibrador.” Avanço incremental nas issues #1488 e #1478, conforme GOV-DOC-01. **Estado:** IMPLEMENTADO EM BRANCH; CI e execução física pendentes de comprovação; **TEST científico 100k NÃO HABILITADO**.

## Problema e justificativa

A master de 15/11 já possui conversor SQL sem perdas do vencedor V9 TF4/81 células, runtime V9 de coorte agrupada, prova de candidatos em SQL e ledger sintético. Ainda faltava testar o método real **ProbabilisticLinkageBatchRunner.RunAsync** com schema operacional integral, e não somente o ledger. Essa é uma das lacunas para paridade TEST/produção.

## Escopo do incremento autorizado

1. Criar teste de integração SQL físico **opt-in**, utilizando 200 observações totalmente fictícias em um banco novo JornadaHarness, isolado de JornadaLocal, HML, PROD e da coorte TEST selada. Aplicar o SQLCMD operacional completo Jornada_Fase1_v3.70.sql, migration V9 TEST_ONLY e preflight estrutural; recusar banco diferente, tabela ausente e corpus anterior.
2. Persistir o modelo V9 de **quatro TF e 81 células** exclusivamente na tabela operacional V9 e recuperá-lo por SHA pelo loader já existente. Para satisfazer a FK atual de linkage_run.modelo_id, criar apenas no banco descartável um metadata anchor RASCUNHO com o mesmo modelId e SHA. **Não** mapear parâmetros V9 parcialmente para tabelas legadas, alterar status ou ativar esse modelo. O scorer injetado usa integralmente o modelo V9 pinado.
3. Exercitar o verdadeiro método RunAsync em MODEL_VALIDATION, Publish=false, com materialização real em Silver, 200 observações, dois lotes de 100, persistência de linkage_run e linkage_resultado, status CONCLUIDO_SEM_PUBLICACAO e invariância física de Gold, Serving e v_vinculo_corrente no fixture sem dados publicados.
4. Ensaiar falha na **101ª avaliação**: primeiro lote deve permanecer commitado, segundo lote não deve ser parcialmente persistido, estado deve ser FALHOU e Gold/Serving/vínculo não podem refletir run incompleto. Não declarar rollback global, pois o Runner persiste cada lote em sua própria transação.
5. Adicionar workflow independente, SQL Server 2022 por digest, senha aleatória mascarada, DDL canônico, build Release com warnings-as-errors, NUnit e recibo sanitizado sem dados de pessoas, segredos ou bytes de modelos.
6. Preservar integralmente a cadeia TRAIN→AUDIT→VALIDATION, 324 hipóteses, seleção, TF4, parâmetros científicos, FDR/CP/P95, segurança, TEST one-use, DT-15 e os dois guards V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED do workflow TF4. **Não executar nem desbloquear TEST científico agora.**

## Limites desta evidência

O fixture inicial de 200 observações não possui referências Gold nem pares positivos. Assim, testa o caminho operacional de materialização, batch, persistência, falha e não publicação; **não demonstra** paridade positiva de TF4, ranking, Resolve, candidatos, agrupamentos ou Gold/Serving não vazio. A interface de modelo V9 é injetada somente para o smoke. O metadata anchor RASCUNHO atende apenas à FK no banco descartável e **não representa integração produtiva do loader V9**. Essa implementação não é certificação full-prod, não autoriza consumir o TEST independente one-use e não prova execução 100k.

**Próximos gates obrigatórios:** binding V9 lossless na execução produtiva sem pseudo-modelo legado, avaliação de TF0/TF4 e casos positivos/adversariais, comparação exata por observação/candidato/grafo/cluster, paridade de execução com RunAsync efetivo em bancos descartáveis, invariância Gold/Serving também com tabelas não vazias, falhas/replay/idempotência, segurança/LOCK e uso único do TEST, seguido de prova física e CI na master. Só após todos os gates será possível retirar o bloqueio deliberado de test_required=true para TEST sintético.

## Rastreabilidade e precedência

- Teste NUnit: Solution/tests/Jornada.Tests/Unit/V9FullSchemaNonPublishingRunnerTests.cs. Reusa a fixture de vencedor TF4/81 já existente.
- Contratos de segurança: Solution/tests/scripts/test_v9_fullschema_runasync_smoke.py.
- Workflow físico: .github/workflows/v9-test-fullschema-runasync-smoke.yml.
- Complementa GOV-V9-TEST-REAL-SCHEMA-RUNASYNC-GOLD-SERVING-20261114 (Decisao_GOV_V9_TEST_Esquema_Operacional_RunAsync_Gold_Serving_20261114.md), GOV-V9-TEST-RUNNER-COORTE-COMPARTILHADA-20261114 (Decisao_GOV_V9_TEST_Execucao_Compartilhada_Runner_Coorte_20261114.md) e GOV-V9-TEST-PARIDADE-FORMATO-OPERACIONAL-20261113 (Decisao_GOV_V9_TEST_Paridade_Formato_Operacional_20261113.md), sem revogação de qualquer gate final.
- Critérios de aceite: CI obrigatória security-guards/unit-runtime/deterministic-build 3/3 no HEAD, SQL smoke full-schema físico PASS e PR por squash, sem publicar nem consumir TEST. Atualizar recibo documental com resultados reais; não presumir sucesso de execução.
