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

## Continuação — indexação dos resultados físicos (12/11/2026)
Após a PR #1462, a primeira amostra pós-SQL caiu de 107,8 s para 72,5 s, mas essa observação não estabelece causalidade. Uma otimização adicional, isolada na branch `perf/v9-index-physical-results-20261112`, substitui buscas `Single(predicate)` repetidas nas coleções de métricas, snapshots e opções por índices `ToLookup` construídos uma vez por grupo físico. Cada consulta mantém `Single()`, preservando falha para chaves ausentes ou duplicadas. Os replays congelados, o SQL, os dados científicos e as políticas permanecem inalterados. Impacto esperado: menos varreduras CPU por hipótese; ganho de tempo global desconhecido e possivelmente pequeno. Validar build e testes antes de integrar. Esta proposta não aprova reutilização de tabelas SQL entre conexões, nem modifica decisões vigentes ou o índice de decisões.


## Instrumentação opt-in de tempo dos índices e replays (12/11/2026)

**Contexto:** a diferença observada entre as dez hipóteses coincidentes dos runs #43292437608 (#1462) e #43295710416 (#1462 + #1463) requer separar custos por fase, sem concluir causalidade.

**Alteração técnica proposta:** no coordenador `V9NestedCalibrationCoordinator`, quando `V9_REPLAY_TIMING_DIAGNOSTICS=1`, emitir exclusivamente ao console linhas `[V9 PERF DIAG]`, com `phase=index_build` (construção cumulativa dos três `ToLookup`, por grupo, contagens das coleções e milissegundos) e `phase=frozen_replay` (uma chamada `EvaluateFrozenHoldoutAsync`, por hipótese elegível, milissegundos e SHA da hipótese). A medição usa `Stopwatch` ao redor das operações existentes. Flag ausente/desligada mantém os logs anteriores sem novos registros. Não são expostos nomes ou dados pessoais.

**Limitações:** `index_build` inclui a criação dos três índices mas não o tempo das consultas às estruturas indexadas; `frozen_replay` mede duração de chamada, não o P95 científico produzido por ela. Os tempos de console e contagem de entradas têm custo operacional apenas com flag ligada. A instrumentação não altera o algoritmo, SQL, partições, políticas, grupos, critérios científicos nem o número de replays. Os dados observados não constituem benchmark A/B; é necessário comparar execuções pareadas em ambiente controlado e múltiplas repetições para inferir regressão.

**Validação exigida antes de adoção:** compilar, executar testes e conferir que com a flag ausente não há linhas novas; com flag ativa conferir uma medição de índice por grupo e uma de replay por hipótese elegível, sem alterar métricas nem contabilidade das 324 hipóteses. Revisão/CI pendentes na PR de instrumentação. Não reverter #1463 com base nesta evidência isolada.

**Governança:** instrumentação diagnóstica submetida para revisão, não é decisão funcional/científica aprovada pelo PO; preserva GOV-DOC-01 e as decisões vigentes, sem mudança no índice de decisões.

**Registro de validação da PR #1469:** a primeira CI `jornada-pr-required-fast` (run #43307543496) aprovou `security-guards` e `unit-runtime`, mas `deterministic-build` falhou com seis diagnósticos CA1829/CA1826 porque três coleções indexáveis foram contadas com `Count()` no log opt-in. Correção de manutenção aplicada no commit `76c1af153caf5566f35be5e868c1bb875dca4b83`, trocando somente essas chamadas por propriedades `Count`. Nova CI e validação da medição física permanecem pendentes; nenhum resultado científico foi produzido nesta correção.


## Ativação manual das medições na campanha 100k (12/11/2026)

O workflow manual `.github/workflows/v9-v3-four-name-100k.yml` passa a aceitar `replay_timing_diagnostics` (booleano, `false` por padrão). Quando habilitado, exporta `V9_REPLAY_TIMING_DIAGNOSTICS=1` para o calibrador, emitindo `[V9 PERF DIAG]` no console. Desabilitado, exporta `0` e não gera esses novos logs. A campanha mantém as 324 hipóteses declaradas (81 TF4 x 2 misturas x 2 margens), a ordem e os replays, sem mudar resultado científico, recursos, timeout ou dados.

**Segurança e operação:** continuam obrigatórios `workflow_dispatch` pelo proprietário, `refs/heads/master`, `expected_sha` idêntico ao `GITHUB_SHA`, `security_preflight_approved=true`, dados sintéticos e demais gates existentes. A instrumentação não habilita automaticamente a campanha nem o TEST; a escolha de marcar o checkbox no dispatch é operacional, após verificação de segurança e aprovação aplicável.

**Rastreabilidade:** PR #1469 integrou a instrumentação após CI aprovada. Esta alteração oferece o canal para executar a mesma instrumentação na campanha manual. A avaliação pareada com a versão sem #1463 continua experimento futuro; não há inferência causal autorizada nem mudança no índice de decisões vigentes.


## Medição real e conclusão operacional do run #43309643251 (13/11/2026)

- **Execução:** SHA `ccb3ffe052b3923eb093c921ff8c642505c1f01d`, 324/324 hipóteses concluídas (100%), envelope com três finalistas, `worker_exit=0`, seguida de **timeout da etapa física aos 305 min**; resultado do workflow `FAILURE`. Auditoria técnica posterior e VALIDATION independente **não executadas**. Evidência agregada [#14005260596](https://github.com/lucianox777/Jornada/actions/runs/43309643251/artifacts/14005260596).
- **Indexação #1463:** quatro medições `index_build` de 3,874; 0,029; 0,222; 0,269 ms, total 4,394 ms para grupos de 64, 4, 128 e 128 solicitações, respectivamente.
- **Replay:** 324 medições `frozen_replay`, média de 92,286 s por chamada, sem equivalência com duração de parede (concorrência). A etapa concluiu as hipóteses em 303,7 min e encerrou perto do limite de 305 min.
- **Interpretação:** a construção dos índices é desprezível ante os replays e não sustenta atribuir vários segundos extras por hipótese à construção `ToLookup`. A diferença observada entre #1462 e #1463 segue sem causalidade demonstrada; o diagnóstico não substitui A/B.
- **Tratamento operacional:** [registro GOV-V9-324-TIMEOUT-FISICO-315-20261113](Decisao_GOV_V9_324_Timeout_Fisico_315_20261113.md) propõe elevar o limite da etapa física e o rótulo ETA a 315 min sem mudar o job 360 min, os critérios científicos, as 324 hipóteses ou a obrigação de VALIDATION.


## Revisão operacional aprovada pelo PO — log único por hipótese (13/11/2026)

A medição histórica acima descreve o comportamento da PR #1469 e das execuções anteriores, em que `phase=frozen_replay` saía numa linha `[V9 PERF DIAG]` além da linha `[V9 PROGRESS]`. Por decisão expressa do PO em 13/11/2026, a versão seguinte deverá **fundir** os campos de replay com o progresso num só `Console.WriteLine` por hipótese quando ambas as opções estiverem ativas, sem alterar o cálculo científico ou a contabilidade. Quando o progresso estiver desligado e apenas a flag PERF estiver ligada, a linha de diagnóstico original permanece disponível; `phase=index_build` continua por grupo. O registro canônico da decisão é [GOV-V9-LOG-UNICO-PROGRESSO-REPLAY-20261113](Decisao_GOV_V9_Log_Unico_Progresso_Replay_20261113.md).

**Não inferir causalidade:** reduzir `lock (Console.Out)` e escritas é otimização de instrumentação com benefício potencial, **não demonstra** que o console causou o timeout do run #43309643251. Validar o custo físico por comparação A/B pareada; a execução #43499315560 foi iniciada com versão anterior e não muda retroativamente. O ETA linear e o limite de 315 minutos permanecem inalterados.
