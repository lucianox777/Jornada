# Evidência runtime — v3.89 — 02/09/2026

## Execução observada

O operador iniciou `scripts/local-clean.ps1` em Windows antes de executar a validação Release.

A limpeza abortou ao tentar remover:

`C:\Users\lucia\source\repos\Jornada\.vs\Jornada\CopilotIndices\17.14.1700.50054\CodeChunks.db`

O Windows informou que `CodeChunks.db` estava sendo usado por outro processo. A exceção ocorreu em `Remove-Item -Recurse -Force` dentro de `Remove-DirectoryIfExists`.

## Classificação

- Docker/build/test: **não avaliados nesta tentativa**, porque a limpeza abortou antes da validação.
- Causa: cache `.vs` da IDE bloqueado por processo externo, compatível com Visual Studio/Copilot/ServiceHub.
- Impacto de produto: nenhum dado de negócio ou schema envolvido.
- Correção v3.90: `.vs` passa a ser removido somente em best-effort; lock nesse cache gera warning e não interrompe a limpeza dos artefatos necessários.
