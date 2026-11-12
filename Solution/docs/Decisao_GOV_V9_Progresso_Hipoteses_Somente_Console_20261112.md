# GOV-V9-PROGRESSO-HIPOTESES-CONSOLE-20261112

**Autoridade:** Product Owner, 12/11/2026. **Status:** aprovada, pendente de integração da PR.

## Contexto e decisão

Na campanha V9 100K TF4, o PO determinou retirar a gravação de logs por hipótese ao término de cada avaliação, mantendo exclusivamente a mensagem de conclusão na saída do GitHub Actions. A gravação `hypothesis-progress.jsonl` e os arquivos individuais `hypotheses/hypothesis-XXXX.json` deixam de ser produzidos pelo callback de hipótese. O progresso físico `[V9 PROGRESS]`, com contagem, estado e ETA, permanece no console quando `V9_PHYSICAL_PROGRESS_LOG=1`. Logs agregados por lote, evidências científicas e snapshots necessários à seleção não são alterados.

## Justificativa e impactos

Evitar persistência redundante de mensagens de conclusão e reduzir I/O/arquivos durante execução longa, sem alterar algoritmos, hipóteses, métricas, seleção, blocking ou VALIDATION. O erro da execução #43159426682 ocorreu na VALIDATION em `File.ReadAllBytesAsync` para arquivo acima de 2 GiB; **não há evidência técnica de que os logs por hipótese tenham causado aquele erro**. Esta decisão não corrige, por si só, o limite de leitura da VALIDATION.

## Precedência e rastreabilidade

Esta decisão substitui apenas a exigência anterior de persistir progresso por hipótese em arquivos, descrita em `Decisao_GOV_V9_Progresso_Fisico_Hipoteses_20261111.md`; preserva o progresso visível no console. Implementação: `Solution/src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCli.cs`; workflow `.github/workflows/v9-v3-four-name-100k.yml` já habilita o progresso físico. A decisão aplica-se a novas execuções, não modifica artefatos históricos.
