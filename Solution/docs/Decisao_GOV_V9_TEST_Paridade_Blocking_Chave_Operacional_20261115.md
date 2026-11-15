# GOV-V9-TEST-PARIDADE-BLOCKING-CHAVE-OPERACIONAL-20261115

**Autoridade:** mandato do Product Owner de 15/11/2026 para continuar a camada TEST do Calibrador V9. **Política:** GOV-DOC-01. **Estado:** IMPLEMENTADO/INTEGRADO PR #1562, squash `9be74888d932bc888e4ae27be8683f518126ddec`; CI required-fast #43745392362 3/3 e SQL físico #43745392396 2/2 SUCCESS. **TEST científico 100k permanece BLOQUEADO.**

## Motivação e contexto

A PR #1560 implementou em SQL Server descartável o grafo físico positivo V9, reconstruído por assinaturas temporárias, e validou dois pares positivos TF4 cruzando os lotes 100+100 do verdadeiro RunAsync. O caminho produtivo de recuperação de candidatos, porém, utiliza a tabela identidade.blocking_chave e o QueryBuilder operacional de INTERSECT/UNION, filtros de projeção e normalização. Faltava comprovar sua igualdade exata com o grafo físico positivo antes de persistir decisões.

## Decisão de implementação

1. Acrescentar auditoria READ-ONLY V9OperationalBlockingChaveCohortParity.VerifyAsync, exclusiva de JornadaHarness com coorte pequena (2..512) e modelo TF4/81 congelado por SHA. O QueryBuilder de produção NÃO é alterado.
2. Antes de pontuar, exigir manifesto exaustivo dos UUIDs diagnósticos e de todas as chaves da coorte em identidade.blocking_chave: identidade da projeção SHA, schema, versão de normalização, vigência, semântica temporal, unicidade e cobertura exata contra V9FittedSnapshotExecutionRuntime.Project(). Rejeitar chaves extras e ausentes; não compensar com fallback ou TOP/truncamento.
3. Para cada fonte, executar sem modificação V9FittedSnapshotExecutionRuntime.BuildCandidateUuidQuery (que reusa BlockingProjectionCandidateQueryBuilder.BuildVerifiedSnapshotCandidateUuidQuery). Recusar UUID externo e candidato duplicado. Ignorar somente autoarestas e pares intragestor conforme contrato do grafo; exigir as duas direções físicas para toda aresta intergestor, comparar conjunto exato e SHA-256 das arestas com o censo SQL temporário.
4. O scorer opt-in só chama ReplayTest após a paridade completa entre dois SQL independentes, e o Runner só persiste o primeiro lote depois disso. Qualquer divergência falha fechada sem decisões gravadas.
5. Fixture físico em banco recém-criado JornadaHarness materializa 200 UUIDs diagnósticos e o contrato de projeção V9 na tabela operacional com FK real. Usa dois pares positivos (1,101) e (2,102), quatro arestas direcionadas, testes do Worker, confronta decisões SQL persistidas por ID com o replay congelado, verifica não publicação e introduz negativa de adulteração de fingerprint de uma chave. Nenhum dado real ou TEST selado.

## Limitações e precedência

Embora o caminho de consulta de blocking_chave seja operacional e real, o fixture semeia as chaves artificialmente. Isso NÃO prova a materialização produtiva desde Gold/Processor, nem uma certificação de modelo V9 nativo sem anchor legado RASCUNHO, Gold/Serving com conteúdo completo, SQL/CPU em escala 100k, idempotência/replay/LOCK/one-use ou desempenho P95. Nenhum guard V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED foi removido. As issues #1488 e #1478 seguem abertas. O score e TF4 congelado, 324 hipóteses, TRAIN/AUDIT/VALIDATION, thresholds, publicação e seleção do vencedor não são modificados.

Complementa GOV-V9-TEST-PREPARO-GLOBAL-COORTE-RUNASYNC-20261115 e GOV-V9-TEST-GRAFO-POSITIVO-SQL-TF4-CROSSBATCH-20261115, sem revogá-los. Próximo gate deve provar materialização operacional V9 correta, binding nativo sem pseudo-modelo legado, Gold/Serving não vazio e segurança científica integral antes de TEST 100k.

## Evidência exigida para integrar

- Código read-only: Solution/src/Jornada.Linkage.Runner/V9OperationalBlockingChaveCohortParity.cs
- Scorer: Solution/src/Jornada.Linkage.Runner/V9PinnedSyntheticGraphCohortRunScorer.cs
- SQL real E2E: Solution/tests/Jornada.Tests/Unit/V9FullSchemaNonPublishingRunnerTests.cs
- Contratos: Solution/tests/scripts/test_v9_fullschema_runasync_smoke.py
- CI SQL: .github/workflows/v9-operational-model-sql-smoke.yml
- Condição: required-fast 3/3 e dois jobs SQL físico 2/2 aprovados no HEAD da PR, merge e recibos sincronizados com índices GOV-DOC-01.

## Recibo pós-integração — 15/11/2026

- [PR #1562](https://github.com/lucianox777/Jornada/pull/1562), HEAD verificado `bd54e1ca8fbdd6e7098a8e3e4cc80cf52c9477b0`, squash na master `9be74888d932bc888e4ae27be8683f518126ddec`.
- [CI obrigatória #43745392362](https://github.com/lucianox777/Jornada/actions/runs/43745392362): jobs `unit-runtime`, `security-guards`, `deterministic-build` **3/3 SUCCESS**.
- [SQL físico #43745392396](https://github.com/lucianox777/Jornada/actions/runs/43745392396): `one-winner-sql-smoke` e `full-schema-runasync-nonpublish` **2/2 SUCCESS**. Log do NUnit: `V9_FULL_SCHEMA_OPERATIONAL_BLOCKING_CHAVE_PARITY_PASS: two positive bidirectional pairs across batches; 200 full manifests; tamper fail-closed; TF4 frozen; SQL Gold/Serving unchanged; scientific TEST 100k blocked`. **1/1 teste físico PASS** e **6/6 testes Python**. SQL Server descartável, schema e migração canônicos.
- [Preflight Gold #43745392355](https://github.com/lucianox777/Jornada/actions/runs/43745392355) **SUCCESS** no escopo disparado; não é prova de publicação/TEST independente.
- **Alcance exato:** verifica consulta efetiva operacional de `identidade.blocking_chave`, com `INTERSECT/UNION`, simetria e materialização de dados de fixture; mantém o seed manual limitado a `JornadaHarness`, isto é, **não valida a produção de chaves V9 a partir de Gold real** nem libera TEST científico 100k. Guard `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED` permanece. Issues #1488/#1478 não concluídas.
