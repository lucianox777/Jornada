# V9 — instrumentação operacional por grupo SQL (12/11/2026)

**Estado:** implementação em PR, sujeita à CI; sem alteração de protocolo científico.

## Contexto e justificativa
Os prints de progresso mostram tempo decorrido e quantidade de hipóteses concluídas, mas não isolam o custo de preparação física por grupo. A campanha organiza requests em grupos, executa `V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync` por grupo e, depois, avalia replays individuais elegíveis. A comparação #43283688181 (10/22,3 min), #43292437608 (10/14,7 min) e #43295710416 (10/16,1 min) é observacional e não permite atribuir causalidade.

## Alteração
No coordenador, um `Stopwatch` iniciado imediatamente antes de `RunAsync` e encerrado após seu sucesso emite uma linha `[V9 SQL GROUP]` com lote, número de requests, opções, margens e `run_async_seconds`. A emissão usa o lock de console existente. Não adiciona logs por registro nem altera opções, SQL, snapshots, P95, ranking, rejeições, paralelismo ou cálculos de progresso. Falhas continuam no tratamento anterior; não se registra tempo de grupo concluído quando `RunAsync` falha.

## Interpretação e impactos
`run_async_seconds` é duração operacional da chamada por grupo, **não** latência física certificada, nem tempo total da campanha; grupos concorrentes podem sobrepor tempos. `tempo_individual_pos_SQL` continua separado e `decorrido_min` continua sendo o indicador primário de progresso. O acréscimo de logging é um pequeno overhead operacional; não há alegação de ganho de desempenho.

## Rastreabilidade e governança
PR #1465 (extração física), PR #1466 (guardas de contrato), `Solution/docs/Baseline_Tempo_Decorrido_V9_100k_20261112.md`, GOV-DOC-01. Esta é instrumentação de observabilidade, não aprovação de reutilização de cache ou de novo protocolo científico. A proposta não cria decisão científica vigente; qualquer mudança de semântica requer aprovação expressa e atualização do índice.
