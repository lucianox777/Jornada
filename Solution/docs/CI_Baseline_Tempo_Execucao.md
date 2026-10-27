> **Estado de 26/10/2026 — execução por impacto do V9 30k:** [CI-V9-PROBE-01](Decisao_CI_Probe_V9_Por_Impacto_20261024.md) foi implementada e integrada à `master` pela PR #1188. O classificador conservador preserva o probe completo quando houver alteração do Calibrador/FS/TF/dependências, SQL/DT-10, CI ou caminho desconhecido; dispensa a repetição apenas em mudanças Gold comprovadamente isoladas ou documentação permitida. Permanecem obrigatórios CI geral, preflight DT-10 e status agregador. **Economia ainda não quantificada em runs equivalentes**; não confundir não execução deliberada do probe com aprovação física. Medições e novos testes de regressão: issue #1200.

# CI — Baseline mensurável de duração e builds observados

**Escopo (12/10/2026):** segunda etapa incremental de refatoração da CI, após #928 (preservar execução em andamento), #929 (diagnóstico somente leitura) e #930 (watchdog horário com token efêmero).

O workflow `jornada-ci-diagnostics` já acompanha `workflow_run` concluído. A extensão acrescenta um perfil observacional, sem execução de código da PR examinada, e sem qualquer poder de modificar a CI.

## Evidências de cada execução

O passo busca o JSON completo do run e dos jobs via API de leitura do GitHub. `ci-run-profile.py` exige `status=completed`, HEAD SHA de 40 caracteres e contagem de jobs consistente (`total_count`). Se a resposta vier parcial, a coleta falha, sem inventar valores.

A saída registra:

- ID e SHA exato;
- duração entre primeiro início e último fim de jobs não ignorados (tempo decorrido observado);
- soma das durações dos jobs não ignorados (runner-minutes, com paralelismo);
- dez jobs mais demorados com estado;
- compilação .NET **explicitamente nomeada** nas etapas e seus tempos. O contador é um limite inferior, pois scripts invocados indiretamente podem compilar sem essa informação no nome do passo.

Não denominar o job de maior duração de caminho crítico do grafo: as dependências e filas precisam de análise adicional. Não atribuir economia em minutos antes de comparar execuções equivalentes.

## Diagnóstico incremental — PR #1199, 26/10/2026 (19h29 BRT)

**Evidência de execução, não benchmark concluído.** No workflow `v9-physical-seed42-probe`, run [#40396877513](https://github.com/lucianox777/Jornada/actions/runs/40396877513), o job `V9 full physical 30k SQL TRAIN (when impacted)` ainda estava **in_progress**. A API de jobs/steps já indicava **concluídos com sucesso**: preparação do contêiner SQL, checkout, setup .NET, builds, criação de `JornadaHarness`, geração dos corpora 1k/1k/30k e busca/validação SQL da fixture. O passo **`Calibrate 30k using ONLY TRAIN-adjust and TRAIN-selection`** permanecia **in_progress**; exportação dos gráficos e artefatos ainda não começara. A CI principal `jornada-ci` da mesma PR estava concluída com sucesso, mas **não substitui** esse probe.

**Consequência operacional:** o gargalo **observado neste instante** está no passo de calibração física 30k, não no restore/build, bootstrap SQL ou geração dos dados já finalizados. Não afirmar que o passo está travado nem estimar economia ou duração final sem timestamps de conclusão. O limite do job é `timeout-minutes: 360`.

O plano gerado por `Solution/scripts/v9-nested-calibration-study.py` para o corpus real de desenvolvimento declara `MaximumBatches=32`, `BootstrapRepetitions=2000`, `MaximumSearchConfigurations=1000000`, `MaximumComparedConfigurations=1000000`, `MaximumParallelRuleSets=2` e `Execution.EndToEndRepetitions=20`. Esses são **tetos/configurações**, não contagens efetivamente realizadas. Antes de propor redução de custo, medir no artefato final a quantidade efetiva de candidatos, lotes, bootstrap, repetições e tempo por fase; não reduzir esses limites arbitrariamente, pois isso poderia alterar seleção, evidência estatística ou certificação.

**Próxima medição necessária:** quando o run concluir, coletar durações de cada step e o `bootstrap-search` auditável, comparar com outro run de HEAD/corpus/seed equivalentes e só então priorizar otimização interna. Não iniciar nova execução física apenas para obter esta medição.

## Invariantes

- O workflow diagnóstico mantém `permissions: actions: read; contents: read`. O checkout da rotina de análise aponta explicitamente a `master` confiável, não o código do evento `workflow_run` ou de PR.
- O job `impact` da CI obrigatória executa os testes de perfil. Nenhum gate é removido, e não são alterados o contrato lockfile, SQL, integração, E2E, CodeQL ou segurança.
- Não inserir em artefatos dados de produção, tokens ou logs brutos; somente timestamps e nomes dos passos com escape HTML/Markdown.
- Nunca usar `JornadaLocal`, cluster DEV do usuário, HML/PROD, IBGE original, volumes ou contêineres existentes. Apenas APIs do GitHub e runners efêmeros.

## Continuação da refatoração integral

Com duas ou mais execuções comparáveis, priorizar uma PR separada para eliminar duplicidade de build com artefatos atestados por **HEAD SHA exato**, versão .NET, os locks versionados e proveniência; validar equivalência de binários antes de trocar cada build. O `deterministic-build` duplo continua como gate até substituição comprovadamente equivalente.

Agente autônomo de correção de código por PR é etapa distinta: precisa credenciais temporárias com escopo próprio, isolamento de PR não confiável e testes integrais. O watchdog #930 só reexecuta falhas de infraestrutura confirmadas, não altera código.
