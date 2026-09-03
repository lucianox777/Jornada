# Jornada - testes técnicos reproduzíveis — base normativa v3.62 / engenharia v3.72

## Escopo

Este runbook cobre instrumentos de engenharia reproduzíveis. Os quatro instrumentos originais da v3.55 permanecem; a engenharia v3.60 acrescenta gates de contrato/DDL/E2E e avaliação metodológica DEV/HML:

1. massa sintética determinística + harness de desempenho do Parameters Worker/Runner;
2. fault injection do gate serial `sp_getapplock`;
3. ensaio local de backup/restore SQL + Bronze;
4. gate OpenAPI × Minimal API;
5. ensaio de upgrade DDL + fingerprint estável;
6. E2E HTTP → Bronze → Processor → Silver → Gold → Serving → HTTP;
7. `Jornada.Linkage.Evaluation` para blocking V1/V2 e transportabilidade de `m`;
8. execução contínua dos gates aplicáveis no CI.

Os instrumentos são exclusivos de Development/HML. Não definem capacidade de Produção, RPO/RTO, thresholds de linkage ou configuração do scheduler corporativo.

## Pré-requisitos

- Docker Compose v2;
- .NET SDK 8;
- SQL Server 2022 Developer do `docker-compose.yml`;
- shell POSIX ou PowerShell;
- Python 3 para gate OpenAPI e fixture E2E.

O banco local é destrutível. `local-scale` executa `local-db reset` antes de gerar a massa.

## 1. Massa sintética e escala

### Perfis

```text
smoke    Gold=10.000      pares independentes=6.000    sem CPF=5.000
medium   Gold=100.000     pares independentes=20.000   sem CPF=25.000
million  Gold=1.000.000   pares independentes=50.000   sem CPF=100.000
```

Execução:

```powershell
.\scripts\local-scale.ps1 -Profile smoke
.\scripts\local-scale.ps1 -Profile medium
.\scripts\local-scale.ps1 -Profile million
```

ou:

```bash
./scripts/local-scale.sh smoke
```

O gerador é `database/Jornada_Dev_SyntheticScale.sql`. Ele cria:

- população `gold.pessoa` determinística;
- duas observações independentes por parte da população, em Gestores distintos, ligadas por `CPF_DETERMINISTICO`, para a amostra m;
- observações sem CPF para o Runner;
- 10% das observações sem CPF com data de nascimento deliberadamente fora do universo Gold, exercitando `SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO`;
- variações sintéticas reprodutíveis de nome e nome da mãe.

O harness executa na sequência:

```text
reset local
-> seed sintético
-> GENERATE_DRAFT
-> VALIDATE
-> ACTIVATE
-> Runner MODEL_VALIDATION (publish=false)
-> relatório JSON
```

A evidência fica em `.local/performance/scale-<perfil>-<utc>.json`, com cópia estável `scale-<perfil>-latest.json`. Desde a v3.70, `performance-evidence-gate.py` valida automaticamente coerência das contagens/status e que as durações foram realmente medidas. O gate não fixa thresholds de desempenho: limites P95/P99/capacidade continuam dependentes de HML e SLA acordado.

### Perfil customizado

Defina:

```text
JORNADA_SCALE_PEOPLE
JORNADA_SCALE_PAIRED
JORNADA_SCALE_PENDING
JORNADA_SCALE_SAMPLE          opcional
JORNADA_SCALE_POOL            opcional
JORNADA_SCALE_PARALLELISM     opcional
JORNADA_SCALE_BATCH_SIZE      opcional
JORNADA_SCALE_SEED            opcional
```

Depois execute `local-scale ... custom`.

Os resultados locais não substituem a homologação com infraestrutura representativa da PRODAM. Servem para detectar regressões, validar índices/algoritmos e tornar o ensaio repetível.

## 2. Fault injection do gate serial

```powershell
.\scripts\local-fault-injection.ps1
```

ou:

```bash
./scripts/local-fault-injection.sh
```

Os testes de categoria `FaultInjection` abrem leases reais do `SqlPipelineCoordinator`, executam `KILL <SPID>` por uma segunda sessão SQL e comprovam:

- cancelamento de `LostToken` pelo heartbeat;
- `IsLost=true`;
- liberação automática do `sp_getapplock` pelo encerramento da sessão SQL;
- possibilidade de outro Processor obter o gate após a perda.

O harness não simula sucesso após perda de coordenação: a propriedade esperada é fail-closed.

Evidência NUnit/TRX: `.local/test-results/fault-injection.trx`. Desde a v3.70, `test-evidence-gate.py --forbid-skipped` torna skip/não executado uma falha para esta categoria crítica; desde a v3.71 a mesma política vale também para as suítes Unit e Integration completas. No CI o SQL Server usa `sa`, portanto falta de permissão para `KILL` não é aceita como justificativa para ignorar o teste.

## 3. Backup/restore drill

```powershell
.\scripts\local-backup-restore-drill.ps1
```

ou:

```bash
./scripts/local-backup-restore-drill.sh
```

O ensaio cria uma referência Bronze controlada a partir de `tests/fixtures/bronze/restore-drill.zip`, verifica a origem, executa `BACKUP DATABASE ... WITH CHECKSUM`, arquiva a raiz Bronze local, restaura o banco em `JornadaRestoreDrill` e verifica novamente o mesmo objeto com `Jornada.Bronze.Verify`.

`Jornada.Bronze.Verify` aceita em v3.55:

```text
--entrega-id <UUID>
--minimum-count <N>
```

O filtro existe para o drill controlado. Sem filtro, o executável continua verificando todas as referências persistidas, comportamento esperado para um restore real.

A evidência fica em `.local/backup-drill/<utc>/report.json`, acompanhada do backup SQL, arquivo Bronze e saídas de verificação. Na v3.70 o drill injeta, após o restore positivo, dois faults obrigatórios: objeto físico ausente (`MISSING`) e objeto adulterado (`DIVERGENT`). Ambos precisam falhar fechado; em seguida o objeto íntegro é restaurado e uma verificação final deve voltar a passar. `bronze-restore-evidence-gate.py` valida esse ciclo completo.

O drill local prova o procedimento de aplicação; não homologa storage corporativo, retenção de backups, RPO/RTO ou plano de desastre da PRODAM.


## 4. Gate OpenAPI × código

```powershell
python .\scripts\openapi-contract-gate.py
```

ou:

```bash
python3 ./scripts/openapi-contract-gate.py
```

O gate extrai as declarações `MapGet/MapPost/...` do `Jornada.Api/Program.cs`, normaliza constraints de rota e exige igualdade exata de método+path com `openapi/jornada-v1.openapi.json`. Também valida `responses` e unicidade de `operationId`. O `local-test` e o job `unit` do CI executam esse gate antes do build.

## 5. Upgrade DDL + fingerprint estável

```powershell
.\scripts\local-ddl-upgrade.ps1
```

ou:

```bash
./scripts/local-ddl-upgrade.sh
```

O ensaio cria banco descartável, aplica `database/baselines/Jornada_Fase1_v3.65.sql` (DDL congelado da Solution v3.65), insere uma sentinela, calcula fingerprint estrutural, aplica o DDL atual duas vezes e exige preservação da sentinela e fingerprint idêntico entre a primeira e a segunda aplicação. A evidência fica em `.local/ddl-upgrade/`.

O fingerprint inclui colunas/defaults, índices, módulos SQL, checks e FKs. Ele prova idempotência estrutural no ambiente ensaiado; não substitui plano corporativo de migração/rollback.

## 6. E2E local da borda ao retorno HTTP

```powershell
.\scripts\local-e2e.ps1
```

ou:

```bash
./scripts/local-e2e.sh
```

O teste recria `JornadaLocal`, compila a Solution, inicia API e Processor, aguarda `/health/ready`, gera ZIP determinístico da fixture `AA01_v2` e verifica HTTP → Bronze → Silver → Gold → Serving → consulta HTTP. Depois prova duas propriedades distintas: replay da mesma `Idempotency-Key` retorna a mesma Entrega; mesmos bytes sob nova chave criam nova Entrega lógica, mas os itens são `RETRANSMITIDO` e não nasce segunda versão Gold vigente. Evidência e logs ficam em `.local/e2e/`.

## 7. Avaliação metodológica de linkage — DEV/HML somente

`Jornada.Linkage.Evaluation` recebe CSV rotulado `pessoa_observacao_id,pessoa_uuid_verdade` para observações sem CPF. O executável faz somente `SELECT` nas tabelas operacionais, compara blocking V1 exato com V2 candidato por janela de nascimento e compara as distribuições de concordância usadas em `m` entre pares CPF-ancorados independentes e a população SEM_CPF rotulada.

```bash
dotnet run --project src/Jornada.Linkage.Evaluation -- \
  --labels .local/linkage-labels.csv \
  --output .local/linkage-evaluation.json \
  --birth-window-days 7
```

As distribuições `m` usam a mesma família de estados (`EXACT/HIGH/MEDIUM/LOW`) e suavização Dirichlet/Laplace do Parameters Worker; `--smoothing-alpha` tem default `0.5`. O relatório sempre declara `purpose=DEV_HML_ONLY_NO_PUBLICATION` e as salvaguardas: não cria `linkage_run`, não escreve `IDENTITY_MAP/vinculo_fonte`, não atualiza Gold e V2 é apenas evidência experimental. Nenhum resultado é promovido automaticamente a parâmetro/modelo.

No CI, após a criação do corpus `SCALE`, o smoke reproduzível é:

```bash
./scripts/linkage-evaluation-smoke.sh
```

Ele gera os rótulos sintéticos a partir da relação determinística da massa SCALE, executa o Evaluation e compara fingerprints operacionais antes/depois para detectar qualquer regressão de escrita acidental.

## 8. CI

O workflow Git executa os jobs permanentes `dependency-lock`, `unit`, `integration-sql`, `harness-smoke`, `ddl-upgrade` e `e2e`. Em tags de release (ou execução manual) também executa `bronze-restore-drill` e `scale-harness`; a promoção da tag depende deles.

- `harness-smoke` aplica DDL/seed, gera corpus sintético pequeno, cria/valida/ativa um modelo, executa `MODEL_VALIDATION` sem publicação e roda `scripts/linkage-evaluation-smoke.sh` com 100 rótulos sintéticos. O smoke do Evaluation exige `purpose=DEV_HML_ONLY_NO_PUBLICATION`, métricas V1/V2 e de transportabilidade e fingerprint idêntico de `identidade.linkage_run`, `identidade.vinculo_fonte` e `gold.pessoa` antes/depois.
- `ddl-upgrade` executa `scripts/local-ddl-upgrade.sh` e publica `.local/ddl-upgrade/` como artifact do workflow.
- `e2e` executa `scripts/local-e2e.sh` e publica `.local/e2e/`, incluindo logs e `evidence.json`, como artifact do workflow.

Os fault-injection tests também pertencem à categoria `Integration`, mas desde a v3.70 o job SQL os executa uma segunda vez com filtro dedicado, gera TRX próprio e exige zero skips via `test-evidence-gate.py`. Desde a v3.71, o job Unit e a execução Integration completa também geram TRX próprios e exigem zero skips. Isso evita que uma fixture/permissão inadequada deixe um teste crítico verde por não execução. O job `unit` executa também `powerbi-static-gate.py`, que valida estruturalmente PBIP/PBIR/TMDL e as 22 páginas; a abertura no Desktop homologado continua externa.

## Segurança

- massa inteiramente sintética; nenhum CPF/nome real;
- scripts recusam bancos que não sejam explicitamente locais/teste quando usam a suíte de integração;
- artefatos em `.local/` não entram no Git;
- SQL Server Developer permanece restrito a desenvolvimento/teste/demonstração;
- nenhuma rotina de fault injection deve ser apontada para HML/Produção sem procedimento específico e autorização operacional.

## Supply chain / NuGet / SBOM — engenharia v3.60

O bootstrap confiável do grafo NuGet é:

```bash
./scripts/nuget-lock-bootstrap.sh
```

Ele exige SDK `8.0.424`, executa restore limpo com geração de `packages.lock.json`, valida todos os locks e `contentHash`, repete o restore em `--locked-mode` e grava evidência em `.local/nuget-lock/`. No CI, o job `dependency-lock` publica esse conjunto e os jobs que compilam/testam reutilizam o mesmo arquivo compactado.

A SBOM é produzida somente depois do restore bloqueado:

```bash
dotnet list Jornada.sln package --include-transitive --format json > .local/sbom/dotnet-packages.json
python3 scripts/generate-sbom.py --input .local/sbom/dotnet-packages.json --output .local/sbom/sbom.cdx.json
```

O resultado é CycloneDX 1.5 e deve ser arquivado junto à evidência do build. Os pins de Actions/SDK/SQL Server estão em `supply-chain/pins.json`.


### Gate de upgrade de engenharia v3.64→v3.65 sobre base normativa v3.60

O `local-ddl-upgrade` aplica o baseline v3.65 (DDL/seed congelados da Solution v3.65), executa o seed DEV e força uma fixture telefônica legada `005511999990001` cujo valor original é `00 55 11 99999-0001`. Após o DDL atual, exige Silver e Gold em `5511999990001`, além de verificar a função SQL V2 com `00` e `+55`. O DDL é reaplicado para provar idempotência.


### Gate de fechamento técnico v3.65

Antes de restore/build, o CI e `local-test` executam:

```bash
python3 scripts/technical-closure-gate.py
```

O gate exige ownership transacional nas cinco procedures multi-tabela cobertas pelo fechamento, transação explícita nos caminhos de retry/falha do Processor e o contrato compartilhado de trim/vetores de `TELEFONE_BR_CANONICO_V2`. O gate é estático; a prova de execução continua sendo `integration-sql`/`local-ddl-upgrade` em SQL Server real.

A integração `PhoneNormalizationConformanceTests` usa exatamente `tests/fixtures/phone/telefone-br-canonico-v2.json` tanto como expectativa do C# quanto da função SQL. Os testes de governança usam triggers de falha para demonstrar rollback quando as procedures são chamadas diretamente sem transação externa.


### Gate de execução semântica SQL v3.67

`database/Jornada_Runtime_Smoke.sql` usa `sys.sp_refreshsqlmodule` e executa `sp_recompor_gold_pessoa` com e sem fonte corrente. O smoke é transacional e termina em `ROLLBACK`, portanto não deixa fixture persistida. Ele é obrigatório no job `integration-sql` e no `local-ddl-upgrade`.


### Fechamento v3.68

Além dos gates anteriores, a v3.68 exige vetores compartilhados de `EMAIL_CANONICO_V2`, testes diretos para 51110–51118 (51119 permanece com regressão direta já existente), validação de 51114 antes do `INSERT @o`, readiness por marcadores exatos Base 3.62/Solution 3.68 e SBOM derivado de `RELEASE_INFO.txt`. O upgrade parte do baseline DDL/seed v3.65 e verifica telefone V2, e-mail V2, marcadores de schema e o runtime smoke.

`predecessor-integrity-gate.py` compara SHA-256 do artefato efetivamente fornecido com `RELEASE_INFO.txt`. O modo `--require-all-predecessors` é o gate de promoção: predecessor ausente ou hash divergente deve falhar, nunca ser substituído silenciosamente por outro pacote.

## 9. Fechamento pós-medição de HML — engenharia v3.72

A v3.72 separa três estados que antes ficavam misturados no Markdown:

1. **medição válida** — o relatório tem estrutura/coerência comprovadas;
2. **critério aprovado** — baseline/política possui responsável, data e evidência com SHA-256;
3. **aprovação técnica** — a medição real está dentro do critério aprovado.

### Desempenho

`performance-evidence-gate.py` continua aceitando relatório sem baseline aprovado para smoke/DEV. Quando `config/hml/performance-baseline.json` estiver `APROVADO`, execute:

```bash
python3 scripts/performance-evidence-gate.py .local/performance/scale-medium-latest.json \
  --baseline config/hml/performance-baseline.json \
  --require-baseline-approved
```

O baseline é por perfil e pode fixar `parametersGenerateMillisecondsMax`, `runnerMillisecondsMax`, `minimumGoldPeople` e `minimumPendingWithoutCpf`. Nenhum valor foi preenchido nesta release porque ainda não existe medição HML homologada.

### Blocking e transportabilidade de m

`linkage-evaluation-evidence-gate.py` valida o relatório do `Jornada.Linkage.Evaluation`, incluindo salvaguardas read-only, tamanho das amostras, recalls V1/V2 candidato, expansão média de candidatos e distâncias de transportabilidade (`nome`, `nomeMae`, `dataNascimento`). Com política HML aprovada:

```bash
python3 scripts/linkage-evaluation-evidence-gate.py .local/linkage-evaluation.json \
  --policy config/hml/linkage-evaluation-policy.json \
  --require-policy-approved
```

A decisão permitida é `MANTER_V1` ou `SUBMETER_V2_PARA_REVISAO_NORMATIVA`. Mesmo no segundo caso, o gate **não** altera DDL, catálogo ou Runner; qualquer promoção de V2 continua exigindo decisão normativa explícita.

### Passagem integrada

```bash
JORNADA_HML_PERFORMANCE_REPORT=/evidencia/scale.json \
JORNADA_HML_LINKAGE_REPORT=/evidencia/linkage.json \
./scripts/hml-readiness-gate.sh
```

Enquanto os contratos estiverem `PENDENTE`, o modo estrito falha por desenho. Isso impede que a existência de um relatório seja confundida com homologação.


## Hardening v3.77 — contratos em runtime e meta-validação

Antes de promover uma release, além dos gates estáticos anteriores:

```bash
python3 scripts/json-schema-meta-gate.py --summary .local/json-schema-meta.json
python3 scripts/architecture-dependency-gate.py --summary .local/architecture-dependencies.json
python3 scripts/contract-backward-compatibility-gate.py --repo .. --summary .local/contract-backward-compatibility.json
```

No ambiente .NET, a categoria `OpenApiRuntime` deve executar sem skip e cobrir exatamente as 18 operações do contrato. O objetivo é confrontar o pipeline HTTP real com os status declarados, exigir JSON bem-formado quando houver corpo e conferir media type quando o OpenAPI o especificar.

Os analyzers do SDK estão fixados em `AnalysisLevel=8.0-recommended`. Enquanto não existir o primeiro baseline de compilação real, `CodeAnalysisTreatWarningsAsErrors=false` mantém CAxxxx como warning para permitir triagem; warnings do compilador continuam tratados como erro.

## Build-fix v3.78 — consolidação da primeira compilação real

A v3.78 incorpora defeitos concretos observados ao abrir/restaurar/compilar a v3.77 no Visual Studio 2022 com .NET SDK 8.0.424. Ela não deve ser tratada como build homologado até que a própria v3.78 seja compilada.

Primeiro ciclo após extrair a v3.78:

```powershell
dotnet --version
dotnet restore Jornada.sln --use-lock-file
dotnet build Jornada.sln
```

O SDK selecionado pelo `global.json` deve ser `8.0.424`. Se o build revelar novos erros, registrar a primeira causa e seus cascatas antes de alterar analyzers/warnings. Os `packages.lock.json` devem nascer de restore NuGet real; não são fabricados pelo pacote.

## Build/Unit-fix v3.79 — primeira bateria Unit real

A execução externa da v3.78, após o ajuste de `Microsoft.AspNetCore.Builder` em `TrustedProxyConfigurationTests`, atingiu build com zero erros. A primeira bateria Unit executou 99 testes: 95 passaram, 4 falharam e nenhum foi skipped. A v3.79 corrige as três causas-raiz dos quatro failures sem relaxar contratos de produção.

Primeiro ciclo após extrair a v3.79:

```powershell
dotnet --version
dotnet restore Jornada.sln --use-lock-file
dotnet build Jornada.sln
dotnet test .\tests\Jornada.Tests\Jornada.Tests.csproj --configuration Debug --no-build --filter "TestCategory=Unit" --logger "trx;LogFileName=unit-tests.trx"
```

Critério antes de avançar: `Failed=0`, `Passed=99`, `Skipped=0`. Se o build for feito em Release, usar a mesma configuração no `dotnet test`. O warning do `ApiAuditMiddleware` por ausência de banco local não deve ser confundido com a causa do teste HTTP; a evidência de Unit só é aprovada se o resultado TRX estiver verde.
