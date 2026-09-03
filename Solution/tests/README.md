# Testes da Jornada

## Ambiente local Docker e harnesses v3.55

A forma recomendada de executar a categoria `Integration` localmente é `scripts/local-test.ps1` (Windows/PowerShell) ou `scripts/local-test.sh` (shell POSIX). Os scripts sobem SQL Server 2022 **Developer** em Docker, criam `JornadaLocal`, aplicam DDL/seed e definem `JORNADA_TEST_SQL_CONNECTION`. A edição Developer é somente desenvolvimento/teste; Produção usa a edição/licenciamento homologados pela PRODAM.


## Unitários - não dependem de SQL Server

```bash
dotnet test tests/Jornada.Tests/Jornada.Tests.csproj
```

A categoria `Integration` foi separada fisicamente em `Jornada.Integration.Tests`; `Jornada.Tests` contém a suíte unitária/runtime HTTP. Testes SQL usam `Assert.Ignore` somente quando `JORNADA_TEST_SQL_CONNECTION` não está definida. Fixtures determinísticas não podem usar `Assert.Ignore`. Em CI de release, Unit e Integration geram TRX e passam por `test-evidence-gate.py --forbid-skipped`, portanto qualquer teste esperado não executado bloqueia a promoção.



## Compatibilidade Testcontainers/Docker — v3.88

A v3.87 compilou em Release com 0 warnings/0 errors e aprovou 153/153 Unit, mas a Integration foi bloqueada no `OneTimeSetUp`: Testcontainers 4.14 tentou API Docker 1.44 contra servidor máximo 1.41. A v3.88 negocia para baixo `DOCKER_API_VERSION` somente quando necessário e respeita qualquer override explícito. Isso é uma correção de infraestrutura; os 58 casos Integration precisam ser reexecutados para produzir evidência funcional.

## Correção de assembly de Integration — v3.85

A compilação real da v3.84 revelou que a movimentação dos testes para `Jornada.Integration.Tests` não havia sido acompanhada pelos `InternalsVisibleTo` dos assemblies de produção. A v3.85 mantém os tipos internos e adiciona o friend assembly somente em `Jornada.Api`, `Jornada.Processor.Worker` e `Jornada.Bronze.Maintenance.Worker`. Tornar esses tipos `public` não faz parte da correção.

## Projeto dedicado de Integration — v3.84

A suíte SQL real possui projeto próprio: `tests/Jornada.Integration.Tests/Jornada.Integration.Tests.csproj`.
Os testes foram retirados fisicamente de `Jornada.Tests/Integration`, preservando namespaces e comportamento, e o projeto foi adicionado a `Jornada.sln`.
Isso permite restaurar/buildar/testar Unit e Integration como assemblies independentes e deixa explícita a dependência de SQL Server/Testcontainers.
Na v3.84, `Testcontainers.MsSql` foi removido do projeto Unit. Integration mantém sete `ProjectReference` diretos; referências a `Jornada.Ingestion`, `Jornada.Linkage.Parameters.Worker` e `Jornada.Linkage.Runner` foram removidas por não haver uso direto na suíte. O gate de arquitetura exige exatamente essa fronteira.

## Integração SQL Server

Use somente banco cujo nome contenha `Test` ou `Dev`:

```bash
export JORNADA_TEST_SQL_CONNECTION='Server=...;Database=JornadaTest;...'
dotnet test tests/Jornada.Integration.Tests/Jornada.Integration.Tests.csproj
```

Os testes de integração executam DDL + seed e agora cobrem também a trilha de auditoria por Pessoa/credencial/recurso e as transições principais do `SqlProcessorRepository`, incluindo publicação atômica de Pessoa/Fato/Serving/completude, e regras unitárias de QC do Processor.

## Processor SQL

Os testes `[Category("Integration")]` cobrem reserva/recovery/rejeição, publicação atômica, concorrência de dois workers reservando lotes distintos e rollback integral quando uma falha ocorre após persistência parcial dentro da transação. Eles continuam opt-in via `JORNADA_TEST_SQL_CONNECTION`.

## Segurança de borda e pipeline HTTP

A suíte unitária cobre rate limiting por classe de endpoint, uso exclusivo de `RemoteIpAddress`, separação dos buckets autenticados de Pessoa/Identidade/Ingestão e configuração fail-closed de proxies confiáveis.

## Fixtures de ingestão

`tests/fixtures/ingestao/` contém payloads PESSOA, Benefício Concedido e Serviço Prestado usados pelos testes de contrato. Não existe pasta `examples` de produto; qualquer amostra executável deve ser tratada como fixture de teste ou documentação histórica não normativa.

## Cobertura v3.41 — compartilhamento municipal e auditoria

- `AccessPolicyTests` verifica que nenhuma credencial autorizada depende de relacionamento prévio com a Pessoa; `BENEFICIO`/`SERVICO` permanecem limitadas ao código de recurso autorizado.
- Não existe `PRIMEIRO_ATENDIMENTO` na fronteira vigente; consultas de Pessoa usam a regra de cardinalidade do próprio endpoint.
- `AgentCpfPseudonymizerTests` verifica HMAC determinístico de 32 bytes sem persistir CPF em claro.
- As projeções SQL aplicam `controle.fn_projecao_jornada_permitida` por Gestor responsável/consumidor e alvo, sem finalidade declarada. A validação contra SQL Server real deve ser executada no job de integração do CI/HML.
- `BASELINE_FONTE_UNICA` permanece Gold; o campo físico passou a `fontes_distintas`.

## Cobertura v3.42 — integridade de CPF compartilhado

- `IdentityResolutionCoordinatorTests` verifica que CPF já associado passa pela decisão de consistência e que conflito determinístico não cai no linkage probabilístico.
- `CpfIdentityConsistency` cobre a política `CPF_CORE_CONSISTENCY_V1`: nome `LOW` + data de nascimento diferente bloqueia; variação de nome com mesma data e diferença isolada de data não bloqueiam por si sós.
- `ProcessorRepositoryTests.Shared_cpf_conflict_materializes_fact_in_gold_without_canonical_assignment` simula CPF de adulto informado para criança e exige `CONFLITO / CPF_COMPARTILHADO_SUSPEITO`, fato preservado em Silver **e Gold**, `cpf_declarado` congelado, `pessoa_uuid=NULL`, `estado_atribuicao_identidade=CONFLITO_IDENTIDADE` e pendência/divergência institucional.
- O resolver SQL falha fechado com `CPF_NUCLEO_EXISTENTE_INDISPONIVEL` quando um CPF já mapeado não possui núcleo carregável para confronto.
- `SeedDatabaseTests` valida a coluna `identidade.vinculo_fonte.motivo` e a constraint `ck_vinculo_status_uuid`, que impede `CONFLITO`/`NAO_RESOLVIDO` de carregar UUID.
- Estes testes foram adicionados ao repositório, mas precisam ser executados no CI/HML porque o ambiente usado para empacotar a distribuição não possui SDK .NET.


## CI v3.39

O workflow possui dois gates: unitário/HTTP e integração SQL. O job SQL sobe SQL Server 2022, cria `JornadaTest`, executa DDL + seed reais pelos testes `Integration` e valida índices/views. O build roda com `-warnaserror` e o CI audita vulnerabilidades NuGet transitivas.


### Referência Territorial v3.33

Os testes Integration validam a remoção dos objetos geográficos residenciais legados e a precedência da view `silver.v_pessoa_referencia_territorial`: referência explícita vence fallback DOMICILIAR; entre referências explícitas, `COMPROVADO` vence `DECLARADO` e, em igualdade, prevalece a mais recente.

## Resiliência do Processor v3.33

Os testes SQL de integração cobrem lease/heartbeat, recuperação de lease expirado, fencing de worker antigo e a transição retry -> `POISON` ao atingir o limite configurado de tentativas. O worker não depende mais apenas de `atualizado_em` para detectar reserva abandonada.


## Coordenação serial do corpus v3.39

- `Jornada.Api` não participa do gate: Entregas/Bronze continuam sendo recebidos durante jobs analíticos.
- `Jornada.Processor.Worker` obtém `Jornada.Pipeline.Corpus` por um lote e não reserva lote quando existe intenção exclusiva.
- `GENERATE_DRAFT` e `Jornada.Linkage.Runner` obtêm `Jornada.Pipeline.ExclusiveRequest` e depois o corpus pelo job inteiro.
- `PipelineCoordinationTests` verifica exclusão global entre lotes do Processor, que intenção exclusiva impede novos lotes, espera o lote corrente drenar e libera o Processor após o job.
- `ReadCommandTimeoutSeconds=900` e `FreezeUniverseCommandTimeoutSeconds=900` permanecem como limites de comandos SQL; não existe requisito de `ALLOW_SNAPSHOT_ISOLATION` na Fase 1.
- `pessoa_observacao_id_high_watermark` substitui o nome legado `pessoa_observacao_id_max_snapshot`; `linkage_run_item` continua materializando o universo auditável.


BronzeStorage v3.39: provider inicial `FileSystem`; raiz física configurável em `BronzeStorage:RootPath`; chave lógica `sha256/ab/cd/<sha256>.zip`; bytes fora do SQL Server.


Hardening v3.39: testes verificam rejeição de objeto canônico corrompido mesmo com tamanho idêntico, contrato HTTP 503 para falhas de storage, ZIP determinístico e integração do GC com app lock por SHA-256 para impedir exclusão durante a janela de referência da API.


## Cobertura v3.39

A v3.39 acrescenta testes do staging configurável e substitui os testes do enriquecimento PRODAM por testes da territorialização de origem. Os contratos JSON Schema exigem situação geográfica explícita. A validação de integração deve cobrir ainda o cursor do Maintenance Worker, as views de BI e a retenção de `item_processado`.


### v3.39

- `SchemaCacheTests`: catálogo descobre nova `vN` sem restart; validator cacheado rejeita edição in-place; orçamento descompactado é compartilhado entre entradas.
- `ItemProcessedRetentionTests`: comprova em SQL Server que o expurgo preserva a última `RETRANSMITIDO` por identidade de origem.
- O Processor valida o teto de 2 GiB na mesma passagem do parse, sem a varredura descompactada redundante.

## v3.46

- `TrustedProxyConfigurationTests` cobre IPs explícitos e CIDR IPv4/IPv6, com falha fechada para rede inválida.
- `SeedDatabaseTests` exige estado de atribuição nas três views factuais, ausência de `v_bi_fatos_identidade` e metadados/ciclo de retenção da Bronze.
- A retenção de Entregas/Bronze permanece desabilitada por default; HML deve habilitá-la somente com `RetentionDays` aprovado e storage durável configurado.
- v3.46 adiciona `IdentityGovernanceTests`: abertura/aplicação de caso governado preserva fatos, não reabre lote e recompõe Gold; desfecho de divergência é restrito ao Gestor responsável.
- v3.46 reforça retenção Bronze com cenário de objeto compartilhado e `VerifyAsync` com ausência/corrupção.

## v3.47

- Remove testes e componentes de finalidade/PRIMEIRO_ATENDIMENTO.
- `AccessPolicyTests` passa a validar somente scopes, recurso e ausência de fronteira populacional.
- `SeedDatabaseTests` exige ausência de catálogo/colunas de finalidade e preservação da auditoria por Pessoa/recurso.


## v3.48

- `RegistryQualityTests` cobre regime de vigência determinado/indeterminado, janela permitida e ausência de configuração sem marcar silenciosamente como válido.
- O parser de Benefício aceita somente `dataInicioConcessao`, `dataFimConcessao`, `dataEventoConcessao` e `valorConcedido`; aliases genéricos não fazem parte do contrato.
- O QC estrutural de regime/janela é independente do `qc_status` específico do Tipo: `qc_especifico_implementado` continua refletindo somente a regra específica da política.
- DDL/seed migram colunas legadas por `sp_rename` guardado e passam a exigir regime coerente na ativação de novas versões de Tipo.


## v3.49

- `RegistryQualityTests` cobre janela configurada sem `dataInicioConcessao`, que deve produzir `NAO_VERIFICAVEL / CONCESSAO_JANELA_NAO_VERIFICAVEL_V1`.
- O SQL de qualidade deixa `concessao_na_janela_permitida` nulo nesse caso e expõe `concessao_janela_verificavel`; o percentual de conformidade usa somente linhas verificáveis.
- Replay técnico permanece ligado ao `tipo_registro_versao_id` persistido na Entrega; reavaliação normativa retroativa é operação distinta.


## v3.51

- `SeedDatabaseTests` valida `serving.v_pessoa` e a remoção da view de nomenclatura anterior no upgrade.
- A mudança é nominal: a Pessoa compartilhada em âmbito municipal continua comum às credenciais autorizadas, sem fronteira populacional por Gestor/Tipo.


## v3.52

- `Person_projection_exposes_gold_interpretation_metadata_outside_variable_schema` fixa que `fontesDistintas`, `estadoConcordancia` e `atualizadoEm` pertencem ao envelope de metadados da Gold, não ao JSON Schema variável da Pessoa.
- A cobertura existente de divergências continua restrita à fila aberta do Gestor e ao registro de desfecho; não há teste para endpoints de convergência/verificação porque eles não pertencem à Fase 1.


## v3.53

- `PipelineWatchdogTests` valida os limiares puramente observacionais do watchdog e garante que o avaliador não inventa tratamento de application lock órfão.
- A execução completa da suíte continua no CI/SQL Server 2022; o watchdog não depende de novo estado persistente.


## v3.54

- Docker Compose local fixa `MSSQL_PID=Developer` e compartilha SQL Server 2022 com a mesma família usada pelo CI.
- `local-test` executa a suíte completa contra `JornadaLocal`; `.env` permanece fora do Git.
- O release é ligado a commit/tag Git por `RELEASE_INFO.txt`.


## v3.55

- `PipelineCoordinationTests` marca os cenários com `KILL <SPID>` também como `FaultInjection`; um segundo cenário comprova liberação do gate do Processor após perda da sessão.
- `scripts/local-fault-injection.*` executa somente a categoria `FaultInjection` e grava TRX em `.local/test-results`.
- `database/Jornada_Dev_SyntheticScale.sql` produz massa reprodutível para Parameters/Runner sem usar dados pessoais reais.
- `tests/fixtures/bronze/restore-drill.zip` é objeto controlado do ensaio local de backup/restore.


## v3.62

A suíte acrescenta testes para chaves de instância de atributos `SINGLE/MULTI`, coexistência de dois telefones correntes no seed, evolução do índice Gold por `(pessoa_uuid, atributo_codigo, atributo_instancia_chave)` e `FUSAO_HISTORICA` 2→1 com `pessoa_uuid_sucessor`, migração de identificadores e eventos auditáveis.


## v3.63

A suíte acrescenta regressão de `FUSAO_HISTORICA` parcial: um UUID com vínculos correntes fora do caso deve falhar com `51119`. Os testes unitários de `TELEFONE_BR_CANONICO_V2` provam convergência entre formato nacional, `+55`, `0055` e E.164 brasileiro e falha fechada para número local sem DDD.


## v3.64

A suíte acrescenta gate estático que exige `51117`, `51118` e `51119` antes da primeira mutação de `sp_aplicar_caso_conflito_identidade`, regressão de `FUSAO_HISTORICA` com uma origem repartida entre dois destinos e verificação do upgrade telefônico baseado no valor original. O gate `local-ddl-upgrade` contém fixture real de chave legada com prefixo `00`.


## v3.65

- `PhoneNormalizationConformanceTests` executa a função SQL e a normalização C# sobre o mesmo arquivo `fixtures/phone/telefone-br-canonico-v2.json`, impedindo divergência silenciosa entre migração e ingestão.
- `TransversalAttributeInstanceKeyTests` consome os mesmos vetores no teste unitário do Processor.
- `IdentityGovernanceTests` injeta falhas por trigger e chama diretamente, sem transação externa, abertura de caso, aplicação de caso e correção CPF; qualquer escrita parcial deve ser revertida pela própria procedure.
- `OperationalAtomicityTests` injeta falha após a primeira escrita para provar rollback direto de `sp_recalcular_entrega` e `sp_sincronizar_atribuicao_fatos`.
- `ProcessorRepositoryTests` injeta falha no recálculo da Entrega e prova que `MarkRejectedAsync` também reverte a mudança anterior do lote.
- `SqlGovernanceArtifactTests` e `scripts/technical-closure-gate.py` exigem o padrão de ownership transacional também em `sp_sincronizar_atribuicao_fatos` e `sp_recalcular_entrega`.
- Os caminhos de retry/poison/rejeição/quarentena do Processor mantêm atualização do lote e recálculo da Entrega dentro da mesma transação.


## v3.67

- `SqlGovernanceArtifactTests` fixa o contrato estrutural da recomposição: fonte materializada, `MERGE ... HOLDLOCK`, ownership transacional e vedação à antiga referência pós-CTE.
- `IdentityGovernanceTests.Recompose_gold_person_executes_source_and_no_source_paths` executa diretamente os dois ramos que importam para a regressão do CTE.
- `database/Jornada_Runtime_Smoke.sql` complementa a suíte .NET com uma prova mínima executável pelo próprio SQL Server e é chamado pelo CI, teste local e gate de upgrade.


## v3.68

- `EmailNormalizationConformanceTests` compara `ref.fn_email_canonico_v2` e o Processor sobre `fixtures/email/email-canonico-v2.json`, inclusive Unicode composto/decomposto e caracteres não ASCII.
- `IdentityGovernanceErrorContractTests` cobre diretamente 51110–51118; o contrato 51114 exige explicitamente o erro governado, não erro de PK. 51119 permanece coberto em `IdentityGovernanceTests`.
- Fixtures determinísticas críticas de identidade falham quando ausentes; `Assert.Ignore` permanece apenas para ausência explícita da conexão SQL de integração.
- `ApiReadinessTests` exige o schema corrente e valida o probe completo após aplicar o DDL.
- `SqlGovernanceArtifactTests` fixa os marcadores Base 3.62/Solution 3.68, e-mail fail-closed e a ordem de 51114.

## v3.69

Nenhum contrato funcional novo. A release acrescenta gates de proveniência/promoção; a suíte funcional da v3.68 permanece íntegra. Em tag de release, a execução desta suíte é pré-requisito do job `release-promotion`.

## Evidência crítica de execução — engenharia v3.70

Unit, Integration e FaultInjection produzem TRX dedicados e passam por `scripts/test-evidence-gate.py --forbid-skipped`. Em CI, qualquer `NotExecuted/Skipped/Inconclusive` nessas suítes é falha de promoção. Ausência de `JORNADA_TEST_SQL_CONNECTION` pode continuar causando `Assert.Ignore` em execução local ad hoc, mas não constitui evidência de release. `Assert.Ignore` determinístico por seed/contrato ausente é proibido pelo `technical-closure-gate.py`.


## Engenharia v3.73

`DeterministicPropertyTests` executa propriedades determinísticas com seeds fixos. `IdentityReplayInvariantTests` prova replay da recomposição Gold e invariantes globais. `PipelineCoordinationTests` inclui oito contendores simultâneos para o gate do corpus. `PossibilityRuleEngineTests` prova o motor/dry-run sem publicar regras institucionais.

## Engenharia v3.80 — Unit/OpenAPI runtime sem SQL

Os testes `Unit` e `OpenApiRuntime` da API não abrem mais SQL Server. `IApiAuditSink` e
`ISqlReadinessProbe` são implementações SQL na aplicação, mas `WebApplicationFactory` substitui
esses pontos por `InMemoryApiAuditSink` e `InMemorySqlReadinessProbe` na suíte unitária.
Isso elimina dependência acidental de `JornadaDev`, credenciais Windows ou disponibilidade de banco
para testes de autenticação/pipeline/contrato HTTP.

A categoria `Integration` continua deliberadamente dependente de SQL Server real. Ela valida T-SQL,
constraints, procedures, rollback, `sp_getapplock`, concorrência e invariantes que um provider em
memória não reproduziria com fidelidade.

A v3.80 também ativa analyzer fail-closed: qualquer diagnóstico CA não aceito explicitamente em
`.editorconfig` passa a bloquear o build. A justificativa das exceções está em
`docs/Analyzer_Policy_v3.80.md`.
