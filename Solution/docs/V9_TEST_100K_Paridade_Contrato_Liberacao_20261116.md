# V9 100k — contrato verificável de liberação do TEST independente

**Estado:** BLOQUEADO. Documento técnico de implementação, não certificação. Issue #1575. Data: 2026-11-16.

## Evidência do código atual

1. `.github/workflows/v9-v3-four-name-100k.yml`, job `prepare-test-100k`, interrompe o TEST antes de gerar a coorte com `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED`.
2. O job `physical-100k` também interrompe o TRAIN quando `test_required=true` e `test_database` está vazio. Retirar somente um dos guardas não libera a campanha.
3. `V9IndependentTestCli.RunAsync` consome o JSON vencedor SHA-pinned e o runtime `V9OperationalPinnedCohortRuntime.LoadPinnedAsync`, mas executa `V9PhysicalSqlAdaptiveCalibrationProbe.EvaluateFrozenTestAsync`, **não** `ProbabilisticLinkageBatchRunner.RunAsync`.
4. `SqlProbabilisticIdentityLinkage.LoadPinnedV9GroupedCohortRuntimeAsync` é um loader opt-in somente diagnóstico, explicitamente **não integrado** a `GetActiveModelAsync`, `ResolveWithoutCpfAsync` nem ao batch publicador. O caminho de produção corrente ainda usa `GetOrLoadRuntimeSnapshotAsync`, `LoadModelByIdAsync` e `LinkageRuleSetReader.TryLoadAsync`.
5. O relatório de TEST declara `productionRunnerFullPipelineParityCertified=false` e `independentlyCertifiedIid=false`. O teste `test_v9_test_production_model_format.py` exige os bloqueios atuais.

## Plano técnico com evidências exigidas

| Gate | Implementação | Evidência de aceite |
| --- | --- | --- |
| P1 — modelo | Adaptar a execução **não publicadora** do Runner para receber o vencedor V9 operacional SQL por SHA e versão, sem fallback legado ou ativação. | SQL e JSON byte-exatos, mesma carga TF4/FS, rejeição de SHA, versão ou schema divergentes. |
| P2 — processamento | Executar o mesmo planejamento de candidatos, blocking, regras, limites e resolução do `ProbabilisticLinkageBatchRunner.RunAsync` e `SqlProbabilisticIdentityLinkage`, com modo `MODEL_VALIDATION`, `Publish=false` e banco descartável. | Teste E2E comparando decisões por identidade e métricas, com golden cases positivos/negativos; sem escrita de publicação. |
| P3 — isolamento | TEST selado antes de TRAIN; chave privada disponível só no job TEST; abrir somente após VALIDATION e reserva one-use atômica. | Testes de tentativa de reabertura, artefato alterado, SHA errado e vazamento de dados. |
| P4 — workflow | Substituir os dois guardas `PARITY_NOT_IMPLEMENTED` por um preflight executável que comprove P1–P3 **antes** da geração e das 324 hipóteses. | CI verde, teste de contrato positivo e negativos, `test_required=true` sem desvio de segurança. |
| P5 — ciência | TEST recebe só vencedor final; não recalibra, não seleciona, não certifica IID sem evidência. | Relatório com `refitted=false`, `certified=false` até prova independente, rastreio de seed e SHA. |

**Critério de saída:** não é permitido remover os guardas de fail-closed enquanto P1–P4 não estiverem implementados e verificados. A persistência REF do corpus TRAIN da PR #1567 é trabalho relacionado, mas distinto; não substitui a prova de paridade.

## Governança

Solicitação do PO: liberar `test_required=true` **com execução científica válida**. Decisão operacional vigente: manter bloqueio até paridade comprovada; não é autorização para bypass. Rastreabilidade: GOV-DOC-01, issue #1575, workflow V9 100k, PR #1567 e decisão de corpus ZIP REF de 2026-11-15.

## Descoberta adicional: o Runner já tem um caminho experimental, limitado a 512 observações

Inspeção de `Solution/src/Jornada.Linkage.Runner/ProbabilisticLinkageBatchRunner.cs` (master em 2026-11-16): `RunAsync` já reconhece `IV9NonPublishingFrozenCohortScorer`, rejeita publicação, exige `MODEL_VALIDATION`, `ModelVersion=1`, `MaxRecords=null` e banco `JornadaHarness`. Entretanto, na materialização do universo, exige **2 a 512 observações** (`V9PinnedSyntheticGraphCohortRunScorer.MaximumSyntheticObservations`). Logo, **não** constitui execução de TEST 100k. A classe também oferece `RehearseV9NonPublishingSyntheticTestAsync` e `PersistV9SyntheticRehearsalLedgerAsync`, mas seus próprios comentários negam certificação de paridade produtiva. O método principal usa um caminho de score agrupado opt-in e ainda possui fluxos legados de SQL e publicação.

**Consequência arquitetural:** não ampliar cegamente o limite 512. Antes, caracterizar custos/memória e garantir equivalência de paginação, universe snapshot, blocking, decisões e ledger com 100k. Planejar teste de carga de 100k no runner descartável e provas de reconciliação por lote; manter o gate até evidência física. A restrição 512 e a ausência de paridade certificada são bloqueadores adicionais rastreáveis na issue #1575.
