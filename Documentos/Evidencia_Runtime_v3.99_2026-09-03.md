# Evidência Runtime — v3.99 — 03/09/2026

## Origem

Execução real reportada pelo operador usando `scripts/local-validate-release.ps1` da Solution Engenharia v3.99.

Esta evidência é externa ao ambiente de empacotamento. O ambiente usado para montar a v4.00 continua sem .NET, Docker ou PowerShell e não reivindica ter repetido essa execução.

## Resultado consolidado reportado

```text
VALIDAÇÃO LOCAL RELEASE CONCLUÍDA COM SUCESSO
ScriptVersion:        2026.09.03-v3.99
Restore Solution:     OK
Restore Unit:         OK
Restore Integration:  OK
Build Solution:       OK
Build Unit:           OK
Testes Unit:          OK
Docker:               OK
Imagem SQL/digest:     OK
SQL engine + CHECKDB: OK
Build Integration:    OK
Testes Integration:   OK
```

Marcadores normalizados para rastreabilidade:

```text
V399_LOCKED_RESTORE_SOLUTION_PASS
V399_LOCKED_RESTORE_UNIT_PASS
V399_LOCKED_RESTORE_INTEGRATION_PASS
V399_BUILD_SOLUTION_PASS
V399_BUILD_UNIT_PASS
V399_UNIT_TESTS_PASS
V399_DOCKER_ENGINE_PASS
V399_SQL_IMAGE_DIGEST_PASS
V399_SQL_ENGINE_CHECKDB_PASS
V399_BUILD_INTEGRATION_PASS
V399_INTEGRATION_TESTS_PASS
V399_LOCAL_RELEASE_VALIDATION_PASS
```

## Interpretação

A execução fecha a pendência declarada na v3.99: o grafo NuGet commitado foi aceito por restore `--locked-mode`, a Solution e os projetos de teste compilaram, e as suítes Unit/Integration concluíram com sucesso sobre a baseline SQL Server validada pelo script canônico.

A síntese reportada não informa contagens individuais de testes nem `ProductVersion`; a v4.00 não inventa esses valores. A evidência anterior v3.95 continua preservada para as contagens históricas 153/153 Unit e 58/58 Integration, mas a confirmação relevante para os locks introduzidos/alterados na v3.99 é esta execução v3.99.

Esta evidência não prova compatibilidade com SQL Database in Microsoft Fabric. Esse passa a ser o experimento executável da v4.00.
