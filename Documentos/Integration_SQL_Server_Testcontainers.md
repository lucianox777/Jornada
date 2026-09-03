# Integration - SQL Server isolado com Testcontainers

**Base Normativa:** v3.62
**Solution Engenharia:** v3.90
**Data:** 02/09/2026


## Projeto dedicado

Desde a v3.83, Integration não compartilha o assembly de `Jornada.Tests`; na v3.84 a separação passa também a valer para as dependências. O projeto `Jornada.Integration.Tests` contém as fixtures SQL, a infraestrutura Testcontainers e os testes de semântica do Database Engine. `Jornada.Tests` permanece focado em Unit/OpenAPI runtime e não referencia mais `Testcontainers.MsSql`.

## Comportamento contratado

1. Sem `JORNADA_TEST_SQL_CONNECTION`, a suíte sobe SQL Server 2022 descartável via Testcontainers.
2. A imagem padrão está fixada por tag + digest:

   `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`

3. Com `JORNADA_TEST_SQL_CONNECTION`, a conexão é tratada como conexão-base do servidor. Por padrão, a fixture cria um banco exclusivo `JornadaIntegrationTest_<guid>` e publica connection string com `Pooling=false`.
4. Somente quando `JORNADA_TEST_SQL_USE_EXISTING_DATABASE=true` a suíte usa literalmente o banco externo informado. Esse modo deve ser usado apenas quando CI/HML já provisionou banco exclusivo para o job.
5. Ao final, o banco criado pela fixture é removido e a variável de conexão é restaurada.
6. O bootstrap funcional do schema permanece nas fixtures de domínio existentes; a fixture de infraestrutura não duplica `Jornada_Fase1.sql` ou seed.

## Isolamento e concorrência

A suíte é serializada para impedir concorrência acidental entre fixtures que compartilham o mesmo banco. Testes de lock/contenção criam concorrência deliberadamente dentro do próprio caso, usando múltiplas `SqlConnection`/tasks.

## Dependência NuGet

A suíte requer `Testcontainers.MsSql` versão `4.14.0`.

A dependência está declarada somente em `tests/Jornada.Integration.Tests/Jornada.Integration.Tests.csproj`. O ambiente de empacotamento da v3.90 não possui .NET SDK e **não alega ter executado restore**. Os `packages.lock.json` permanecem byte-a-byte herdados da linha v3.84; a execução externa da v3.88 aceitou restore `--locked-mode` também para o projeto Integration. A promoção continua exigindo restore bloqueado em ambiente confiável. A origem é auditável em `Solution/config/release/nuget-lock-provenance.json`.

## Friend assemblies

Os testes de integração exercitam internals deliberadamente sem torná-los API pública. A v3.85 adiciona `InternalsVisibleTo("Jornada.Integration.Tests")` em `Jornada.Api`, `Jornada.Processor.Worker` e `Jornada.Bronze.Maintenance.Worker`, mantendo também o friend assembly histórico `Jornada.Tests` onde já existia.

## Execução local

```powershell
.\scripts\apply-testcontainers-integration.ps1 `
  -RepositoryRoot C:\Users\lucia\source\repos\Jornada `
  -RunTests
```

Sem conexão SQL externa, Docker Desktop deve estar disponível em Linux containers.

## Isolamento e guard de nome — v3.90

A execução real da v3.88 mostrou que o container e a DLL Integration já eram iniciados corretamente, mas 53 dos 58 casos eram bloqueados pelos próprios guards fail-closed porque a fixture criava `JornadaIntegration_<guid>`, nome que não contém `Test`, `Dev` ou `Local`. A correção preservada desde a v3.89 corrige a origem do problema: o banco descartável passa a se chamar `JornadaIntegrationTest_<guid>`. Os guards de segurança **não foram relaxados**.

Os scripts canônicos `scripts/local-clean.ps1` e `scripts/local-validate-release.ps1` também passam a fazer parte da Solution. O validador restaura explicitamente os dois projetos de teste em `--locked-mode`, compila cada assembly necessário e força o caminho Testcontainers descartável para impedir que um override residual do shell aponte os testes para um banco não isolado.

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
$env:JORNADA_TEST_SQL_CONNECTION = "Server=...;Database=JornadaIntegrationTest_build_123;..."
$env:JORNADA_TEST_SQL_USE_EXISTING_DATABASE = "true"
```

Nesse modo o pipeline assume explicitamente a responsabilidade pelo isolamento.

## Provas de infraestrutura

`SqlServerRuntimeIntegrationTests` valida engine SQL Server 2022, isolamento, `Pooling=false`, rollback, CHECK 547, lock real e timeout 1222.
