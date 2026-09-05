# Estado de Engenharia — v3.93

- Base Normativa: **v3.62**
- SolutionSchema: **v3.68**
- Solution Engenharia: **v3.93**
- Predecessora: **v3.92**
- Estado: **correção de idempotência DDL aplicada; reexecução Integration pendente**

## Evidência v3.92

A execução real confirmou limpeza, restores `--locked-mode`, build Release com 0 warnings/0 erros e Unit 153/153. Os 58 Integration executaram: 29 passaram e 29 falharam.

As 29 falhas convergiram para o mesmo SQL Server 547: `ck_vinculo_metodo` era recriada por um bloco histórico de compatibilidade sem aceitar `CONFLITO_GOVERNADO`, embora esse valor seja válido no estado final introduzido depois pelo bloco v3.44.

## Correção v3.93

- o bloco de reentrada de `ck_vinculo_metodo` passa a aceitar `CONFLITO_GOVERNADO`;
- o `ck_vinculo_modelo` correspondente aceita o mesmo estado final (`score`, `modelo_id` e `pessoa_uuid` nulos);
- baselines históricos permanecem intactos;
- `technical-closure-gate.py` exige as duas definições pós-criação (reentrada + v3.44) com o domínio final, impedindo regressão estática.

## Pendência

Executar `scripts/local-clean.ps1` e `scripts/local-validate-release.ps1`. A correção será confirmada empiricamente se o bloqueador SQL 547 desaparecer; qualquer falha residual deve ser reagregada pelo novo sintoma, sem presumir 58/58 antes da execução.
