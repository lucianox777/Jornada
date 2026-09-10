# Matriz de Rastreabilidade - Jornada do Cidadão - Fase 1 de Requisitos - Jornada do Cidadão - Fase 1

**Versão do documento:** 1.1  
**Data:** 10/09/2026  
**Base normativa:** Especificação Técnica Jornada v3.62  
**Estado de incorporação:** candidato técnico à consolidação Solution Engenharia v5.00; release/tag ainda não cortada  
**Status:** MATRIZ CONSOLIDADA E AUTOSSUFICIENTE DA FASE 1
> **Leitura institucional.** Esta matriz v1.1 contém a rastreabilidade histórica e os aditivos vigentes em um único artefato. A matriz v1.0 permanece somente como histórico e não precisa ser consultada em conjunto. Referências `RNF01` a `RNF33` foram normalizadas para `RNF-001` a `RNF-033` sem alteração de significado.


> Esta matriz é o ponto único de rastreabilidade entre os quatro níveis de requisitos. As listas são intencionalmente de muitos-para-muitos; não significam que cada RT implemente isoladamente todo o RN.

## 1. Matriz por Requisito de Negócio

| RN | RF relacionados | RNF relacionados | RT relacionados | Evidência principal |
|---|---|---|---|---|
| **RN-001** Consolidar a Jornada sem substituir os sistemas finalísticos | — | RNF-001, RNF-018 | RT-001, RT-003 | Integration: ProcessorRepositoryTests/SeedDatabaseTests; contract and release gates conforme domínio |
| **RN-002** Padronizar a integração municipal de Pessoas e fatos | RF-001, RF-002, RF-003, RF-004, RF-005, RF-009 | RNF-003 | RT-006, RT-007, RT-010, RT-011, RT-013, RT-018 | Integration: ProcessorRepositoryTests/SeedDatabaseTests; contract and release gates conforme domínio |
| **RN-003** Preservar origem, temporalidade e linhagem | RF-001, RF-005, RF-006, RF-007, RF-008, RF-010, RF-024, RF-032 | RNF-002, RNF-004, RNF-006, RNF-012, RNF-021, RNF-022, RNF-024, RNF-032, RNF-033 | RT-002, RT-010, RT-011, RT-014, RT-015, RT-016, RT-017, RT-019, RT-020, RT-027, RT-043 | Integration: ProcessorRepositoryTests/SeedDatabaseTests; contract and release gates conforme domínio |
| **RN-004** Constituir a Gold de Pessoas de forma evolutiva | RF-011 | — | RT-032 | Integration: IdentityGovernanceTests, IdentityReplayInvariantTests; Unit: IdentityResolutionCoordinatorTests/FellegiSunterScoringTests |
| **RN-005** Manter uma identificação municipal estável da Pessoa | RF-012, RF-013, RF-018 | RNF-005, RNF-021, RNF-022, RNF-024 | RT-023, RT-024, RT-027, RT-030, RT-036 | Integration: IdentityGovernanceTests, IdentityReplayInvariantTests; Unit: IdentityResolutionCoordinatorTests/FellegiSunterScoringTests |
| **RN-006** Usar CPF como rota determinística com trava conservadora | RF-013, RF-014, RF-016 | RNF-005, RNF-023, RNF-025 | RT-023, RT-024, RT-026, RT-028, RT-050, RT-056 | Integration: IdentityGovernanceTests, IdentityReplayInvariantTests; Unit: IdentityResolutionCoordinatorTests/FellegiSunterScoringTests |
| **RN-007** Não descartar Pessoas admitidas sem CPF | RF-015, RF-016 | RNF-023 | RT-025, RT-026, RT-056 | Integration: IdentityGovernanceTests, IdentityReplayInvariantTests; Unit: IdentityResolutionCoordinatorTests/FellegiSunterScoringTests |
| **RN-008** Separar núcleo de identidade de atributos transversais | RF-019 | — | RT-034 | Integration: IdentityGovernanceTests, IdentityReplayInvariantTests; Unit: IdentityResolutionCoordinatorTests/FellegiSunterScoringTests |
| **RN-009** Promover valores Golden pela qualidade da evidência | RF-011, RF-020, RF-021 | — | RT-032, RT-034 | Integration: IdentityGovernanceTests, IdentityReplayInvariantTests; Unit: IdentityResolutionCoordinatorTests/FellegiSunterScoringTests |
| **RN-010** Permitir produção distribuída de evidência | RF-020, RF-021 | — | RT-034 | Integration: IdentityGovernanceTests, IdentityReplayInvariantTests; Unit: IdentityResolutionCoordinatorTests/FellegiSunterScoringTests |
| **RN-011** Separar fatos administrativos da Gold cadastral | RF-022, RF-027 | RNF-014 | RT-007, RT-031, RT-038 | Integration: ProcessorRepositoryTests, OperationalAtomicityTests, SeedDatabaseTests; gates de contratos/QC |
| **RN-012** Preservar fato válido mesmo sem atribuição canônica | RF-023 | RNF-006, RNF-014 | RT-011, RT-020, RT-033, RT-038 | Integration: ProcessorRepositoryTests, OperationalAtomicityTests, SeedDatabaseTests; gates de contratos/QC |
| **RN-013** Distinguir Possibilidade de fato, direito e decisão | RF-026, RF-027, RF-037 | RNF-014 | RT-031, RT-038 | Integration: ProcessorRepositoryTests, OperationalAtomicityTests, SeedDatabaseTests; gates de contratos/QC |
| **RN-014** Versionar regras que alteram significado de negócio | RF-003, RF-017, RF-026, RF-045 | RNF-013, RNF-016 | RT-005, RT-006, RT-007, RT-008, RT-029, RT-060 | Integration: ProcessorRepositoryTests, OperationalAtomicityTests, SeedDatabaseTests; gates de contratos/QC |
| **RN-015** Manter Referência Territorial separada de endereço civil | RF-029, RF-030 | RNF-007 | RT-041, RT-042 | Integration: SeedDatabaseTests; Unit: GeographicEnrichmentTests/ConfidentialShelterAddressPolicyTests |
| **RN-016** Responsabilizar a origem pela territorialização | RF-029, RF-031, RF-032 | RNF-007 | RT-042, RT-043 | Integration: SeedDatabaseTests; Unit: GeographicEnrichmentTests/ConfidentialShelterAddressPolicyTests |
| **RN-017** Aplicar precedência territorial explícita | RF-030, RF-032 | — | RT-043 | Integration: SeedDatabaseTests; Unit: GeographicEnrichmentTests/ConfidentialShelterAddressPolicyTests |
| **RN-018** Disponibilizar consulta da Pessoa aos sistemas autorizados | RF-033, RF-034, RF-035, RF-036, RF-037, RF-040 | RNF-029 | RT-035, RT-036, RT-037, RT-047, RT-048 | Unit: AuthorizationMatrixContractTests, AccessPolicyTests, ApiHttpPipelineTests, ApiRateLimitingTests; Integration: ApiReadinessTests; security gates |
| **RN-019** Adotar compartilhamento municipal padrão da Pessoa autorizada | RF-035, RF-038 | RNF-029 | RT-035, RT-037 | Unit: AuthorizationMatrixContractTests, AccessPolicyTests, ApiHttpPipelineTests, ApiRateLimitingTests; Integration: ApiReadinessTests; security gates |
| **RN-020** Permitir restrições setoriais como exceções negativas | RF-035, RF-038 | RNF-029 | RT-035, RT-037 | Unit: AuthorizationMatrixContractTests, AccessPolicyTests, ApiHttpPipelineTests, ApiRateLimitingTests; Integration: ApiReadinessTests; security gates |
| **RN-021** Proteger informação territorial especialmente sigilosa | RF-029, RF-031, RF-035, RF-038 | — | RT-042, RT-052 | Integration: SeedDatabaseTests; Unit: GeographicEnrichmentTests/ConfidentialShelterAddressPolicyTests |
| **RN-022** Manter decisão e escrita cadastral no Gestor finalístico | RF-033 | — | RT-036, RT-037, RT-054 | Unit: AuthorizationMatrixContractTests, AccessPolicyTests, ApiHttpPipelineTests, ApiRateLimitingTests; Integration: ApiReadinessTests; security gates |
| **RN-023** Estabelecer controles de qualidade antes do consumo | RF-003, RF-005, RF-007, RF-009, RF-010, RF-024, RF-025, RF-047, RF-048 | RNF-003, RNF-004, RNF-006, RNF-007, RNF-009, RNF-012, RNF-015, RNF-020, RNF-023, RNF-026, RNF-027, RNF-028, RNF-033 | RT-012, RT-014, RT-015, RT-017, RT-018, RT-019, RT-020, RT-021, RT-022, RT-024, RT-026, RT-033, RT-046, RT-053, RT-054, RT-056, RT-057, RT-058, RT-059, RT-061, RT-062, RT-063, RT-064 | Integration: ProcessorRepositoryTests, OperationalAtomicityTests, SeedDatabaseTests; gates de contratos/QC |
| **RN-024** Evidenciar cobertura e incerteza nos indicadores | RF-010, RF-014, RF-015, RF-016, RF-017, RF-025, RF-028, RF-042 | RNF-007, RNF-009, RNF-015, RNF-016, RNF-021, RNF-022, RNF-024, RNF-025, RNF-026, RNF-027, RNF-028 | RT-019, RT-022, RT-025, RT-027, RT-028, RT-029, RT-046, RT-050, RT-053, RT-057 | Integration: ProcessorRepositoryTests, OperationalAtomicityTests, SeedDatabaseTests; gates de contratos/QC |
| **RN-025** Oferecer BI municipal sobre Pessoas, fatos, qualidade e território | RF-028, RF-041, RF-042, RF-044 | RNF-010, RNF-011, RNF-013 | RT-041, RT-043, RT-044, RT-045, RT-046, RT-047, RT-064 | powerbi-static-gate; serving contracts; Anexo BI/SLA; evidência HML quando aplicável |
| **RN-026** Manter consultas individualizadas fora do BI | RF-034, RF-036, RF-037, RF-043 | — | RT-047 | Unit: AuthorizationMatrixContractTests, AccessPolicyTests, ApiHttpPipelineTests, ApiRateLimitingTests; Integration: ApiReadinessTests; security gates |
| **RN-027** Medir tempestividade de recebimento por Tipo e versão | RF-044 | RNF-020 | RT-058 | powerbi-static-gate; serving contracts; Anexo BI/SLA; evidência HML quando aplicável |
| **RN-028** Suportar carga inicial sem degradar governança de identidade | RF-011, RF-016, RF-024, RF-048 | RNF-015, RNF-023, RNF-026, RNF-027, RNF-028 | RT-022, RT-026, RT-032, RT-056, RT-057 | Integration: IdentityGovernanceTests, IdentityReplayInvariantTests; Unit: IdentityResolutionCoordinatorTests/FellegiSunterScoringTests |
| **RN-029** Garantir rastreabilidade e auditoria de atos sensíveis | RF-002, RF-006, RF-007, RF-014, RF-017, RF-018, RF-020, RF-025, RF-038, RF-039, RF-040, RF-045, RF-046, RF-047 | RNF-004, RNF-006, RNF-008, RNF-012, RNF-016, RNF-017, RNF-021, RNF-022, RNF-024, RNF-029, RNF-030, RNF-031, RNF-032, RNF-033 | RT-004, RT-005, RT-013, RT-014, RT-015, RT-016, RT-017, RT-020, RT-021, RT-027, RT-029, RT-030, RT-034, RT-035, RT-039, RT-040, RT-048, RT-049, RT-051, RT-054, RT-059, RT-060, RT-062, RT-063, RT-064 | Unit: AuthorizationMatrixContractTests, AccessPolicyTests, ApiHttpPipelineTests, ApiRateLimitingTests; Integration: ApiReadinessTests; security gates |
| **RN-030** Aplicar minimização e proteção de dados pessoais | RF-008, RF-035, RF-038, RF-039, RF-040, RF-042, RF-043, RF-046 | RNF-003, RNF-005, RNF-008, RNF-011, RNF-017, RNF-029, RNF-030, RNF-031, RNF-032 | RT-004, RT-012, RT-016, RT-023, RT-039, RT-040, RT-045, RT-049, RT-050, RT-051, RT-052 | Unit: AuthorizationMatrixContractTests, AccessPolicyTests, ApiHttpPipelineTests, ApiRateLimitingTests; Integration: ApiReadinessTests; security gates |
| **RN-031** Definir governança de retenção antes da produção | RF-008, RF-046 | RNF-032, RNF-033 | RT-016, RT-017, RT-051, RT-064 | governance/release gates, runbooks e homologação; runtime local v3.95 para gates executáveis localmente |
| **RN-032** Exigir avaliação de impacto antes da produção | — | — | RT-064 | governance/release gates, runbooks e homologação; runtime local v3.95 para gates executáveis localmente |
| **RN-033** Formalizar responsabilidades institucionais | RF-047, RF-048 | RNF-009, RNF-011, RNF-012, RNF-017, RNF-019, RNF-020 | RT-004, RT-005, RT-009, RT-045, RT-048, RT-053, RT-055, RT-058, RT-059, RT-060, RT-061, RT-062, RT-063, RT-064 | governance/release gates, runbooks e homologação; runtime local v3.95 para gates executáveis localmente |
| **RN-034** Manter interlocução técnica e qualidade por Gestor | RF-002, RF-044, RF-049 | RNF-019 | RT-006, RT-009, RT-013, RT-055, RT-064 | governance/release gates, runbooks e homologação; runtime local v3.95 para gates executáveis localmente |
| **RN-035** Permitir expansão a novos Gestores e Tipos | RF-003, RF-009, RF-041, RF-045, RF-049 | RNF-001, RNF-002, RNF-010, RNF-012, RNF-013, RNF-015, RNF-018, RNF-019, RNF-027 | RT-001, RT-002, RT-003, RT-006, RT-007, RT-008, RT-009, RT-018, RT-021, RT-044, RT-057 | governance/release gates, runbooks e homologação; runtime local v3.95 para gates executáveis localmente |
| **RN-036** Manter biometria como evolução e não como núcleo cadastral | RF-050 | — | RT-065 | governance/release gates, runbooks e homologação; runtime local v3.95 para gates executáveis localmente |

## 2. Cobertura por RF

| RF | RN de origem | RNF associados | RT de realização |
|---|---|---|---|
| RF-001 | RN-002, RN-003 | RNF-003 | RT-010 |
| RF-002 | RN-002, RN-029, RN-034 | — | RT-013, RT-048 |
| RF-003 | RN-002, RN-014, RN-023, RN-035 | RNF-003, RNF-013 | RT-006, RT-007, RT-008, RT-013 |
| RF-004 | RN-002 | — | RT-010, RT-011 |
| RF-005 | RN-002, RN-003, RN-023 | — | RT-011 |
| RF-006 | RN-003, RN-029 | RNF-021, RNF-022, RNF-024 | RT-010, RT-014, RT-019, RT-027, RT-043 |
| RF-007 | RN-003, RN-023, RN-029 | RNF-004 | RT-015 |
| RF-008 | RN-003, RN-030, RN-031 | RNF-032, RNF-033 | RT-016, RT-017 |
| RF-009 | RN-002, RN-023, RN-035 | RNF-026, RNF-028 | RT-018, RT-021, RT-022 |
| RF-010 | RN-003, RN-023, RN-024 | — | RT-019 |
| RF-011 | RN-004, RN-009, RN-028 | — | RT-032 |
| RF-012 | RN-005 | RNF-005 | RT-023, RT-030 |
| RF-013 | RN-005, RN-006 | — | RT-024 |
| RF-014 | RN-006, RN-024, RN-029 | — | RT-024, RT-030 |
| RF-015 | RN-007, RN-024 | RNF-023 | RT-025, RT-026 |
| RF-016 | RN-006, RN-007, RN-024, RN-028 | RNF-023, RNF-025 | RT-026, RT-028 |
| RF-017 | RN-014, RN-024, RN-029 | RNF-016, RNF-021, RNF-022, RNF-024 | RT-027, RT-029 |
| RF-018 | RN-005, RN-029 | — | RT-030 |
| RF-019 | RN-008 | RNF-005 | RT-023, RT-034 |
| RF-020 | RN-009, RN-010, RN-029 | — | RT-034 |
| RF-021 | RN-009, RN-010 | — | RT-034 |
| RF-022 | RN-011 | RNF-014 | RT-031, RT-033 |
| RF-023 | RN-012 | — | RT-033 |
| RF-024 | RN-003, RN-023, RN-028 | RNF-006 | RT-020, RT-032, RT-033 |
| RF-025 | RN-023, RN-024, RN-029 | — | RT-019, RT-046, RT-064 |
| RF-026 | RN-013, RN-014 | RNF-014 | RT-038, RT-064 |
| RF-027 | RN-011, RN-013 | RNF-014 | RT-038 |
| RF-028 | RN-024, RN-025 | — | RT-046 |
| RF-029 | RN-015, RN-016, RN-021 | RNF-007 | RT-041, RT-043 |
| RF-030 | RN-015, RN-017 | — | RT-041, RT-043 |
| RF-031 | RN-016, RN-021 | RNF-007 | RT-042 |
| RF-032 | RN-003, RN-016, RN-017 | RNF-007 | RT-043 |
| RF-033 | RN-018, RN-022 | RNF-029 | RT-035, RT-036 |
| RF-034 | RN-018, RN-026 | RNF-029 | RT-035, RT-037 |
| RF-035 | RN-018, RN-019, RN-020, RN-021, RN-030 | RNF-029 | RT-035, RT-037 |
| RF-036 | RN-018, RN-026 | RNF-014 | RT-038 |
| RF-037 | RN-013, RN-018, RN-026 | RNF-014 | RT-038 |
| RF-038 | RN-019, RN-020, RN-021, RN-029, RN-030 | RNF-029 | RT-035, RT-037, RT-048 |
| RF-039 | RN-029, RN-030 | RNF-030 | RT-039 |
| RF-040 | RN-018, RN-029, RN-030 | RNF-029, RNF-031 | RT-040 |
| RF-041 | RN-025, RN-035 | RNF-010, RNF-013 | RT-044 |
| RF-042 | RN-024, RN-025, RN-030 | RNF-010, RNF-011, RNF-013 | RT-044, RT-045, RT-046 |
| RF-043 | RN-026, RN-030 | — | RT-047 |
| RF-044 | RN-027, RN-025, RN-034 | RNF-020 | RT-058 |
| RF-045 | RN-014, RN-029, RN-035 | RNF-013, RNF-016 | RT-005, RT-006, RT-007, RT-008, RT-029 |
| RF-046 | RN-031, RN-029, RN-030 | RNF-033 | RT-017, RT-051 |
| RF-047 | RN-023, RN-029, RN-033 | RNF-009 | RT-053, RT-054, RT-061, RT-062, RT-063, RT-064 |
| RF-048 | RN-023, RN-028, RN-033 | RNF-026, RNF-028 | RT-022, RT-055, RT-056 |
| RF-049 | RN-034, RN-035 | RNF-001, RNF-010, RNF-013, RNF-018, RNF-019 | RT-001, RT-003, RT-006, RT-007, RT-008, RT-009, RT-044 |
| RF-050 | RN-036 | — | RT-065 |

## 3. Cobertura por RNF

| RNF | RF afetados | RT de realização |
|---|---|---|
| RNF-001 | RF-049 | RT-001 |
| RNF-002 | — | RT-002 |
| RNF-003 | RF-001, RF-003 | RT-012 |
| RNF-004 | RF-007 | RT-015 |
| RNF-005 | RF-012, RF-019 | RT-023 |
| RNF-006 | RF-024 | RT-020 |
| RNF-007 | RF-029, RF-031, RF-032 | RT-041 |
| RNF-008 | — | RT-049 |
| RNF-009 | RF-047 | RT-053 |
| RNF-010 | RF-041, RF-042, RF-049 | RT-044 |
| RNF-011 | RF-042 | RT-045 |
| RNF-012 | — | RT-002, RT-059 |
| RNF-013 | RF-003, RF-041, RF-042, RF-045, RF-049 | RT-008, RT-044 |
| RNF-014 | RF-022, RF-026, RF-027, RF-036, RF-037 | RT-031, RT-038 |
| RNF-015 | — | RT-057 |
| RNF-016 | RF-017, RF-045 | RT-029 |
| RNF-017 | — | RT-004 |
| RNF-018 | RF-049 | RT-003 |
| RNF-019 | RF-049 | RT-009 |
| RNF-020 | RF-044 | RT-058 |
| RNF-021 | RF-006, RF-017 | RT-027 |
| RNF-022 | RF-006, RF-017 | RT-027 |
| RNF-023 | RF-015, RF-016 | RT-026 |
| RNF-024 | RF-006, RF-017 | RT-027 |
| RNF-025 | RF-016 | RT-028 |
| RNF-026 | RF-009, RF-048 | RT-022 |
| RNF-027 | — | RT-057 |
| RNF-028 | RF-009, RF-048 | RT-022 |
| RNF-029 | RF-033, RF-034, RF-035, RF-038, RF-040 | RT-035, RT-040 |
| RNF-030 | RF-039 | RT-039 |
| RNF-031 | RF-040 | RT-040 |
| RNF-032 | RF-008 | RT-016 |
| RNF-033 | RF-008, RF-046 | RT-017 |

## 4. Cobertura e regras de manutenção

- Todos os 36 RN devem aparecer na Seção 1.
- Todos os 50 RF devem aparecer na Seção 2.
- Todos os 33 RNF normativos devem aparecer na Seção 3.
- Todos os 65 RT devem estar referenciados por pelo menos um RN/RF/RNF ou ser explicitamente classificados como infraestrutura transversal.
- Mudança material em qualquer camada exige revisão desta matriz e das evidências correspondentes.

## 5. Controle de versão

| Versão | Data | Síntese | Incorporação |
|---|---|---|---|
| 1.0 | 03/09/2026 | Matriz inicial RN -> RF -> RNF -> RT -> evidência da Fase 1. | Solution Engenharia v3.98 |

## Incorporações da versão 1.1

## Novos requisitos funcionais

| RF | RN de origem | RNF associados | Realização/evidência principal |
|---|---|---|---|
| **RF-051** Paralelismo no calibrador/avaliador quando vantajoso | RN-023, RN-024, RN-028, RN-035 | RNF-015, RNF-020, RNF-021, RNF34-A, RNF34-B | Benchmark comparativo serial×paralelo; grau de paralelismo configurável; regressão de equivalência determinística; CI |
| **RF-052** Uso governado de frequências agregadas oficiais do IBGE no blocking | RN-005, RN-014, RN-023, RN-024, RN-029 | RNF-013, RNF-016, RNF-021, RNF-027, RNF34-A, RNF34-B | `IbgeTypedNameFrequencyCatalog`, snapshot/fingerprint IBGE, testes de proveniência e avaliação de ganho |
| **RF-053** Nome completo, prenome, sobrenome, último nome e dia/mês/ano no blocking otimizado | RN-005, RN-006, RN-014, RN-023, RN-024 | RNF-016, RNF-021, RNF-025, RNF-027, RNF34-A, RNF34-B | `BirthBlockingPlan`, `BlockingFeatureDiagnostic`, `BlockingCombinationDiagnostic`, frequências IBGE quando aplicáveis, política dinâmica versionada e avaliação independente |
| **RF-054** Preservação da semântica oficial dos nomes do IBGE | RN-005, RN-014, RN-024, RN-029, RN-030 | RNF-013, RNF-016, RNF-021, RNF-030, RNF34-A, RNF34-B | grafia/frequência oficial preservada; snapshot tipado por prenome/sobrenome e território; representação técnica versionada; regressões contra colapsos indevidos |
| **RF-055** Mesma política dinâmica versionada no calibrador e avaliador | RN-003, RN-014, RN-024, RN-029 | RNF-013, RNF-016, RNF-021, RNF34-A, RNF34-B | `DynamicBlockingPolicy`/ruleset persistido, fingerprint da política, relatório de avaliação e testes de equivalência entre superfícies/providers |
| **RF-056** Evitar snapshots redundantes do IBGE | RN-014, RN-023, RN-024, RN-029 | RNF-013, RNF-020, RNF-021, RNF34-A, RNF34-B | `IbgeSnapshotChangeDetector`; Content-Length/ETag/Last-Modified como pré-verificação; SHA-256 para metadados inconclusivos; testes de mesmo tamanho com conteúdo distinto |

## Aditivos não funcionais

| RNF | Incidência | Evidência mínima |
|---|---|---|
| **RNF-012 (complemento)** Regressão unitária + integração | Toda mudança funcional/estatística/contratual aplicável | Teste unitário e teste integrado do caminho alterado; exceção somente quando explicitamente justificada |
| **RNF34-A** Documentação sincronizada | Todo change-set material | Código + requisitos + README/runbook/especificação/arquitetura afetados no mesmo PR; ADR somente quando a política de histórico decisório o exigir |
| **RNF34-B** CI obrigatório | Todo PR com mudança verificável | Gates obrigatórios do HEAD exato concluídos com sucesso antes de Ready/merge |
| **RNF34-C** Diagramas UML | Toda visão técnica/arquitetural normativa | DOCX/PDF com diagramas UML incorporados; diagrama de classes para estrutura e de atividade para resolução de identidade; DER/DRE não classificado como UML; leitura sem software específico de modelagem |
| **RNF34-D** Ambiente tecnológico reprodutível | Desenvolvimento, teste, integração, bancos e BI | C#/.NET, Git, Docker e Power BI Desktop conforme aplicável; **Microsoft SQL Server como tecnologia relacional normativa**; PostgreSQL somente como provider paralelo em escopos explícitos; versões controladas/documentadas; CI reproduzível |

## Relação com a issue #31

Nenhum dos itens acima, isoladamente, encerra a homologação estatística. A ativação probabilística continua condicionada a corpus representativo/atestado, separação de calibração e avaliação, recall/precisão/calibração/falsos vínculos, análise de dependências e subgrupos e aprovação institucional explícita.
