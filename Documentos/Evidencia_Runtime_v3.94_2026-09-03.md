# Evidência de runtime — v3.94 — 2026-09-03

Fonte: execução externa real em Windows / Visual Studio Developer PowerShell, .NET SDK 8.0.424 e Docker SQL Server.

## Resultado observado

- `local-clean.ps1`: PASS; `.vs/CopilotIndices/CodeChunks.db` bloqueado foi apenas aviso best-effort e a limpeza continuou;
- `dotnet restore Jornada.sln --locked-mode`: PASS;
- restore locked explícito de `Jornada.Tests`: PASS;
- restore locked explícito de `Jornada.Integration.Tests`: PASS;
- build Release da Solution: PASS, **0 warnings / 0 errors**;
- `Jornada.Tests`: **153 PASS / 0 FAIL / 0 SKIP**;
- build explícito de `Jornada.Integration.Tests`: PASS, **0 warnings / 0 errors**;
- `Jornada.Integration.Tests`: **57 PASS / 1 FAIL / 0 SKIP / 58 total**.

## Única falha residual

`Transaction_failure_after_partial_persistence_rolls_back_silver_identity_and_gold` recebeu a `InvalidDataException` esperada, mas a verificação posterior contou 2 linhas em `silver.pessoa_observacao` e 2 em `ingestao.item_processado`, pois a query abrangia todo o lote canônico reaproveitado do seed e exigia zero absoluto.

Os identificadores de identity/gold do cenário já eram filtrados. O sintoma é consistente com contaminação da asserção por estado preexistente do seed, não com evidência de commit parcial do cenário de rollback.

## Interpretação incorporada à v3.95

A v3.95 mantém a prova de atomicidade, mas escopa as contagens Silver/item_processado aos códigos `PROC-V325-ROLLBACK-A` e `PROC-V325-ROLLBACK-B`. Se qualquer linha desse cenário sobreviver ao rollback, o teste continuará falhando.

A mesma release incorpora o hardening aprovado do SQL Docker: digest canônico, probe real do engine, `DBCC CHECKDB(master)` e uma tentativa controlada de re-pull somente quando o próprio SQL Server não consegue iniciar/operar.

Esta evidência não declara Integration PASS para v3.94 nem para v3.95.
