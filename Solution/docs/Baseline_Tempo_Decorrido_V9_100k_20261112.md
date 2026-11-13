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

## Retrospectiva ampliada — localizar o primeiro TRAIN 324 PASS sem log por hipótese (13/11/2026)

**Correção importante de recorte histórico:** a comparação de `303,1 min` versus `303,7 min` **não incluía o primeiro ensaio 324 comprovado**, anterior à instrumentação por hipótese. O run [#43105369099](https://github.com/lucianox777/Jornada/actions/runs/43105369099), SHA `7bf750f510fc1ef698e4e707748b776ab78e7ecf`, job `130807192482`, terminou `SUCCESS`: auditor `V9_V3_TF4_100K_TRAIN_PASS`, `measured=324`, `worker_exit=0`. Emitiu **0** linhas `[V9 PROGRESS]` e **0** linhas `[V9 PERF DIAG]`. Já utilizava o `Stopwatch` global da CLI para o recibo do lote; ainda não havia `individualClock` nem `physicalProgressClock` no coordenador.

A mesma métrica de cronômetro `V9_CALIBRATION_MEMORY_PER_BATCH_V1.elapsedMilliseconds` foi extraída diretamente de três logs imutáveis:

| GitHub run / SHA | Instrumentação por hipótese | Recibo elapsedMilliseconds (ms) | Minutos | Desfecho |
| --- | --- | ---: | ---: | --- |
| [#43105369099](https://github.com/lucianox777/Jornada/actions/runs/43105369099) / `7bf750f5` | **Sem progresso e sem PERF DIAG**, nenhum callback por hipótese | **14.039.465** | **233,991** | 324/324; TRAIN + auditor PASS; job SUCCESS |
| [#43159426682](https://github.com/lucianox777/Jornada/actions/runs/43159426682) / `32ac1845` | **Uma linha de progresso por hipótese**, `individualClock`, `physicalProgressClock` e callback com `File.AppendAllText` por hipótese | **18.186.467** | **303,108** | 324/324; TRAIN + auditor PASS; falha posterior da VALIDATION >2 GiB |
| [#43309643251](https://github.com/lucianox777/Jornada/actions/runs/43309643251) / `ccb3ffe0` | **Progresso + PERF DIAG por hipótese**, 3 cronômetros adicionais de grupo/índice/replay, **sem** o append individual à saída | **18.224.732** | **303,746** | 324/324; worker exit 0; timeout no wrapper de 305 min |

**Diferenças observadas:** 303,108 − 233,991 = **69,117 minutos** (+29,5% sobre o menor tempo); 303,746 − 303,108 = **0,638 minuto** (+0,21%). A estimativa de 69 minutos refere-se a **tempo decorrido**, não a uma soma de custos de logs ou cronômetros. Todos os runs mediram 324 configurações TRAIN sintéticas de 100k com `AUTO_0`, mas os GitHub runners, regiões Azure, carregamento/contensão e commits diferem, portanto **não constituem A/B pareado**, nem demonstram que `Stopwatch`, locks ou flush sejam causa isolada.

### Auditoria de alterações entre a campanha rápida e a primeira com progresso

`git compare 7bf750f5...32ac1845` revela oito commits. Na parte executada durante TRAIN, o coordenador recebeu `physicalProgressClock`, `individualClock`, contador atômico, atualização de ETA e um `lock (Console.Out)` com `Console.WriteLine`/`Flush` a cada conclusão, além de callback síncrono opcional. A CLI recebeu, **na mesma janela histórica**, `hypothesis-progress.jsonl` gravado com `File.AppendAllText` protegido por outro bloqueio e linha de início. Houve também alterações de workflow, em seleção/documentação e no trecho externo da VALIDATION; o diff não é um experimento que isole cada mecanismo.

O append por hipótese foi posteriormente retirado por decisão GOV-V9-LOG-HIPOTESE-SOMENTE-CONSOLE; a execução #43309643251 continuou em ~303,7 min, mas acumulou outros commits e flags, e não prova ausência de efeito dos arquivos. A PR [#1493](https://github.com/lucianox777/Jornada/pull/1493), já integrada à master, unificou as duas emissões por hipótese quando ambas flags estão ligadas; **ainda não existe medição física 324 com essa versão**.

**Hipótese operacional a investigar, sem conclusão causal:** instrumentação adicionada ao *caminho crítico* assíncrono dos grupos concorrentes (incluindo escrita síncrona, contenção, flush, serialização e I/O), não necessariamente a consulta monotônica de `Stopwatch`. A diferença inicial (~69 min) é grande e justifica comparar uma execução com toda instrumentação por hipótese desligada a outra com progresso unificado, utilizando o **mesmo plano, corpus, perfil, SHA científico e características de runner**, idealmente com réplicas e medição agregada da espera por console/I/O. Não modificar protocolo científico nem disparar 100k sem aprovação operacional manual. Nenhum ganho de performance está comprovado.

**Estado documental:** apuração factual, não aprova mudança de código, desligamento de logs, revisão de timeout ou novo experimento científico. Decisões futuras exigem GOV-DOC-01 e atualização de `Indice_Decisoes_Vigentes.md`. Esta seção restaura a **baseline sem logs** omitida das análises anteriores.
