# Evidência de Runtime — v3.90 — 2026-09-02

Origem: execução real fornecida pelo operador em Windows / Visual Studio Developer PowerShell / .NET SDK 8.0.424 / Docker.

## Resultado confirmado

- `scripts/local-clean.ps1` v3.90: concluiu a limpeza e preservou a imagem SQL Server.
- restore `--locked-mode` da Solution: PASS.
- restore `--locked-mode` de `Jornada.Tests`: PASS.
- restore `--locked-mode` de `Jornada.Integration.Tests`: PASS.
- build Release da Solution: PASS, 0 warnings / 0 errors.
- build explícito Unit: PASS.
- Unit: 153/153 PASS.
- Docker Engine: OK.
- build explícito Integration: PASS; `Jornada.Integration.Tests.dll` foi produzido.
- Integration: 58 casos executados; 30 PASS / 28 FAIL / 0 skipped.

## Grupos de falha observados e correção v3.91

1. SQL 208 / `Invalid object name 'im'` e contratos 51113–51118 recebendo 208: `UPDATE im` sem `FROM identidade.identity_map im` no DDL corrente.
2. `SequentialAccess` / ordinal 0 após ordinal 8: `ReserveNextAsync` lia campos fora de ordem com `CommandBehavior.SequentialAccess`.
3. `Lote reservado não pôde ser recarregado`: o banco compartilhado conservava mutações destrutivas da Bronze/seed entre casos; o seed v3.91 restaura suas três Entregas canônicas.
4. `ExecuteNonQueryAsync()` retornando `-1` e cenário transacional de divergência perdendo o registro: `SET NOCOUNT ON` / `SET XACT_ABORT ON` vazavam do bootstrap para a sessão reutilizada.
5. SQL 515 em `situacao_geografia`: fixture territorial omitia coluna NOT NULL.
6. Prova de isolamento: banco `JornadaIntegrationTest_<guid>` continha `Test`, mas não começava com `JornadaIntegration_`; v3.91 usa `JornadaIntegration_Test_<guid>`.

## Limite

Este documento registra a v3.90. As correções v3.91 ainda precisam de reexecução real; não há declaração de 58/58 PASS nesta release antes dessa evidência.
