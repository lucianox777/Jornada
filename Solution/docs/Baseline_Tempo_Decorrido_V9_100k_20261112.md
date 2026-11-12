# V9 100k — baseline comparável de tempo decorrido (12/11/2026)

**Status:** registro observacional; não é decisão de mudança do protocolo nem certificação científica. Dados transcritos de capturas fornecidas pelo Product Owner. Execuções diferentes não são experimentos controlados; não atribuir causalidade sem repetição comparável.

| Execução | Código | Hipóteses concluídas | Decorrido da etapa (min) | Decorrido/hipótese (s) | Observação |
|---|---|---:|---:|---:|---|
| [43283688181](https://github.com/lucianox777/Jornada/actions/runs/43283688181) | anterior à #1462 | 10 | 22,3 | 133,8 | print de progresso |
| [43292437608](https://github.com/lucianox777/Jornada/actions/runs/43292437608) | #1462 | 10 | 14,7 | 88,2 | print de progresso |
| [43295710416](https://github.com/lucianox777/Jornada/actions/runs/43295710416) | #1462 + #1463 | 10 | 16,1 | 96,6 | print de progresso; execução posteriormente cancelada |
| [43295710416](https://github.com/lucianox777/Jornada/actions/runs/43295710416) | #1462 + #1463 | 13 | 20,0 | 92,3 | print de progresso; execução posteriormente cancelada |

## Interpretação rigorosa

- A métrica principal é **tempo decorrido da etapa até N hipóteses concluídas**, para o mesmo N. Inclui custo acumulado de preparação de grupos e outros overheads; `decorrido/N` é média amortizada, **não** duração individual do replay.
- Em N=10, a #1462 apresentou 34,1% menos tempo decorrido que a execução anterior (7,6/22,3). A versão #1462+#1463 apresentou 27,8% menos (6,2/22,3) e 9,5% mais tempo decorrido que a #1462 (1,4/14,7). Nenhuma diferença comprova regressão causal.
- `tempo_individual_pos_SQL` exclui o preparo do grupo SQL e não deve substituir o tempo decorrido como indicador de duração da campanha.
- O custo por hipótese pode variar entre grupos; multiplicar a média inicial por 324 é **projeção exploratória**, não prazo garantido.
- Cancelamento de #43295710416 não permite inferir conclusão de 324 hipóteses ou motivo do cancelamento sem logs apropriados.

## Plano de benchmark da próxima otimização

1. Mesmo seed, corpus 100k, SHA do plano científico, máquina/runner comparável, orçamento e ordem de hipóteses.
2. Capturar timestamps monotônicos de início e fim de cada grupo SQL e de cada hipótese, além de `tempo_individual_pos_SQL`; não incluir logging por registro em hot path.
3. Comparar checkpoints fixos (10, 25, 50, 100, 200, 324) quando disponíveis em ambas as execuções; declarar ausentes os checkpoints não observados.
4. Separar tempo de preparação SQL por grupo, preparação de pares do holdout, replay FS/Resolve e P95 físico completo, sem alterar a definição de `V9_FROZEN_HOLDOUT_FULL_SQL_FS_BLIND_SOURCE_RESOLVE_V1`.
5. Preservar auditoria de snapshots, digest e métricas por hipótese; medir memória máxima e cancelamentos; não declarar ganho científico sem paridade.

## Rastreabilidade e governança

PRs #1462, #1463, #1465 e #1466; execução #43295710416; `Solution/docs/Plano_Otimizacao_V9_Holdout_SQL_20261112.md`. Este documento consolida observações e critérios de medição, não aprova alteração funcional, estatística ou de protocolo. Decisões futuras aprovadas pelo PO exigem registro GOV-DOC-01 e atualização do índice de decisões vigentes.

## Achado adicional — custo por grupo físico versus replay individual

Inspeção de `V9NestedCalibrationCoordinator.cs` em `master` (12/11/2026): o coordenador particiona pedidos com `V9MarginFrameWorkPartition.Group`, ordena os frames com `V9PhysicalFrameExecutionOrder.BroadestLikelyFirst` e processa grupos via `V9ParallelWork.MapCalibrationAsync`. Cada grupo executa `V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync` antes de iterar sobre os pedidos e chamar `EvaluateFrozenHoldoutAsync` apenas quando não há razões anteriores de rejeição. Portanto, o tempo decorrido para N hipóteses **não é linear em N**: inclui preparo de grupos com custo variável, execução concorrente e replays condicionais. Os prints isolados não permitem separar essas parcelas.

**Instrumentação proposta (ainda não implementada):** medir por grupo o intervalo de `RunAsync`, contagem de pedidos e de replays efetivamente executados, e por pedido os tempos pós-SQL já disponíveis. Reportar tempos por fase sem modificar relógios existentes, P95, ordenação, paralelismo, hashes ou critérios científicos. A comparação 10/10 permanece o melhor checkpoint observado, mas requer repetição controlada para afirmar regressão ou ganho causal.
