# DT-17 — matriz transversal de regressão (baseline reconciliada)

**Base reconciliada:** `master` em `73bc53af0d427239fd6ec84cc820add8b22313b5` (29/09/2026). **Issue:** [#583](https://github.com/lucianox777/Jornada/issues/583). **Norma vigente:** [Decisões canônicas de identidade e linkage — 29/09/2026](Decisoes_Canonicas_Identidade_Linkage_20260929.md). Este arquivo é inventário de provas e execuções; não promove modelos e não transforma existência de teste em execução comprovada.

O teste `Dt17RegressionMatrixContractTests` valida que as provas citadas continuam presentes. Os testes novos `Dt17IdentityCompositionRegressionTests` e `Dt17NonScorerInvariantContractTests` explicitam os invariantes desta frente que não dependem do scorer. **Skipped não conta como prova.** Uma linha só passa a **EXECUTADA** quando houver SHA do HEAD efetivamente testado e job concluído; até lá permanece **NÃO EXECUTADA**.

## Reconciliação da baseline de 28/09

A seção histórica “IBGE obrigatório, sem fallback”, integrada em 28/09 pelo PR #595, foi superada em 29/09 por DC-LK-01. Ela permanece registrada abaixo para rastreabilidade, mas não é contrato vigente: a referência IBGE é bootstrap inicial versionado e persistido; depois do primeiro bootstrap, `GENERATE_DRAFT` não deve exigir carga/revalidação de IBGE ativo em toda execução e não pode usar fallback oculto na carga inicial.

As provas V6 de empate/threshold e a regressão V8 de ausência materna também permanecem como histórico, sem serem usadas como aceite desta frente. DC-LK-01 tornou V8 o contrato executável futuro e a branch `feat/v8-tf-guard-budget-20260929` altera TF/guard/resultados esperados. Homônimos, empates e guard demográfico estão fora do escopo desta DT-17.

Na ETAPA 0 não foi localizada prova citada inexistente entre os marcadores controlados por `Dt17RegressionMatrixContractTests`. Foram identificadas lacunas de 29/09 sem linha transversal: divisão literal DC-ID-02, NULL na observação versus obrigatoriedade do contrato, proibição de `initial_uuid` como feature, CPF válido/inválido/conflitante por superfícies existentes e segurança 401/403/escopo.

## Matriz vigente — frente sem scorer

| Invariante | Prova técnica | Estado de execução |
|---|---|---|
| DC-ID-02 — divisão sem âncora: A vira histórico e as duas pessoas recebem destinos canônicos novos/admitidos, sem prêmio por continuidade | [`Dt17IdentityCompositionRegressionTests.DcId02_unanchored_decided_split_requires_two_new_canonical_uuids_and_makes_old_reference_historical`](../tests/Jornada.Tests/Unit/Dt17IdentityCompositionRegressionTests.cs); reforça [`IdentityCompositionTests.Fully_decided_unanchored_split_requires_new_or_already_admitted_destinations`](../tests/Jornada.Tests/Unit/IdentityCompositionTests.cs) | **EXECUTADA** — SHA `0f43d8ccfe89484be2b8871ddea650b888598006`, jornada-ci #36647809207, job `unit`: success. |
| DC-ID-02 — com âncora CPF, A permanece com o titular; a outra pessoa recebe outro destino | [`Dt17IdentityCompositionRegressionTests.DcId02_cpf_anchor_keeps_holder_on_old_uuid_and_moves_only_other_person`](../tests/Jornada.Tests/Unit/Dt17IdentityCompositionRegressionTests.cs); [`IdentityCompositionTests.Cpf_anchored_split_preserves_anchor_and_allocates_other_destination`](../tests/Jornada.Tests/Unit/IdentityCompositionTests.cs) | **EXECUTADA** — SHA `0f43d8ccfe89484be2b8871ddea650b888598006`, jornada-ci #36647809207, job `unit`: success. |
| DC-ID-01/DC-ID-02 — `initial_uuid` isolado não é promovido a canônico | [`Dt17IdentityCompositionRegressionTests.DcId02_bare_initial_uuid_is_not_promoted_to_canonical_destination`](../tests/Jornada.Tests/Unit/Dt17IdentityCompositionRegressionTests.cs); [`IdentityCompositionTests.Reusing_a_historical_alias_or_someone_elses_initial_uuid_is_rejected`](../tests/Jornada.Tests/Unit/IdentityCompositionTests.cs) | **EXECUTADA** — SHA `0f43d8ccfe89484be2b8871ddea650b888598006`, jornada-ci #36647809207, job `unit`: success. |
| `initial_uuid` é proveniência, nunca feature de blocking/score | [`Dt17NonScorerInvariantContractTests.Initial_uuid_is_not_a_blocking_or_scoring_feature`](../tests/Jornada.Tests/Unit/Dt17NonScorerInvariantContractTests.cs) protege `FellegiSunterScoring.cs` e `BlockingProjectionCandidateQueryBuilder.cs` contra uso de `InitialUuid` | **EXECUTADA** — E2E-C HEAD `e51876b175ad212344033fd08d7f32687b2ed323`, `jornada-ci` #36950535828, job `unit`: success; não altera scorer. |
| Atributos do núcleo podem ser NULL na observação; obrigatoriedade pertence ao contrato da Secretaria/remessa | [`Dt17NonScorerInvariantContractTests.Observation_core_identity_attributes_are_nullable_while_secretariat_contract_may_require_them`](../tests/Jornada.Tests/Unit/Dt17NonScorerInvariantContractTests.cs) confere nulabilidade Silver e preserva contratos que declaram `required` | **EXECUTADA** — SHA `0f43d8ccfe89484be2b8871ddea650b888598006`, jornada-ci #36647809207, job `unit`: success. |
| CPF válido/inválido/conflitante — regras e superfícies de resolução/correção existentes | [`CpfRulesTests.Normalizes_valid_cpf`](../tests/Jornada.Tests/Unit/CpfRulesTests.cs), `Rejects_invalid_cpf`; [`CpfAnchorResolutionApiTests.Resolver_classifies_structurally_invalid_cpf_without_uuid`](../tests/Jornada.Integration.Tests/Integration/CpfAnchorResolutionApiTests.cs), `Resolver_uses_permanent_anchor_when_current_map_is_closed_or_in_conflict`; [`CpfAnchorGovernedCorrectionTests.Governed_correction_cannot_transfer_permanent_cpf_anchor`](../tests/Jornada.Integration.Tests/Integration/CpfAnchorGovernedCorrectionTests.cs); inventário protegido por [`Dt17NonScorerInvariantContractTests.Cpf_regressions_cover_valid_invalid_and_conflict_evidence_without_claiming_route_execution`](../tests/Jornada.Tests/Unit/Dt17NonScorerInvariantContractTests.cs) | **EXECUTADA** — SHA `0f43d8ccfe89484be2b8871ddea650b888598006`, jornada-ci #36647809207: `unit` e `integration-sql` success. A inspeção de marcadores continua não substituindo a execução. |
| Segurança 401/403 e escopo nas rotas de identidade existentes | [`ProgressiveOriginApiTests.Origin_route_fails_closed_and_returns_only_owner_snapshot`](../tests/Jornada.Tests/Unit/ProgressiveOriginApiTests.cs); [`SemiblindIdentityHttpTests.Route_enforces_access_and_pre_response_audit`](../tests/Jornada.Tests/Unit/SemiblindIdentityHttpTests.cs); [`AuthorizationMatrixContractTests.Route_matrix_is_unique_and_type_credentials_are_limited_to_explicit_routes`](../tests/Jornada.Tests/Unit/AuthorizationMatrixContractTests.cs); inventário protegido por [`Dt17NonScorerInvariantContractTests.Existing_identity_routes_keep_401_403_and_scope_regressions`](../tests/Jornada.Tests/Unit/Dt17NonScorerInvariantContractTests.cs) | **EXECUTADA** — SHA `0f43d8ccfe89484be2b8871ddea650b888598006`, jornada-ci #36647809207, job `unit`: success; #539/#378 continuam limites institucionais distintos. |
| Identidade progressiva e reavaliação | [`ProgressiveIdentityTests.InitialUuidIsAllocatedBeforeResolutionWithoutInventingAnAssociation`](../tests/Jornada.Tests/Unit/ProgressiveIdentityTests.cs), `AmbiguityKeepsTheReferenceWithoutChoosingAnyCandidate`; [`IdentityReplayInvariantTests.Identity_graph_global_invariants_hold_after_seed_and_recomposition_replay`](../tests/Jornada.Integration.Tests/Integration/IdentityReplayInvariantTests.cs) | **EXECUTADA** — SHA `0f43d8ccfe89484be2b8871ddea650b888598006`, jornada-ci #36647809207: `unit` e `integration-sql` success. |
| Ledger semântico/idempotência | [`Dt05SemanticTransitionContractTests.V1_signature_field_order_and_documentation_are_frozen`](../tests/Jornada.Tests/Unit/Dt05SemanticTransitionContractTests.cs); [`Dt05SemanticThreeWavesSqlServerTests.Four_waves_ignore_unchanged_and_score_only_retries_without_losing_raw_results`](../tests/Jornada.Integration.Tests/Integration/Dt05SemanticThreeWavesSqlServerTests.cs); [`Dt05PublicationGuardsSqlServerTests.Ledger_requires_transaction_and_executing_run_without_side_effects`](../tests/Jornada.Integration.Tests/Integration/Dt05PublicationGuardsSqlServerTests.cs) | **EXECUTADA** — SHA `0f43d8ccfe89484be2b8871ddea650b888598006`, jornada-ci #36647809207: `unit` e `integration-sql` success; replay Parquet/NAS permanece fora do escopo. |

## Evidência histórica preservada — não é aceite vigente desta frente

- **IBGE obrigatório, sem fallback (28/09):** PR #595, merge `911f8699c021cf3b7a846d8e397e543ee68a24be`, jornada-ci #36496723272. Foi regra vigente naquele corte e está **SUPERADA por DC-LK-01**; não apagar a evidência histórica.
- **V6 empate/threshold:** [`ProbabilisticV6TieOrderingTests.Exact_log_odds_tie_is_conflict_and_uuid_only_stabilizes_display_order`](../tests/Jornada.Tests/Unit/ProbabilisticV6TieOrderingTests.cs) e [`LinkageDecisionBoundaryTests.V6_threshold_is_inclusive_at_exact_best_score_and_rejects_immediately_above`](../tests/Jornada.Tests/Unit/LinkageDecisionBoundaryTests.cs) permanecem rastreáveis, mas não são critério da V8.
- **V8 ausência materna (28/09):** `ProbabilisticV8NeutralMissingContractTests.V8_maternal_absence_is_neutral_for_unilateral_and_bilateral_missingness` permanece histórica enquanto TF/guard são alterados na frente V8.
- **CPF tardio três ondas:** [`dt05-cpf-late-wave.ps1`](../scripts/dt05-cpf-late-wave.ps1), marcador `DT05_CPF_LATE_REAL_RUNNER_E2E`, teve prova histórica no PR #540. Não foi reexecutado por esta frente e não deve ser contabilizado como execução atual.

## Fora do escopo e lacunas preservadas

- **LACUNA: MARIA SOUZA / MARIA SOUZA LIMA / MARIA LIMA.** Homônimos/empates/guard pertencem à frente V8; não reinterpretar resultados aqui.
- **LACUNA: DT-05 replay histórico NAS.** Replay Parquet/NAS permanece fora desta frente.
- **DT-10 RESOLVIDA (01/10/2026):** PR #664, `jornada-ci` #36858619309 e artefato `dt10-evidence` registram 4/4 testes sem skips para concorrência adversarial, conflito concorrente, rollback por falha injetada e plano/volumetria em `JornadaSyntheticDev`. A evidência integra a preparação do baseline E2E-A, sem alegar SLA de Produção.
- **LACUNA: #539 / #378 compartilhamento institucional.** CI DEV não autoriza exposição institucional.
- **LACUNA: #506 / #31 validação externa e representatividade.** CI sintético não substitui validação externa/real.
- **LACUNA: Ensaio integrado DT-17.** Esta matriz não declara aceite integral de Ensaio/HML/Produção.
- **UUID incorporado:** estado sem nome/contrato; fora do escopo.

## Execução e ambiente

Execução registrada antes da atualização documental final: SHA `0f43d8ccfe89484be2b8871ddea650b888598006`, jornada-ci #36647809207; `dependency-lock`, `unit`, `deterministic-build`, `security-analysis`, `integration-sql`, `harness-smoke`, `ddl-upgrade` e `e2e` concluíram `success`. `bronze-restore-drill`, `scale-harness`, `release-promotion`, `rc-evidence` e `rc-publish` ficaram `skipped` e permanecem **NÃO EXECUTADOS**. A atualização documental final exige nova rodada no HEAD final antes do merge. Não alterar workflows para esta DT.

Testes SQL desta frente usam somente o banco sintético/isolado configurado pelo CI. **Nunca resetar `JornadaLocal`**. O roteiro DEV destrutivo continua restrito a `JornadaSyntheticDev`.

## DP-01

**Arquivos declarados:** `Solution/docs/DT17_Matriz_Regressao.md`, `Solution/tests/Jornada.Tests/Unit/Dt17RegressionMatrixContractTests.cs`, `Solution/tests/Jornada.Tests/Unit/Dt17IdentityCompositionRegressionTests.cs` e `Solution/tests/Jornada.Tests/Unit/Dt17NonScorerInvariantContractTests.cs`.

**Contratos/SQL afetados:** nenhum contrato, DDL, migration ou SQL de produção alterado; testes apenas leem contratos/DDL existentes. **Dependências:** DC-ID-01/DC-ID-02 e DC-LK-01 de 29/09. **Testes/gates:** os oito jobs do jornada-ci, contabilizados somente se executados. **Riscos:** testes de contrato estrutural podem detectar mudança legítima futura e exigir reconciliação documental; não corrigir produção nesta frente. **Rollback:** reverter documentação e arquivos de teste novos/ajustados.

A separação da CI está excluída desta frente. A DT-10, que era a última entrega técnica antes do baseline, foi concluída em 01/10/2026. **E2E-A/DT-17A foi EXECUTADA** no HEAD `406732a4351313d0e411a010f370073e8a4ce3c4`, com `jornada-ci` #36866260882 e DT-05 E2E #36866260886 verdes; ver [registro de execução](evidence/E2E_A_Execution_20261001.md). **E2E-B/DT-17B foi EXECUTADA** no HEAD `1f647053c186834998b4526b9fb33b09df033c0a`, com `jornada-ci` #36886632283 e DT-05 E2E #36886632424 verdes; ver [registro de execução](evidence/E2E_B_Execution_20261001.md). **E2E-C/DT-17C foi EXECUTADA** no HEAD `e51876b175ad212344033fd08d7f32687b2ed323`, com `jornada-ci` #36950535828 e DT-05 E2E #36950535827 verdes após a DT-12; ver [registro de execução](evidence/E2E_C_Execution_20261001.md). A comparação A × B × C não detectou regressão coberta por esses gates após a limpeza; lacunas externas/institucionais permanecem explicitamente preservadas.

## Matriz complementar DT-10/V9 — 28/10/2026 (PR #1199, não integrada)

| Caso | Prova exigida | Estado neste adendo |
| --- | --- | --- |
| `SEM_CANDIDATO_%` com melhor candidato persistido | `THROW 51835`, 0 evento, origem `PROVISORIA`, falha idem em replay | Preflight LocalDB #40805124598 SUCCESS no SHA `3017f048` (1 teste físico) |
| `SEM_CANDIDATO_%` com segundo candidato persistido | Mesma recusa e atomicidade, sem promover `initial_uuid` | Preflight LocalDB #40805124598 SUCCESS no SHA `3017f048` (1 teste físico) |
| Ausência legítima de candidatos | Um único evento, mesmo UUID e nenhum evento extra em replay | Preflight LocalDB #40805124598 SUCCESS no SHA `3017f048` (1 teste físico) |
| Lote misto com ausência legítima e ausência forjada com candidato | Rejeitar o lote inteiro com `51835`, zero eventos de ambas as origens, ambas `PROVISORIA`, retry igualmente negado | Novo cenário de atomicidade na branch; **CI do novo SHA ainda não executada** |
| `LIMIAR_INFERIOR_V9_GOVERNADO` | Melhor candidato real, score físico abaixo de piso bootstrap, política por digest DT-15 e snapshot corretos | Guardas SQL em branch; **teste positivo governado pendente** |
| Faixa intermediária, políticas/fit alterados, origem sintética | Abstenção ou recusa; nenhum ledger parcial; rollback comprovado | **Matriz física completa pendente** |
| Calibrador C# único, loader REF, TEST final | Mesmo fit, proveniência, separação TEST/DT-15 e ausência de ativação automática | **Pendente (#1089 / gates institucionais)** |

Esta matriz é **adendo da candidata em PR**, não revisão retroativa dos E2E históricos nem certificação operacional. Registros reais de CI/artefatos só serão atribuídos após execução dos jobs no SHA exato. A auditoria de risco SEM_CANDIDATO era hipótese estática até sua reprodução física; não alegar exploração comprovada.

## Adendo de rastreabilidade — 28/10/2026, correção de preflight e vínculo estrutural

Evidência CI no SHA `b79e167`: `jornada-ci`, inventário e instalador SUCCESS; preflight Windows [40659908775](https://github.com/lucianox777/Jornada/actions/runs/40659908775) FAILURE por assinatura antiga no patch TRAIN; schema [40659908716](https://github.com/lucianox777/Jornada/actions/runs/40659908716) FAILURE por hash stale, embora clean/upgrade/flattened tenham produzido SHA idêntico `8fe1db4da72aa420f3741c625270e5a6cd9cee2217ec9f60effbee8466a08a21`. Correções e testes unitários preparados na mesma PR; **não marcar TESTADO/SUCCESS no novo HEAD sem nova CI**. Matriz física positiva DT-10/DT-15, negativos governados, completude da busca e loader REF #1089 seguem PENDENTES.

## Evidência adicional DT-10 — 29/10/2026

No SHA `3017f048`, [preflight LocalDB #40805124598](https://github.com/lucianox777/Jornada/actions/runs/40805124598) executou 1 teste físico, 1 aprovado, 0 falhas, 0 ignorados. [Probe TRAIN 30k #40805124534](https://github.com/lucianox777/Jornada/actions/runs/40805124534) concluiu SUCCESS; isso não prova a matriz governada DT-15 nem as seis corridas físicas de holdout (ignoradas no workflow #40805124555). O cenário de lote misto acima reforça somente a atomicidade do caminho legado quando o motivo de ausência é forjado; não substitui o teste positivo governado, prova de completude da busca, nem a revisão REF #1089. Nenhuma política operacional foi habilitada.

## Adendo CI-V9-PROBE-01 — matriz de impacto da PR #1205 (28/10/2026)

Este adendo é transversal à DT-17 e **não altera** os resultados históricos de identidade/linkage acima. A [PR #1205](https://github.com/lucianox777/Jornada/pull/1205) implementa a proposta de isentar o probe físico somente quando o diff comprovado altera exclusivamente `.github/dependabot.yml`; código e testes estão em `Solution/scripts/v9-physical-impact-classifier.py` e `Solution/tests/scripts/test_v9_physical_impact_classifier.py`. A [ADR](Decisao_CI_Probe_V9_Por_Impacto_20261024.md) descreve as condições de segurança.

| Caso de regressão de classificação | Resultado obrigatório | Estado da evidência |
| --- | --- | --- |
| Diff comprovado e exclusivamente `.github/dependabot.yml` | Pode dispensar TRAIN 30k; **não** dispensar CI geral nem agregador obrigatório | **IMPLEMENTADO NA PR / não integrado**; validar teste específico e status no HEAD final. |
| `.github/dependabot.yml` misturado a SQL, Gold, FS/TF, calibrador, dependências ou outro arquivo | Não aplicar exceção isolada; classificar pelo impacto total, conservadoramente | **GATE EXIGIDO**; conferir regressões negativas no HEAD final. |
| Diff ausente, parcial, desconhecido ou ambíguo | Fail-closed; não aprovar probe por ausência de evidência | **GATE EXIGIDO**; conferir regressões negativas no HEAD final. |
| Alteração de workflow, classificador ou dos próprios testes do classificador | Não usar exceção Dependabot; preservar validação física aplicável | **GATE EXIGIDO**; a própria #1205 altera classificador/testes. |
| Probe cancelado, SKIPPED indevido ou HEAD divergente | Não declarar sucesso físico; agregador deve recusar certificação indevida | **GATE EXIGIDO**; cancelamento não é aprovação. |

**Rastreabilidade de execução:** no HEAD anterior `4e71f14b44e520a1f32f829ce2e501b536e226c7`, `jornada-ci` [40760201313](https://github.com/lucianox777/Jornada/actions/runs/40760201313) e `v9-experimental-fast-pr` [40760201311](https://github.com/lucianox777/Jornada/actions/runs/40760201311) terminaram SUCCESS, mas o probe [40760201315](https://github.com/lucianox777/Jornada/actions/runs/40760201315) estava IN_PROGRESS na consulta. O HEAD `eae8279d5a39800893f6a56cb3df426c09fc9bdb` iniciou `jornada-ci` [40778210984](https://github.com/lucianox777/Jornada/actions/runs/40778210984) **IN_PROGRESS**, fast PR [40778210986](https://github.com/lucianox777/Jornada/actions/runs/40778210986) **SUCCESS** e probe [40778211023](https://github.com/lucianox777/Jornada/actions/runs/40778211023) **PENDING** na consulta. Nenhum deles certifica antecipadamente o próximo commit deste documento.

**Estado:** proposta implementada na PR, documentação em reconciliação, **não aprovada nem integrada**; não há economia medida nem ativação de política V9. Preservar REF/IBGE, FS/TF, DT-10/DT-15, 30k sintéticos e aprovação humana posterior. A evidência deve ser atualizada por novo adendo após a conclusão dos checks do HEAD final, nunca por substituição de registros históricos.


## Adendo DT-10 — guarda literal SEM_CANDIDATO, PR #1210 (29/10/2026)

A migration aditiva `20261029_Linkage_NoCandidate_Literal_Guards.sql` redefine as procedures escalar e lote sem editar migrations históricas. `SEM[_]CANDIDATO[_]%` exige underscores literais. `51810` rejeita os quase-prefixos `SEMXCANDIDATO_` e `SEM_CANDIDATOX`; `51835` rejeita ausência alegada com melhor/segundo candidato persistido. A guarda não certifica a completude do blocking.

A matriz de evidência obrigatória é `LinkageNoCandidateLiteralGuardsSqlServerTests` (11 casos físicos SQL, sem skip): quatro quase-prefixos escalar/lote; quatro contradições de candidato; lote misto com zero eventos e estado/versão inalterados; dois cenários de ausência legítima e replay idempotente. Testes estruturais e sucesso do `integration-sql` anterior **não** atestam esses novos casos. A fixture usa apenas SQL Test/Dev isolado/Testcontainers e deve registrar TRX, run/job e SHA exato após inclusão na #1210. A prova foi incorporada ao HEAD da PR #1210 em `7f999ebb9a9039c2e673148c4317ea3636d4428c`, mas permanece **sem resultado físico comprovado** até a execução dos checks no HEAD definitivo. Não usar os resultados do HEAD anterior como certificação.

O ajuste DT-06 de 73 para 74 migrations foi validado no HEAD `e2fb16cb7e5fbec764485eb26cd7cd2f17464fcd` pela CI [40918850891](https://github.com/lucianox777/Jornada/actions/runs/40918850891), mas a prova 30k [40918851020](https://github.com/lucianox777/Jornada/actions/runs/40918851020) ainda estava em andamento na auditoria. Não transportar SUCCESS para um HEAD posterior; SKIPPED não é execução física. A #1199 permanece independente, com autorização DT-15 default OFF.


## Checkpoint DT-10 de integração — 30/10/2026 (PR #1199, sem ativação)

**Ordem e rollback.** A migration `20261029_Linkage_NoCandidate_Literal_Guards.sql` (#1210) precede `20261026_DT10_V9_Lower_Governed_Off.sql` (#1199) no manifesto e no instalador. A procedure de lote governada deve preservar o prefixo literal `SEM[_]CANDIDATO[_]%` e o erro 51835. O rollback controlado restaura a procedure **endurecida após a #1210**, nunca a definição histórica anterior. Qualquer formulação antiga de “procedure anterior” refere-se à versão endurecida imediatamente anterior à redefinição governada.

**Decisão multiobservação.** Para a mesma origem no mesmo run, a líder não pode publicar `SEM_CANDIDATO_*` se qualquer observação irmã materializada tiver `melhor_candidato_uuid`, `segundo_candidato_uuid` ou `pessoa_uuid_resolvido`. A guarda SQL percorre `#dt10_rows` por `pessoa_origem_id` e deve abortar o lote antes de qualquer evento, inclusive em lote misto; retry não pode produzir ledger. Isso não prova que o produtor executou todos os passes físicos de blocking.

**Evidência no SHA auditado `28125456ab349a865a52dc66f6d1e15234dbfa21`.** [Preflight LocalDB #41098709724](https://github.com/lucianox777/Jornada/actions/runs/41098709724): 1 teste NUnit PASS, 0 FAIL, 0 SKIP, TRX validado; cobre recusas OFF, candidato forjado na líder e em observação irmã, atomicidade mista e replay legado. [CI #41098709798](https://github.com/lucianox777/Jornada/actions/runs/41098709798), [schema #41098709771](https://github.com/lucianox777/Jornada/actions/runs/41098709771), [instalador #41098709760](https://github.com/lucianox777/Jornada/actions/runs/41098709760) e [validação independente #41098709936](https://github.com/lucianox777/Jornada/actions/runs/41098709936) concluíram SUCCESS. O candidato declara fingerprint estrutural `8b9adae24654e94bc668385ca9aa5c6c906744c0d8d88bd5b457f7ae9caa1f68` e SHA de manifest `c1d7d37f505563baea5d71d310e996945f5ffbf59d34fa6e24defbed3252e4b8`. [Holdout #41098709711](https://github.com/lucianox777/Jornada/actions/runs/41098709711): workflow SUCCESS, corridas físicas e agregador **SKIPPED**; não prova TEST selado. [Probe #41098709730](https://github.com/lucianox777/Jornada/actions/runs/41098709730): SUCCESS, job TRAIN 30k **SKIPPED**; não é novo ensaio de calibração.

**Bloqueios para merge.** Falta prova SQL **positiva** de autorização DT-15 ACTIVATE/digest legítimos (LOWER/intermediário/SEM_CANDIDATO/replay), negativos de adulteração individual de digest/fit/snapshot/limiares, atestado verificável de completude da busca física, contrato `upper` produzido pelo calibrador V9 e `lower` otimizado condicionado ao `upper`, revisão REF/loader #1089, VALIDATION/TEST selados e revisão humana. A PR segue DRAFT, política default OFF, sem aprovação, merge ou ativação. Revalidar todos os checks no SHA final após reconciliação com master. Os resultados históricos acima não certificam commits posteriores.

Matriz física ainda exigida: LOWER autorizado; faixa intermediária; ausência genuína com blocking completo; replay; mutações isoladas de digest, modelo, fit, snapshot, lower e upper; rollback endurecido; atomicidade e contagens de eventos. Registrar TRX, SHA, run/job e estado final; não contar cenários apenas descritos como executados.


## Adendo CI-V9-PROBE-01 — evidência pós-merge da #1205 (29/10/2026)

**Estado vigente: INTEGRADO**, sem apagar a matriz de proposta de 28/10 acima. A [#1205](https://github.com/lucianox777/Jornada/pull/1205) foi integrada pelo merge `b4fdc3f80310eeff9b48a4cf01429f485583e49a`, após checks no HEAD `7ca4fcec0ba67741b96b40dd0bcd0e21624ae5c5`.

| Classe de diff / gate | Comportamento exigido pelo classificador integrado | Evidência e limite |
| --- | --- | --- |
| Somente `.github/dependabot.yml` ou `.yaml`, status `A`/`M` | `NOT_REQUIRED_FOR_THIS_DIFF` | Casos unitários presentes; não equivale a TRAIN executado. |
| Dependabot `.yml`/`.yaml` **mais** documentação segura, status `A`/`M` | Composição segura, `NOT_REQUIRED_FOR_THIS_DIFF` | `test_dependabot_metadata_mixed_with_safe_docs_skips_probe` implementado no HEAD. |
| Mistura acima com SQL, workflow, FS/TF, calibrador ou caminho desconhecido | `RUN` fail-closed | Regressões negativas no script; não dispensar gates físicos. |
| Diff ausente/truncado ou status `D`, `R`, `C`, `T` | `RUN` ou rejeição do diff | Cobertura no parser/testes; não interpretar falha de leitura como dispensa. |
| Alteração do classificador e de seus testes | `RUN` | A própria #1205 exigiu TRAIN físico 30k. |
| Agregador e HEAD de referência | `SUCCESS` somente com gates efetivamente exigidos no SHA certo | SKIPPED/CANCELLED não são evidência de execução física. |

**Checks verificados do HEAD da #1205:** [jornada-ci #40895343764](https://github.com/lucianox777/Jornada/actions/runs/40895343764) `SUCCESS`; [fast PR #40895343762](https://github.com/lucianox777/Jornada/actions/runs/40895343762) `SUCCESS`; [probe #40895343763](https://github.com/lucianox777/Jornada/actions/runs/40895343763) `SUCCESS` para classificação, TRAIN e agregador. Não extrapolar esses resultados para um teste operacional de cancelamento seletivo, para a economia de CI ou para SHA posterior.

**PENDENTE:** comparação controlada de tempo/runner-minutes na [#1200](https://github.com/lucianox777/Jornada/issues/1200), prova operacional de cancelamento seletivo, e gates DT-10/DT-15/TEST independente em suas frentes próprias. A V9 continua default OFF, sem aprovação humana nem ativação. A documentação da #1205 foi corrigida em PR posterior ao merge; a divergência histórica não deve ser apagada.

## Complemento de integração documental — #1210 e #1211 (30/10/2026)

**Histórico preservado:** o adendo DT-10 da #1210 acima descreve o estado e os bloqueios observados em 29/10; o adendo CI-V9-PROBE-01 registra a #1205. A presente reconciliação reúne ambos após a integração da [#1210](https://github.com/lucianox777/Jornada/pull/1210) à master, merge `c03e3ef07f2b2bbf1374c37836d931a2705f1cfd`, sem reescrever as observações anteriores.

**Implementado e testado no HEAD da #1210 `fdc45924ee48610ec87094cc3bcb3b7ca7bf7f2e`:** [jornada-ci #40991772026](https://github.com/lucianox777/Jornada/actions/runs/40991772026) terminou SUCCESS; o job `integration-sql` registrou **162 PASS, 0 FAIL, 0 SKIP** no TRX principal e **2 PASS, 0 FAIL, 0 SKIP** no TRX de fault injection. A [probe #40991771989](https://github.com/lucianox777/Jornada/actions/runs/40991771989) terminou SUCCESS em classificação, TRAIN físico 30k e SQL clean. Essas contagens são do conjunto executado; não devem ser convertidas sem inspeção individual do TRX em certificação isolada de cada um dos 11 cenários DT-10.

**Limites e governança:** o resultado da #1210 não certifica completude do blocking, aprovação positiva governada DT-15, TEST independente, ganho de runner-minutes nem ativação V9. A [#1199](https://github.com/lucianox777/Jornada/pull/1199) continua DRAFT, com política default OFF e testes governados pendentes. A #1211 é uma correção documental posterior aos merges, não uma alegação de que a documentação já estava sincronizada nos merges anteriores. REF/IBGE, FS/TF, corpus sintético 30k e dados reais permanecem fora do escopo desta atualização.

**Changelog documental:** adendos #1205 e #1210 reunidos nesta matriz após os merges, com evidência física do SHA da #1210 adicionada sem alterar registros históricos.
