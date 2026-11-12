# GOV-V9-VALIDATION-ENVELOPE-STREAM-20261112 — seleção TRAIN acima de 2 GiB

**Autoridade:** Product Owner, 12/11/2026. **Status:** correção autorizada, proposta para integração.

## Contexto e evidência
A execução TF4 100K #43159426682 concluiu a etapa TRAIN e falhou ao iniciar VALIDATION independente: `System.IO.IOException: The file is too long` em `File.ReadAllBytesAsync(selectionFile)`, método `V9NestedCalibrationCli.ValidateAsync`. O envelope de seleção contém avaliações volumosas, mas a admissão da VALIDATION precisa apenas dos campos de identidade, integridade e finalistas.

## Decisão e implementação
Substituir a leitura integral do envelope por (1) SHA-256 calculado em fluxo sobre os bytes originais, preservando a conferência exata com o nome content-addressed; (2) desserialização assíncrona em fluxo para um DTO restrito aos campos `result.Version`, `result.ExternalValidationUsed`, `result.TestUsed`, `result.PlanSha256`, `result.FinalistSnapshotShas`, `originalObservationsSha256` e `producerSeed`. As avaliações não são materializadas. Manter os bloqueios fail-closed de integridade, identidade, plano, snapshots e reserva do holdout antes de ler atributos de VALIDATION.

## Impactos, limites e precedência
Não alterar o algoritmo, TF4, ETA, seleção científica, dados ou critérios de aprovação. O streaming evita a limitação da leitura integral em `byte[]`, sem prometer que outras etapas de VALIDATION não tenham limites próprios. Exigir CI e teste real de envelope >2 GiB antes de declarar validação operacional comprovada. Esta correção prevalece sobre a implementação de leitura integral no método de validação. A execução histórica permanece falha, não certificada e não ativada.

## Rastreabilidade
Execução #43159426682; `Solution/src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCli.cs`; decisão GOV-DOC-01; índice `Solution/docs/Indice_Decisoes_Vigentes.md`.
