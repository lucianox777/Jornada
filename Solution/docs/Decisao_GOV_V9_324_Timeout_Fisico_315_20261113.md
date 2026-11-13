# GOV-V9-324-TIMEOUT-FISICO-315-20261113 — folga operacional após 324 avaliações

**Origem:** continuidade da correção operacional do calibrador V9 TF4/324 solicitada pelo PO em 13/11/2026. **Estado:** correção proposta em PR, sujeita à CI e integração; **não** constitui alteração de hipótese científica nem certificação de campanha. **Política:** GOV-DOC-01.

## Fato e contexto verificáveis

O workflow manual [#43309643251](https://github.com/lucianox777/Jornada/actions/runs/43309643251), SHA exato `ccb3ffe052b3923eb093c921ff8c642505c1f01d`, terminou com `conclusion=failure`, mas o log comprovou as **324/324 hipóteses** concluídas às **02:31:37 UTC de 13/11/2026** (303,7 min de etapa), todas `AVALIADA`, **3 finalistas** no envelope emitido às 02:32:53 UTC, e o wrapper de memória registrou `worker_exit=0` às 02:33:01 UTC. O GitHub Actions registrou **timeout da etapa física após 305 min** às 02:33:01 UTC, cerca de 100 ms após o último recibo do wrapper. O arquivo de artefato agregado foi enviado [#14005260596](https://github.com/lucianox777/Jornada/actions/runs/43309643251/artifacts/14005260596), mas **auditoria completa e VALIDATION independente foram puladas**, pois a etapa física foi marcada como falha. Logo não se pode classificar a campanha como SUCCESS ou certificada; nada foi ativado e TEST não foi utilizado.

## Evidência temporal da instrumentação

Com `V9_REPLAY_TIMING_DIAGNOSTICS=1`, a execução registrou quatro grupos com custos de `ToLookup` (ms): **3,874; 0,029; 0,222; 0,269**; soma **4,394 ms** para os 324 pedidos. Foram emitidos **324 tempos de replay congelado**, média **92,286 s** por chamada (mínimo 70,892 s, mediana aproximadamente 94,185 s, P95 amostral aproximadamente 106,342 s, máximo 110,869 s). A soma de tempos de chamada (~8,31 h) não é tempo de parede e inclui execuções com concorrência. O trabalho físico concluiu todos os pedidos próximo do limite de 305 min.

**Interpretação e limites:** o tempo de construção dos três índices da PR #1463 é ínfimo perante os replays, e **não explica por si só** diferenças observacionais de ~5,5 s por hipótese entre runs #43292437608 e #43295710416. O benchmark **A/B pareado e repetido ainda não ocorreu**; as consultas indexadas e a variabilidade do ambiente não foram isoladas. Não reverter PR #1463 por esta evidência nem prometer ganho causal.

## Correção operacional e justificativa

Aumentar **somente a duração máxima da etapa física** do workflow manual `.github/workflows/v9-v3-four-name-100k.yml` de **305 para 315 minutos**, para absorver encerramento do wrapper, gravação do envelope, coleta de recursos e finalização de logs após a hipótese #324. Sincronizar no código `V9NestedCalibrationCoordinator.cs` o indicador de ETA (`TimeSpan.FromMinutes(315)` e coluna `margem_etapa_315min`) para não exibir margem fictícia. Preservar **timeout total do job de 360 min** e a **etapa de validação independente com limite de 40 min**. O acréscimo é de até 10 minutos de capacidade de etapa, não altera algoritmo, opções TF, replays, pontuações, parâmetros, custo SQL por avaliação, nem o critério de seleção.

**Risco residual:** o job total de 360 min ainda pode encerrar uma campanha lenta, especialmente com a validação independente. Esta correção responde ao incidente observado (worker concluiu junto ao limite físico) e não garante que futuras execuções caberão no orçamento do job. Se a soma de fases exceder o teto, tratar arquitetura do pipeline em decisão separada, sem omitir auditoria/VALIDATION ou alterar ciência para forçar sucesso. O artefato anterior não é equivalente a campanha validada; a nova execução manual exige ambiente seguro efêmero novamente.

## Precedência, segurança e gates

Complementa e **substitui apenas o limite operacional antigo de 305 min e a margem de progresso de 305 min** nas normas de execução TF4/324, mantendo em vigor [GOV-V9-V3-TF4-324-20261111](Decisao_GOV_V9_V3_TF4_324_100k_20261111.md), [GOV-V9-PROGRESS-324-20261111](Decisao_GOV_V9_Progresso_Fisico_Hipoteses_20261111.md), demais políticas e GOV-DOC-01. Não concede dispensa de `workflow_dispatch` owner-only, SHA idêntico na `master`, `security_preflight_approved=true`, dados sintéticos, TEST lacrado, auditoria fail-closed, VALIDATION obrigatória ou travas de memória/disco.

**Verificações exigidas:** CI `jornada-pr-required-fast` (`unit-runtime`, `security-guards`, `deterministic-build`) no HEAD; teste estático que vincula 315 minutos ao indicador e preserva 360/40; merge por squash apenas após checks; qualquer reexecução 100k é manual pelo proprietário, com preflight real e novo SHA imutável. Resultado histórico #43309643251 permanece FAILURE.

**Rastreabilidade:** [run #43309643251](https://github.com/lucianox777/Jornada/actions/runs/43309643251); [artefato agregado #14005260596](https://github.com/lucianox777/Jornada/actions/runs/43309643251/artifacts/14005260596); [relatório de performance](Analise_Performance_V9_Replay_20261112.md); [índice](Indice_Decisoes_Vigentes.md); PR da correção operacional criada a partir da evidência deste incidente.
