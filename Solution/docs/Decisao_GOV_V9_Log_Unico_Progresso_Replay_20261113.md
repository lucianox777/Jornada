# GOV-V9-LOG-UNICO-PROGRESSO-REPLAY-20261113 — uma escrita por hipótese

**Autoridade:** Product Owner, 13/11/2026 (America/Sao_Paulo). **Estado:** DECIDIDO pelo PO; **IMPLEMENTADO e INTEGRADO à master pela PR #1493 (squash SHA `4b95d73b8c691894a41468e97f9d3578a39b0e06`) com CI rápida aprovada**; **ganho físico de desempenho ainda não medido**. **Política:** GOV-DOC-01.

## Contexto, evidência e problema

O PO observou que a campanha V9 TF4 de 324 hipóteses passou a emitir duas linhas por replay quando `V9_PHYSICAL_PROGRESS_LOG=1` e `V9_REPLAY_TIMING_DIAGNOSTICS=1`, e suspeitou de custo por concorrência em `lock (Console.Out)`. O coordenador `V9NestedCalibrationCoordinator.cs` adquiria o bloqueio uma vez para `[V9 PERF DIAG] phase=frozen_replay` (após `EvaluateFrozenHoldoutAsync`) e novamente para `[V9 PROGRESS]`, seguido de `Flush()`. A observação é hipótese operacional, **não prova causal de regressão**.

A execução manual histórica [#43309643251](https://github.com/lucianox777/Jornada/actions/runs/43309643251) emitiu 324 pares de linhas, concluiu 324/324 avaliações físicas em 303,7 min e o worker encerrou com `exit=0`, mas a etapa excedeu seu limite antigo de 305 min e não alcançou a auditoria/VALIDATION. Os horários externos do GitHub não medem tempo bloqueado pelo `lock`; diferenças entre execuções sofrem variabilidade do ambiente, SQL e concorrência. A campanha [#43499315560](https://github.com/lucianox777/Jornada/actions/runs/43499315560), iniciada antes desta mudança, **não recebe a alteração retroativamente**.

## Decisão aprovada e escopo da implementação

**Consolidar, quando ambos os diagnósticos estiverem ativados, o tempo do replay congelado e o progresso físico em uma única mensagem `[V9 PROGRESS]` por hipótese finalizada**, em vez de `[V9 PERF DIAG] phase=frozen_replay` mais `[V9 PROGRESS]` separadas. Manter o cronômetro `Stopwatch` da chamada física **única**, sem repetir replay e sem remover qualquer hipótese. A mensagem consolidada inclui `phase=frozen_replay`, `request_sha` integral e `replay_elapsed_ms` com três casas, além do contador, resultado, duração individual pós-SQL, decorrido, ETA preliminar e margem da etapa física de 315 minutos.

Preservar as demais combinações das opções de diagnóstico: quando apenas progresso estiver ativo, manter formato e frequência de `[V9 PROGRESS]` anteriores; quando só timing estiver ativo, preservar a mensagem `[V9 PERF DIAG] phase=frozen_replay` sem progresso; com ambos desligados não acrescentar logs. A medição `[V9 PERF DIAG] phase=index_build` e o aviso `[V9 SQL GROUP]` continuam por grupo físico, porque não são duplicações por hipótese.

O callback de conclusão mantém o recibo de auditoria antes da emissão operacional, recebe duração do replay opcional (`null` se não houve replay, inclusive rejeição controlada); permanece um `Interlocked.Increment` por hipótese concluída. No modo consolidado, cada hipótese tem **no máximo uma aquisição explícita de `lock (Console.Out)` e um `Console.WriteLine`**, sem alterar `Console.Out.Flush()` existente. Não antecipar `reportPhysicalCompletion` antes da verificação de divergência física.

## Impactos, precedência e riscos

**Ganho esperado:** reduzir de duas para uma as escritas por hipótese com as duas flags ativas; em 324 replays, aproximadamente 648 linhas viram 324 linhas (sem contar mensagens por grupo, falhas e preparação). Isso reduz oportunidades de contenção, mas **não demonstra** que o bloqueio era gargalo, nem garante redução estatisticamente mensurável do tempo total. Medição de espera no `lock` e A/B físico pareado ainda são necessários para atribuir causalidade.

**Sem alteração científica:** manter exatamente as 324 hipóteses TF4, 81 opções, duas misturas M/U, duas margens, SQL e replay físico, métricas, FDR/CP, P95, congelamento, ordenação, paralelismo AUTO_0, regras de segurança, timeout físico 315 min e job TRAIN 360 min, isolamento TRAIN/VALIDATION/TEST, hashes e publicação. ETA permanece estimativa linear rudimentar, não foi corrigido nesta mudança.

**Precedência:** complementa e ajusta **apenas o formato operacional dos logs quando ambas as flags estão ativas** de [GOV-V9-PROGRESS-324-20261111](Decisao_GOV_V9_Progresso_Fisico_Hipoteses_20261111.md), sem revogar a regra de progresso por hipótese e somente no console, e do diagnóstico descrito em [Análise de performance V9 replay](Analise_Performance_V9_Replay_20261112.md). Preserva [GOV-V9-324-TIMEOUT-FISICO-315-20261113](Decisao_GOV_V9_324_Timeout_Fisico_315_20261113.md), todas as decisões científicas e a política GOV-DOC-01.

## Implementação, gates e rastreabilidade

- Código: `Solution/src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCoordinator.cs`, inclusão da medição opcional no callback de conclusão e remoção da escrita duplicada dentro do replay quando progresso está ligado.
- Testes de contrato: `Solution/tests/scripts/test_v9_calibrator_safe_failure_stage.py`; garantir uma única chamada física `EvaluateFrozenHoldoutAsync`, uma escrita por hipótese com ambas flags, preservação do fallback quando só timing está ativo e contabilidade dos rejeitados.
- Workflow: `.github/workflows/v9-v3-four-name-100k.yml`, somente esclarecimento da descrição da flag, sem alterar entradas, valores, política de dispatch ou agendamento.
- Gates: `unit-runtime`, `security-guards`, `deterministic-build` e revisão dos diffs no HEAD da PR; integração somente por **squash merge** após checks requeridos. Qualquer campanha física de 100 mil segue **manual**, com SHA/prefight segurança vigentes; não iniciar automaticamente, não cancelar o run existente.
- Aceite de desempenho: A/B pareado sob mesmo corpus/plano/runner para avaliar tempo total; sem isso a entrega pode ser classificada como **IMPLEMENTADO** após merge, jamais **GANHO DE PERFORMANCE MEDIDO**.

**Rastreabilidade:** branch `fix/v9-single-console-message-per-hypothesis-20261113`; execução histórica #43309643251; execução atual #43499315560; [índice de decisões vigentes](Indice_Decisoes_Vigentes.md). PR e status de CI devem ser anotados após abertura e execução; não considerar esta decisão integrada antes do merge.

## Recibo de integração (13/11/2026)

A [PR #1493](https://github.com/lucianox777/Jornada/pull/1493) foi integrada à `master` por **squash merge** no commit `4b95d73b8c691894a41468e97f9d3578a39b0e06`. O [workflow obrigatório #43511538731](https://github.com/lucianox777/Jornada/actions/runs/43511538731) aprovou **unit-runtime**, **security-guards** e **deterministic-build** no HEAD `743971f1408e2c332a6e477c32fae0e54c6a186d`; o [preflight de plano #43511538716](https://github.com/lucianox777/Jornada/actions/runs/43511538716) também aprovou o contrato offline. O merge está confirmado no histórico da master.

**Limite da prova:** validação por build, testes de contrato e gate de segurança; ainda não há execução física 100k com a nova instrumentação ou A/B de tempo total. A [campanha #43499315560](https://github.com/lucianox777/Jornada/actions/runs/43499315560) foi disparada no SHA anterior e permanece inalterada. Esta decisão está **IMPLEMENTADA/INTEGRADA**, não é `GANHO_MEDIDO`, nem autorização de TEST ou mudança científica. A janela de até 315 min continua válida para o job TRAIN. GOV-DOC-01.
