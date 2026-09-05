# Estado de Engenharia — v3.95

- Base Normativa: **v3.62**
- SolutionSchema: **v3.68**
- Solution Engenharia: **v3.95**
- Predecessora: **v3.94**
- Estado: **único resíduo Integration tratado; preflight/recovery SQL Docker incorporado; reexecução pendente**

## Evidência v3.94

A execução real confirmou limpeza, restores `--locked-mode`, build Release com 0 warnings/0 erros e Unit 153/153. Os 58 Integration executaram: **57 passaram e 1 falhou**.

O único FAIL foi a prova de rollback do Processor. A exceção de entrada esperada já ocorreu corretamente; a falha restante vinha da verificação posterior contar dados canônicos preexistentes no lote reutilizado pelo seed.

## Correções v3.95

- a prova de rollback escopa Silver e item_processado aos identificadores `PROC-V325-ROLLBACK-A/B`, sem relaxar a exigência de zero linhas do cenário após rollback;
- identity_map/gold continuam escopados aos CPFs do cenário;
- `local-validate-release.ps1` valida a imagem SQL exata por digest, executa probe real + `DBCC CHECKDB(master)` e permite uma única recuperação segura por re-pull;
- falhas funcionais dos testes não disparam reinstalação da imagem SQL.

## Pendência

Executar `scripts/local-clean.ps1` e `scripts/local-validate-release.ps1`. Não há alegação de 58/58 para v3.95 antes dessa execução real.
