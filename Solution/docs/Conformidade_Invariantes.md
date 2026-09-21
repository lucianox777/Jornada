# Kit de conformidade — invariante → prova

Este arquivo liga invariantes arquiteturais a evidências executáveis existentes. Ele não exige um teste por linha; uma mesma prova pode cobrir mais de um invariante. Quando uma prova deixar de existir ou mudar de semântica, o documento deve ser atualizado no mesmo change-set.

| Invariante | Evidência principal | Tipo de prova |
|---|---|---|
| INV-FATO-001 | `tests/Jornada.Integration.Tests/Integration/ProcessorRepositoryTests.cs`; `IdentityGovernanceTests.cs` | Integração SQL: fato válido sobrevive a identidade pendente/conflito. |
| INV-GOLD-001 | `IdentityGovernanceTests.cs`; `IdentityReplayInvariantTests.cs` | Correção/replay preservam fato e histórico. |
| INV-CPF-001 | migrations `20260907_Cpf_Ancora.sql`; testes de governança de identidade | Constraint/trigger + integração impedem transferência/reciclagem. |
| INV-ORIGEM-001 | `PersonOriginIdentitySchemaMigrationTests.cs`; testes do Linkage | Estrutura e regressão impedem uso indevido de `initial_uuid`. |
| INV-SECID-001 | `NisRulesTests.cs`; `PersonIdentifierParsingTests.cs`; `ProcessorRepositoryTests.cs`; ADR-006 | Unit + integração provam NIS/RG secundários e ausência de `identity_map`. |
| INV-LINK-001 | `Linkage.Core` + testes de blocking/ruleset | Blocking produz candidatos; scorer/policy decide separadamente. |
| INV-LINK-002 | persistência `linkage_resultado` + publicação progressiva | Schema/procedure + integração separam score bruto e publicação. |
| INV-LINK-003 | gates de proveniência/modelo + tests de Parameters Worker | Versão/fingerprint/ruleset congelados. |
| INV-LINK-004 | testes de run incompleto/timeout/limite | Falha/incompletude não publica “nova identidade”. |
| INV-LINK-005 | `IdentityGovernanceTests.cs`; `IdentityReplayInvariantTests.cs`; publicação progressiva | Correção governada/replay preservam observações e histórico; atribuição probabilística não destrói origem. |
| INV-CONF-001 | migrations `20260920_Linkage_Implementation_Conference_Evidence.sql`, `...Conference_Command_Governance.sql`; CI de conferência | SQL fail-closed + processo independente. |
| INV-LEDGER-001 | `IdentityDecisionLedgerTests.cs`; trigger `tr_decisao_identidade_evento_append_only` | Atomicidade + append-only. |
| INV-MODEL-LEDGER-001 | migration `20260920_Linkage_Model_Promotion_Ledger.sql`; monitor tests | Trilha de promoção persistente e read-only no monitor. |
| INV-API-001 | testes de arquitetura/projeto + fronteiras de repositório | API não referencia writers Silver/Gold como atalho. |
| INV-SEC-001 | testes de access context/readiness; #378 | Fora de Development falha fechado sem identidade corporativa. |
| INV-SIGILO-001 | schema/processor/serving tests de endereço sigiloso | Projeção e Linkage bloqueados por construção. |
| INV-REPLAY-001 | `IdentityReplayInvariantTests.cs` + constraints/ledgers | Replay idempotente e histórico preservado. |

## Provas de supply chain

- `CandidateInfoTests.cs` congela o contrato de RC/preflight.
- `TestProjectBoundaryTests.cs` congela a separação Unit/Integration/ExternalRealData.
- workflows de schema/cluster/linkage independente são provas complementares; não substituem testes de domínio.
- #31 continua sendo o gate de dado real e não pode ser marcado como satisfeito por fixtures sintéticas.

## Regra para novos invariantes

Um novo invariante só entra aqui quando possui: ID estável, descrição observável e pelo menos uma prova concreta ou estado fail-closed verificável. Preferências de design e decisões ainda não tomadas pertencem a `Decisoes_Contingentes_Alocacao_CSharp_TSQL.md` ou às issues de governança, não a esta matriz.
