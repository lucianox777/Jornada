# Análise de performance V9 — preparação do replay (12/11/2026)

## Contexto
O PO solicitou prosseguir na investigação de otimizações do calibrador TF4/324, sem mudar o algoritmo científico. A campanha física vigente utiliza `V9NestedCalibrationCoordinator.EvaluateAsync` e `V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync`.

## Evidência no código
Antes desta proposta, cada hipótese elegível executava `frame.Where(x => x.Partition == "VALIDATION").ToArray()` e criava `c.Execution with { TotalComparedConfigurations = 1 }` dentro do laço. Os objetos são equivalentes entre hipóteses do mesmo grupo físico. A criação de assinaturas, a carga SQL, a materialização de pares e o ajuste FS são realizados por grupo; `EndToEndRepetitions=0` já desativa as repetições completas de benchmark na busca.

## Alteração proposta
Materializar uma vez por grupo `validationFrame` e `replayExecution`, reutilizando os dois valores nas chamadas a `EvaluateFrozenHoldoutAsync`. Nenhum replay é eliminado. Preservam-se a seleção de TRAIN/VALIDATION, todas as métricas, snapshots, checks de divergência, 324 hipóteses, TF4, AUTO_0, FDR, limites e tratamento de erro.

## Impactos, riscos e validação
A alteração reduz alocações e varreduras de memória, mas **não há evidência de ganho significativo no tempo total**: o custo dominante pode estar em SQL/FS/Rank-Resolve. É necessário CI, testes de equivalência e benchmark físico antes de atribuir ganho. A avaliação de compartilhamento de tabelas SQL entre conexões independentes permanece investigação separada, não autorizada nem implementada.

## Precedência, governança e rastreabilidade
Proposta de engenharia decorrente do pedido de investigação do PO, **não constitui aprovação de nova política científica ou arquitetural**. A precedência das decisões vigentes, inclusive GOV-DOC-01, GOV-V9-MARGIN-REUSE-20261111 e GOV-V9-RESTAURAR-PROGRESSO-BASELINE-20261112, permanece intacta. Branch `perf/v9-hoist-validation-replay-input-20261112`, arquivo `Solution/src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCoordinator.cs`. Não integrar na master antes dos gates e aprovação aplicáveis.
