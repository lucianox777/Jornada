# Integration - SQL Server isolado com Testcontainers

**Natureza:** documentação operacional acumulativa  
**Última release selada de referência:** Base Normativa v3.64 / Solution Engenharia v4.05 / SolutionSchema v3.69  
**Base de schema normativa:** v3.62  
**Estado técnico desta branch:** SolutionSchema v3.70, candidato à consolidação v5.00; release/tag ainda não cortada  
**Data de revisão:** 10/09/2026

As seções identificadas por versões anteriores registram a evolução e as evidências históricas da infraestrutura de integração. Elas não substituem `RELEASE_INFO.txt` como fonte da última release efetivamente selada.

## Projeto dedicado

Desde a v3.83, Integration não compartilha o assembly de `Jornada.Tests`; na v3.84 a separação passa também a valer para as dependências. O projeto `Jornada.Integration.Tests` contém as fixtures SQL, a infraestrutura Testcontainers e os testes de semântica do Database Engine. `Jornada.Tests` permanece focado em Unit/OpenAPI runtime e não referencia mais `Testcontainers.MsSql`.

## Comportamento contratado

1. Sem `JORNADA_TEST_SQL_CONNECTION`, a suíte sobe SQL Server 2022 descartável via Testcontainers.
2. A imagem padrão está fixada por tag + digest:

   `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`

3. Com `JORNADA_TEST_SQL_CONNECTION`, a conexão é tratada como conexão-base do servidor. Por padrão, a fixture cria um banco exclusivo `JornadaIntegration_Test_<guid>` e publica connection string com `Pooling=false`.
4. Somente quando `JORNADA_TEST_SQL_USE_EXISTING_DATABASE=true` a suíte usa literalmente o banco externo informado. Esse modo deve ser usado apenas quando CI/HML já provisionou banco exclusivo para o job.
5. Ao final, o banco criado pela fixture é removido e a variável de conexão é restaurada.
6. O bootstrap funcional do schema permanece nas fixtures de domínio existentes; a fixture de infraestrutura não duplica `Jornada_Fase1.sql` ou seed.

## Isolamento e concorrência

A suíte é serializada para impedir concorrência acidental entre fixtures que compartilham o mesmo banco. Testes de lock/contenção criam concorrência deliberadamente dentro do próprio caso, usando múltiplas `SqlConnection`/tasks.

## Dependência NuGet

A suíte requer `Testcontainers.MsSql` versão `4.14.0`.

A dependência está declarada somente em `tests/Jornada.Integration.Tests/Jornada.Integration.Tests.csproj`. O ambiente de empacotamento da v3.96 não possui .NET SDK e **não alega ter executado restore**. Os `packages.lock.json` permanecem byte-a-byte herdados da linha v3.84; a execução externa da v3.95 confirmou restore `--locked-mode` da Solution, de `Jornada.Tests` e de `Jornada.Integration.Tests` com o grafo atual. A evidência externa é registrada separadamente da capacidade do ambiente de empacotamento em `Solution/config/release/nuget-lock-provenance.json`.

## Friend assemblies

Os testes de integração exercitam internals deliberadamente sem torná-los API pública. A v3.85 adiciona `InternalsVisibleTo("Jornada.Integration.Tests")` em `Jornada.Api`, `Jornada.Processor.Worker` e `Jornada.Bronze.Maintenance.Worker`, mantendo também o friend assembly histórico `Jornada.Tests` onde já existia.

## Execução local

```powershell
.\scripts\apply-testcontainers-integration.ps1 `
  -RepositoryRoot C:\Users\lucia\source\repos\Jornada `
  -RunTests
```

Sem conexão SQL externa, Docker Desktop deve estar disponível em Linux containers.

## Reentrada DDL e evidência runtime — v3.93

A execução externa da v3.92 confirmou restore/build/Unit e executou os 58 Integration: 29 PASS / 29 FAIL. Todas as 29 falhas registraram SQL Server 547 em `ck_vinculo_metodo` durante reaplicação do DDL. A v3.93 corrige o bloco de compatibilidade anterior ao v3.44 para aceitar `CONFLITO_GOVERNADO` em `ck_vinculo_metodo` e `ck_vinculo_modelo`, evitando rebaixamento temporário do domínio sobre banco já evoluído. Baselines históricos permanecem intactos.

## Isolamento e regressões de runtime — v3.92

A execução real da v3.90 confirmou que o Testcontainers, a DLL e os 58 casos Integration são executáveis no ambiente do operador: 30 passaram e 28 falharam. A correção funcional v3.91 mantém os guards que exigem `Test`/`Dev`/`Local` e usa `JornadaIntegration_Test_<guid>` para satisfazer simultaneamente a prova de prefixo `JornadaIntegration_`; a v3.92 apenas corrige documentação/proveniência.

O mesmo ciclo corrigiu cinco causas adicionais observadas nessa execução: referência ausente de `identity_map` no DDL, `SequentialAccess` com leitura fora de ordem no Processor, opções `NOCOUNT/XACT_ABORT` vazando do bootstrap, seed DEV não convergente após testes destrutivos e fixture territorial sem `situacao_geografia`.

Os scripts canônicos continuam sendo `scripts/local-clean.ps1` e `scripts/local-validate-release.ps1`; variantes temporárias como `local-validate-release-r4.ps1` não integram a release. Desde a v4.05, `local-validate-release.ps1` invoca `local-clean.ps1` automaticamente no início e produz TRX dedicados para Unit e Integration em `TestResults/Release/`; os nomes dos dois scripts canônicos permanecem inalterados.

## Compatibilidade de API Docker — v3.88

Testcontainers 4.14 usa Docker API `1.44` por padrão. A execução real da v3.87 encontrou Docker Server com API máxima `1.41`, causando falha no `OneTimeSetUp` antes de qualquer caso Integration. A v3.88 consulta `docker version --format {{.Server.APIVersion}}` antes de construir o container. Se `DOCKER_API_VERSION` já estiver definido, o valor do operador é preservado; caso contrário, somente quando o servidor anunciar versão inferior a 1.44 a fixture define a variável para esse máximo. Em servidores 1.44 ou superiores, o default do Testcontainers permanece inalterado.

## SQL externo com banco isolado automático

```powershell
$env:JORNADA_TEST_SQL_CONNECTION = "Server=...;Database=master;..."
dotnet test .\tests\Jornada.Integration.Tests\Jornada.Integration.Tests.csproj `
  --configuration Release
```

A credencial precisa poder criar/remover o banco temporário.

## Banco externo já provisionado

```powershell
$env:JORNADA_TEST_SQL_CONNECTION = "Server=...;Database=JornadaIntegration_Test_build_123;..."
$env:JORNADA_TEST_SQL_USE_EXISTING_DATABASE = "true"
```

Nesse modo o pipeline assume explicitamente a responsabilidade pelo isolamento.

## Provas de infraestrutura

`SqlServerRuntimeIntegrationTests` valida engine SQL Server 2022, isolamento, `Pooling=false`, rollback, CHECK 547, lock real e timeout 1222.

## Resíduos Integration — v3.94

A execução externa da v3.93 confirmou a correção de reentrada DDL e terminou em 54 PASS / 4 FAIL. A v3.94 tratou os quatro resíduos: poll concorrente `READPAST` com progresso eventual, contagens escopadas ao cenário de teste, validação `COMPROVADO` antes da constraint Silver e seed DEV de possibilidades convergente por linha. A execução posterior da v3.94 chegou a 57 PASS / 1 FAIL; o resíduo final é tratado na v3.95.

## Preflight e recuperação local — v3.95

`local-validate-release.ps1` lê a imagem SQL do `docker-compose.yml`, exige referência fixada por `@sha256`, reutiliza o cache quando presente e faz `docker pull` somente quando ausente. Antes dos Integration, um container descartável valida inicialização do SQL Server, `SERVERPROPERTY('ProductVersion')` e `DBCC CHECKDB(master)`.

Se esse probe do próprio engine falhar, a rotina tenta uma única recuperação: verifica se a imagem não está em uso por outro container, remove o image ID sem `--force`, baixa novamente o mesmo digest e repete o probe. Erros de DDL, constraints, testes ou código da Jornada não são tratados como corrupção da imagem.

## Fechamento runtime — v3.95 / incorporado na v3.96

A execução real da v3.95 concluiu com restore locked Solution/Unit/Integration PASS, build 0 warnings / 0 errors, Unit 153/153 PASS, digest da imagem SQL canônica OK, SQL Server ProductVersion 16.0.4265.3, `DBCC CHECKDB(master)` OK e Integration **58/58 PASS**. A v3.96 não modifica a infraestrutura Integration; apenas incorpora a evidência ao pacote e fecha a proveniência documental.
