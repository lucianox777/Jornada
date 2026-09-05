# Evidência Runtime — v3.95 — 03/09/2026

## Origem

Execução real reportada pelo operador em Windows / Visual Studio 2022 Developer PowerShell, usando `scripts/local-validate-release.ps1` da Solution Engenharia v3.95.

Esta evidência é externa ao ambiente de empacotamento. O ambiente de empacotamento continua sem .NET, Docker, PowerShell ou SQL Server; portanto a distinção entre **evidência runtime externa** e **execução local de empacotamento** é preservada.

## Resultado consolidado

```text
V395_LOCKED_RESTORE_SOLUTION_PASS
V395_LOCKED_RESTORE_UNIT_PASS
V395_LOCKED_RESTORE_INTEGRATION_PASS
V395_BUILD_RELEASE_PASS
V395_BUILD_RELEASE_0_WARNINGS_0_ERRORS
V395_UNIT_153_OF_153_PASS
V395_DOCKER_ENGINE_OK
V395_SQL_IMAGE_DIGEST_OK
V395_SQL_IMAGE_CACHE_REUSED
V395_SQL_SERVER_PRODUCTVERSION_16.0.4265.3
V395_DBCC_CHECKDB_MASTER_OK
V395_INTEGRATION_BUILD_PASS
V395_INTEGRATION_58_OF_58_PASS
V395_LOCAL_RELEASE_VALIDATION_PASS
```

## Restore e build

- `dotnet restore Jornada.sln --locked-mode`: PASS / todos os projetos atualizados.
- restore locked explícito `Jornada.Tests`: PASS.
- restore locked explícito `Jornada.Integration.Tests`: PASS.
- build Release da Solution: PASS, **0 warnings / 0 errors**.
- build explícito Unit: PASS, **0 warnings / 0 errors**.
- build explícito Integration: PASS, **0 warnings / 0 errors**.

## Testes

- `Jornada.Tests`: **153 PASS / 0 FAIL / 0 SKIP / 153 total**.
- `Jornada.Integration.Tests`: **58 PASS / 0 FAIL / 0 SKIP / 58 total**.

## Docker e SQL Server

- Docker Engine: OK.
- imagem canônica exigida pela Solution:

  `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`

- cache local encontrado para o digest fixado; nenhum download foi necessário.
- SQL Server engine: **OK**.
- `ProductVersion`: **16.0.4265.3**.
- `DBCC CHECKDB(master)`: **OK**.

## Interpretação

A v3.95 fecha a sequência de execução real iniciada na v3.87 e, especificamente para a suíte Integration, converge de 30/58 na v3.90, 29/58 na v3.92, 54/58 na v3.93, 57/58 na v3.94 para **58/58 na v3.95**.

A evidência confirma a executabilidade local da cadeia canônica de restore locked, build, Unit, preflight Docker/SQL e Integration. Não transforma automaticamente calibrações HML, critérios de produção, RIPD ou demais gates externos em aprovados; esses itens continuam sujeitos às respectivas políticas.
