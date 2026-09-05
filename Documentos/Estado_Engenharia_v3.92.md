# Estado de Engenharia — v3.92

- Base Normativa: **v3.62**
- SolutionSchema: **v3.68**
- Solution Engenharia: **v3.92**
- Predecessora: **v3.91**
- Estado: **precisão documental corrigida; reexecução Integration das correções v3.91 pendente**

## Evidência existente

A v3.90 executou limpeza, restores `--locked-mode`, build Release e Unit 153/153 com sucesso. A suíte Integration executou 58 casos: 30 passaram e 28 falharam.

A v3.91 aplicou seis grupos de correções funcionais derivados desse log. A v3.92 não altera essas correções; apenas torna a documentação/proveniência precisa antes do próximo run.

## Correções de precisão v3.92

- SQL 208 descrito como `UPDATE im` sem `FROM identidade.identity_map im`;
- comentário do validador alinhado a `JornadaIntegration_Test_<guid>`;
- seis grupos v3.91 tratados como hipótese causal operacional a confirmar por reexecução, e não como prova já fechada de cobertura das 28 falhas;
- evidência externa de restore v3.90 mantida separada da assurance do ambiente de empacotamento `NO_RESTORE_CLAIMED`.

## Pendência

Executar `scripts/local-clean.ps1` e depois `scripts/local-validate-release.ps1` em ambiente .NET 8.0.424 + Docker. O runtime Integration só é fechado por nova evidência real; 58/58 PASS seria o fechamento ideal, mas qualquer falha residual deve ser reagregada por sintoma antes de nova correção.
