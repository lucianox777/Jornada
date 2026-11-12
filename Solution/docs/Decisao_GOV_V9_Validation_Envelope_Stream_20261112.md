# GOV-V9-VALIDATION-ENVELOPE-STREAM-20261112 — seleção TRAIN acima de 2 GiB

**Autoridade:** Product Owner, 12/11/2026. **Status:** correção autorizada, proposta para integração.

## Contexto e evidência
A execução TF4 100K #43159426682 concluiu TRAIN e falhou ao iniciar VALIDATION independente: `System.IO.IOException: The file is too long` em `File.ReadAllBytesAsync(selectionFile)`, método `V9NestedCalibrationCli.ValidateAsync`. O envelope de seleção pode conter avaliações volumosas, mas a admissão de VALIDATION exige apenas campos de identidade, integridade e finalistas.

## Decisão aprovada
Substituir a leitura integral por SHA-256 calculado em fluxo sobre os bytes originais, preservando conferência exata do nome content-addressed, e desserialização assíncrona em fluxo para DTO restrito aos campos `result.Version`, `result.ExternalValidationUsed`, `result.TestUsed`, `result.PlanSha256`, `result.FinalistSnapshotShas`, `originalObservationsSha256` e `producerSeed`. Não materializar avaliações. Preservar bloqueios fail-closed de identidade, plano, snapshots e reserva do holdout antes de ler atributos de VALIDATION.

## Impactos, precedência e limites
Não alterar TF4, ETA, seleção científica, dados ou critérios de aprovação. A leitura em fluxo elimina a limitação específica de `ReadAllBytesAsync` sobre o envelope, mas não comprova que outras etapas aceitam arquivos grandes. Exigir CI e execução física de envelope >2 GiB antes de declarar correção operacional comprovada. A implementação prevalece sobre a leitura integral anterior; não altera a decisão GOV-V9-LOG-HIPOTESE-CONSOLE-20261112.

## Rastreabilidade
Execução #43159426682; PR #1454 (anterior, conflitante após integração da PR #1455); branch `fix/v9-validation-stream-after-console-20261112`; `Solution/src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCli.cs`; índice de decisões vigentes; GOV-DOC-01.
