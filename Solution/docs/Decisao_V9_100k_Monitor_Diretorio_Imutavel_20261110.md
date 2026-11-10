# GOV-V9-100K-MONITOR-OUTPUT-20261110 — correção da instrumentação e propriedade do diretório de resultados

**Autoridade:** implementação corretiva de contrato já vigente, sem nova mudança científica; Product Owner solicitou continuidade e investigação em 10/11/2026. **Política:** GOV-DOC-01. **Issue:** [#1349](https://github.com/lucianox777/Jornada/issues/1349). **Execução causadora:** [#42859834044](https://github.com/lucianox777/Jornada/actions/runs/42859834044) no SHA `dc016ee0f650c5a08ef63d14e0a1fbe84bb806b7`.

## Fatos apurados e causa

O artefato agregado `#13784588732` atestou corpus sintético 100.000/seed42, vinculação 24/16/8, `MaximumParallelRuleSets=0`, SQL nativo (Python SQL budget desabilitado), admissão de RAM/disco PASS e a interrupção com `V9_CALIBRATOR_SAFE_STAGE:PLAN_VALIDATED_AND_OUTPUT_ADMISSION`, `V9_CALIBRATOR_FAILED:UNEXPECTED`, `workerExitCode=1`.

**Causa determinística no código:** `v9-100k-physical-memory-evidence.py` executava `args.output.parent.mkdir(parents=True, exist_ok=True)` **antes** de `subprocess.Popen(command)`. O parent era `.../results`, mesmo diretório que `V9NestedCalibrationCli.RunAsync` recusa quando `Directory.Exists(output)`, em defesa de resultados novos e imutáveis. O monitor fazia o worker falhar antes de qualquer fitting. Esse encadeamento é comprovado por inspeção estática dos dois caminhos e reproduzido por teste real de subprocesso; não depende de segredo ou de diagnóstico de exceção livre.

## Correção limitada

Preservar a propriedade exclusiva da criação de `results/` no C#; o monitor apenas cria o parent de seu **próprio** JSON depois de `child.wait()`, garantindo que o worker possa verificar a ausência de diretório. Se o worker falhar antes de criá-lo, o monitor ainda cria a pasta depois da saída para preservar sua evidência. O arquivo JSON usa `open("x")` e não substitui evidência existente. Testes adicionais simulam worker que exige diretório inexistente e worker que encerra precocemente; verificam códigos de retorno e persistência do JSON.

## Leitura correta da evidência física

- Monitor: `samples=1`, `peakSqlContainerMemoryKiB=4742709`, `sqlCgroupV2MemoryPeakKiB=4774408`, `peakWorkerProcessTreeRssKiB=null`, `minHostMemAvailableKiB=9751928`.
- Docker `OOMKilled=false` nas amostras; runner `oom=0` e `oom_kill=0`; cgroup SQL `oom=0`/`oom_kill=0`. Esses números descrevem **apenas a execução abortada precocemente** e não certificam ausência de OOM durante 24 fits, ainda não executados.
- `sqlMemoryLimitMb=null` e `sqlContainerMemoryMb=null` nos recibos, coerentes com `disable_python_sql_budget=true`; campos de configuração observados `null` significam desconhecido ou padrão nativo, nunca memória infinita.
- Nenhum dos 24 fits foi realizado; `physicalExecution=NOT_STARTED`; TEST/Gold não utilizados, sem modelo ativado.

## Precedência, impactos, segurança e critério de aceitação

Complementa e corrige a instrumentação somente-leitura das [decisões de memória/cgroup](Decisao_V9_100k_Memoria_Cgroup_SQL_20261110.md), preservando a propriedade imutável de resultados e o monitoramento. **Não** altera ciência, 24/16/8, TF, M/U, FDR, planos congelados, governor AUTO, limites SQL/Docker, credenciais, CI ou gatilhos manuais. Não pressupõe que o problema de `PHYSICAL_SQL_FRAME` mais antigo foi resolvido — essa fase só será alcançada em novo run bem-sucedido.

Critério para integrar: checks rápidos e testes do HEAD exato; posteriormente, todos os gates aplicáveis na master. **Não lançar 24×100k como efeito do merge**: é `workflow_dispatch` owner-only, SHA-bound e exige preflight real. [#1253](https://github.com/lucianox777/Jornada/issues/1253) permanece aberta por logs históricos.
