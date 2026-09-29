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
| DC-ID-02 — divisão sem âncora: A vira histórico e as duas pessoas recebem destinos canônicos novos/admitidos, sem prêmio por continuidade | [`Dt17IdentityCompositionRegressionTests.DcId02_unanchored_decided_split_requires_two_new_canonical_uuids_and_makes_old_reference_historical`](../tests/Jornada.Tests/Unit/Dt17IdentityCompositionRegressionTests.cs); reforça [`IdentityCompositionTests.Fully_decided_unanchored_split_requires_new_or_already_admitted_destinations`](../tests/Jornada.Tests/Unit/IdentityCompositionTests.cs) | **NÃO EXECUTADA** — criada na branch DT-17; aguarda job `unit` no HEAD do PR. |
| DC-ID-02 — com âncora CPF, A permanece com o titular; a outra pessoa recebe outro destino | [`Dt17IdentityCompositionRegressionTests.DcId02_cpf_anchor_keeps_holder_on_old_uuid_and_moves_only_other_person`](../tests/Jornada.Tests/Unit/Dt17IdentityCompositionRegressionTests.cs); [`IdentityCompositionTests.Cpf_anchored_split_preserves_anchor_and_allocates_other_destination`](../tests/Jornada.Tests/Unit/IdentityCompositionTests.cs) | **NÃO EXECUTADA** — aguarda `unit`. |
| DC-ID-01/DC-ID-02 — `initial_uuid` isolado não é promovido a canônico | [`Dt17IdentityCompositionRegressionTests.DcId02_bare_initial_uuid_is_not_promoted_to_canonical_destination`](../tests/Jornada.Tests/Unit/Dt17IdentityCompositionRegressionTests.cs); [`IdentityCompositionTests.Reusing_a_historical_alias_or_someone_elses_initial_uuid_is_rejected`](../tests/Jornada.Tests/Unit/IdentityCompositionTests.cs) | **NÃO EXECUTADA** — aguarda `unit`. |
| `initial_uuid` é proveniência, nunca feature de blocking/score | [`Dt17NonScorerInvariantContractTests.Initial_uuid_is_not_a_blocking_or_scoring_feature`](../tests/Jornada.Tests/Unit/Dt17NonScorerInvariantContractTests.cs) protege `FellegiSunterScoring.cs` e `BlockingProjectionCandidateQueryBuilder.cs` contra uso de `InitialUuid` | **NÃO EXECUTADA** — aguarda `unit`; não altera scorer. |
| Atributos do núcleo podem ser NULL na observação; obrigatoriedade pertence ao contrato da Secretaria/remessa | [`Dt17NonScorerInvariantContractTests.Observation_core_identity_attributes_are_nullable_while_secretariat_contract_may_require_them`](../tests/Jornada.Tests/Unit/Dt17NonScorerInvariantContractTests.cs) confere nulabilidade Silver e preserva contratos que declaram `required` | **NÃO EXECUTADA** — aguarda `unit`. |
| CPF válido/inválido/conflitante — regras e superfícies de resolução/correção existentes | [`CpfRulesTests.Normalizes_valid_cpf`](../tests/Jornada.Tests/Unit/CpfRulesTests.cs), `Rejects_invalid_cpf`; [`CpfAnchorResolutionApiTests.Resolver_classifies_structurally_invalid_cpf_without_uuid`](../tests/Jornada.Integration.Tests/Integration/CpfAnchorResolutionApiTests.cs), `Resolver_uses_permanent_anchor_when_current_map_is_closed_or_in_conflict`; [`CpfAnchorGovernedCorrectionTests.Governed_correction_cannot_transfer_permanent_cpf_anchor`](../tests/Jornada.Integration.Tests/Integration/CpfAnchorGovernedCorrectionTests.cs); inventário protegido por [`Dt17NonScorerInvariantContractTests.Cpf_regressions_cover_valid_invalid_and_conflict_evidence_without_claiming_route_execution`](../tests/Jornada.Tests/Unit/Dt17NonScorerInvariantContractTests.cs) | **NÃO EXECUTADA** — `unit` e `integration-sql` precisam concluir no mesmo HEAD. A inspeção de marcadores não afirma execução das rotas. |
| Segurança 401/403 e escopo nas rotas de identidade existentes | [`ProgressiveOriginApiTests.Origin_route_fails_closed_and_returns_only_owner_snapshot`](../tests/Jornada.Tests/Unit/ProgressiveOriginApiTests.cs); [`SemiblindIdentityHttpTests.Route_enforces_access_and_pre_response_audit`](../tests/Jornada.Tests/Unit/SemiblindIdentityHttpTests.cs); [`AuthorizationMatrixContractTests.Route_matrix_is_unique_and_type_credentials_are_limited_to_explicit_routes`](../tests/Jornada.Tests/Unit/AuthorizationMatrixContractTests.cs); inventário protegido por [`Dt17NonScorerInvariantContractTests.Existing_identity_routes_keep_401_403_and_scope_regressions`](../tests/Jornada.Tests/Unit/Dt17NonScorerInvariantContractTests.cs) | **NÃO EXECUTADA** — aguarda `unit`; #539/#378 continuam limites institucionais distintos. |
| Identidade progressiva e reavaliação | [`ProgressiveIdentityTests.InitialUuidIsAllocatedBeforeResolutionWithoutInventingAnAssociation`](../tests/Jornada.Tests/Unit/ProgressiveIdentityTests.cs), `AmbiguityKeepsTheReferenceWithoutChoosingAnyCandidate`; [`IdentityReplayInvariantTests.Identity_graph_global_invariants_hold_after_seed_and_recomposition_replay`](../tests/Jornada.Integration.Tests/Integration/IdentityReplayInvariantTests.cs) | **NÃO EXECUTADA** no HEAD desta frente até os jobs correspondentes concluírem. |
| Ledger semântico/idempotência | [`Dt05SemanticTransitionContractTests.V1_signature_field_order_and_documentation_are_frozen`](../tests/Jornada.Tests/Unit/Dt05SemanticTransitionContractTests.cs); [`Dt05SemanticThreeWavesSqlServerTests.Four_waves_ignore_unchanged_and_score_only_retries_without_losing_raw_results`](../tests/Jornada.Integration.Tests/Integration/Dt05SemanticThreeWavesSqlServerTests.cs); [`Dt05PublicationGuardsSqlServerTests.Ledger_requires_transaction_and_executing_run_without_side_effects`](../tests/Jornada.Integration.Tests/Integration/Dt05PublicationGuardsSqlServerTests.cs) | **NÃO EXECUTADA** no HEAD desta frente; replay Parquet/NAS permanece fora do escopo. |

## Evidência histórica preservada — não é aceite vigente desta frente

- **IBGE obrigatório, sem fallback (28/09):** PR #595, merge `911f8699c021cf3b7a846d8e397e543ee68a24be`, jornada-ci #36496723272. Foi regra vigente naquele corte e está **SUPERADA por DC-LK-01**; não apagar a evidência histórica.
- **V6 empate/threshold:** [`ProbabilisticV6TieOrderingTests.Exact_log_odds_tie_is_conflict_and_uuid_only_stabilizes_display_order`](../tests/Jornada.Tests/Unit/ProbabilisticV6TieOrderingTests.cs) e [`LinkageDecisionBoundaryTests.V6_threshold_is_inclusive_at_exact_best_score_and_rejects_immediately_above`](../tests/Jornada.Tests/Unit/LinkageDecisionBoundaryTests.cs) permanecem rastreáveis, mas não são critério da V8.
- **V8 ausência materna (28/09):** `ProbabilisticV8NeutralMissingContractTests.V8_maternal_absence_is_neutral_for_unilateral_and_bilateral_missingness` permanece histórica enquanto TF/guard são alterados na frente V8.
- **CPF tardio três ondas:** [`dt05-cpf-late-wave.ps1`](../scripts/dt05-cpf-late-wave.ps1), marcador `DT05_CPF_LATE_REAL_RUNNER_E2E`, teve prova histórica no PR #540. Não foi reexecutado por esta frente e não deve ser contabilizado como execução atual.

## Fora do escopo e lacunas preservadas

- **LACUNA: MARIA SOUZA / MARIA SOUZA LIMA / MARIA LIMA.** Homônimos/empates/guard pertencem à frente V8; não reinterpretar resultados aqui.
- **LACUNA: DT-05 replay histórico NAS.** Replay Parquet/NAS permanece fora desta frente.
- **LACUNA: DT-10 concorrência adversarial e rollback parcial.** PR #630; permanece separado.
- **LACUNA: #539 / #378 compartilhamento institucional.** CI DEV não autoriza exposição institucional.
- **LACUNA: #506 / #31 validação externa e representatividade.** CI sintético não substitui validação externa/real.
- **LACUNA: Ensaio integrado DT-17.** Esta matriz não declara aceite integral de Ensaio/HML/Produção.
- **UUID incorporado:** estado sem nome/contrato; fora do escopo.

## Execução e ambiente

Para este PR, registrar após execução o SHA exato e os jobs efetivamente concluídos: `dependency-lock`, `unit`, `deterministic-build`, `security-analysis`, `integration-sql`, `harness-smoke`, `ddl-upgrade` e `e2e`. Job `skipped` permanece **NÃO EXECUTADO**. Não alterar workflows para esta DT.

Testes SQL desta frente usam somente o banco sintético/isolado configurado pelo CI. **Nunca resetar `JornadaLocal`**. O roteiro DEV destrutivo continua restrito a `JornadaSyntheticDev`.

## DP-01

**Arquivos declarados:** `Solution/docs/DT17_Matriz_Regressao.md`, `Solution/tests/Jornada.Tests/Unit/Dt17RegressionMatrixContractTests.cs`, `Solution/tests/Jornada.Tests/Unit/Dt17IdentityCompositionRegressionTests.cs` e `Solution/tests/Jornada.Tests/Unit/Dt17NonScorerInvariantContractTests.cs`.

**Contratos/SQL afetados:** nenhum contrato, DDL, migration ou SQL de produção alterado; testes apenas leem contratos/DDL existentes. **Dependências:** DC-ID-01/DC-ID-02 e DC-LK-01 de 29/09. **Testes/gates:** os oito jobs do jornada-ci, contabilizados somente se executados. **Riscos:** testes de contrato estrutural podem detectar mudança legítima futura e exigir reconciliação documental; não corrigir produção nesta frente. **Rollback:** reverter documentação e arquivos de teste novos/ajustados.

A separação da CI está excluída desta frente. DT-10 permanece última na sequência já definida.
