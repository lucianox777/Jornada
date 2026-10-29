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

## Estado de integração e medição — 27/10/2026 (adendo, sem alterar baseline histórico)

A [PR #1201](https://github.com/lucianox777/Jornada/pull/1201) foi **integrada** à `master` pelo merge `c0f0851a06fd8286d3d5dca8253d8ab7e0dc9449`, HEAD testado `ac60769fc5bbc140fc8ff279f86e699683ad7da4`. Nesse HEAD, [CI 40433795628](https://github.com/lucianox777/Jornada/actions/runs/40433795628), [fast PR 40433795646](https://github.com/lucianox777/Jornada/actions/runs/40433795646) e [probe 30k 40433795608](https://github.com/lucianox777/Jornada/actions/runs/40433795608) terminaram **SUCCESS**. Isso comprova conclusão desses checks, **não** redução de duração, execução de um teste real de cancelamento seletivo nem aprovação humana da V9. O workflow vigente possui concorrência condicional por PR, guarda de HEAD obsoleto e cache NuGet por lockfile; detalhes e histórico em [CI-V9-PROBE-01](Decisao_CI_Probe_V9_Por_Impacto_20261024.md).

Em 27/10, a [PR #1203](https://github.com/lucianox777/Jornada/pull/1203), HEAD `9719522703abb71cb71bc38fa1f4ad51c97a4fbd`, alterava apenas `.github/dependabot.yml`. A [CI 40501009672](https://github.com/lucianox777/Jornada/actions/runs/40501009672) e o [probe 30k 40501009565](https://github.com/lucianox777/Jornada/actions/runs/40501009565) estavam **COMPLETED/SUCCESS**; a PR permanecia **aberta** na consulta. O probe foi executado porque esse caminho não pertence à allowlist conservadora atual: sucesso do run **não é medição de economia**, nem autorização para alargar a allowlist sem testes de regressão e revisão de risco. A [PR #1199](https://github.com/lucianox777/Jornada/pull/1199), HEAD `c4c748b6ea54717f6ec0dd74e2057b54d32ca281`, tinha o [probe 40509840420](https://github.com/lucianox777/Jornada/actions/runs/40509840420) ainda **IN_PROGRESS** nesta auditoria; não declarar conclusão nem usar probe de SHA anterior para certificá-la.

**Próxima evidência necessária:** extrair durações de fila, etapas e runner-minutes de pelo menos três execuções comparáveis, com SHA, perfil, corpus/seed e critérios de seleção explicitados. Separar custo de atualização de dependências, custo de calibração TRAIN e custo da CI principal; não atribuir economia à classificação de impacto sem comparação reproduzível. Esta seção documenta somente estados observados e mantém as decisões originais intactas.


## Adendo de rastreabilidade — PR #1205, 28/10/2026 (sem certificação de otimização)

**Estado PROPOSTO/IMPLEMENTADO no HEAD `347efacb06e821c249b0b0f0d39435220ab75226`:** a PR #1205 altera `Solution/scripts/v9-physical-impact-classifier.py` e `Solution/tests/scripts/test_v9_physical_impact_classifier.py` para tratar a mudança isolada de `.github/dependabot.yml` sem exigir o probe físico 30k. A mudança deve preservar o comportamento conservador para alterações desconhecidas, SQL/Gold, workflows e conjuntos mistos. Esta seção registra o escopo do changeset, **não** comprova cobertura completa desses casos por execução.

**TESTADO (evidência observada no HEAD acima):** `jornada-ci` run [40563360659](https://github.com/lucianox777/Jornada/actions/runs/40563360659) e `v9-experimental-fast-pr` run [40563360598](https://github.com/lucianox777/Jornada/actions/runs/40563360598) concluíram SUCCESS. O `v9-physical-seed42-probe` run [40563360620](https://github.com/lucianox777/Jornada/actions/runs/40563360620) terminou **CANCELLED**: não contar como prova de execução física nem como gate agregador aprovado. Confirmar os checks obrigatórios e a classificação de impacto no HEAD final antes de considerar merge.

**NÃO APROVADO / NÃO ATIVADO:** a PR #1205 permanece aberta; não há benchmark equivalente de wall-clock/runner-minutes que demonstre economia. A issue [#1200](https://github.com/lucianox777/Jornada/issues/1200) acompanha medições e critérios. A dispensa seletiva de probe não altera a obrigação de CI geral, preflight DT-10, agregador fail-closed nem aprovação humana posterior da política V9.

**Reconciliação pendente:** registrar a decisão e casos negativos na [ADR CI-V9-PROBE-01](Decisao_CI_Probe_V9_Por_Impacto_20261024.md) e na matriz de gates aplicável, no mesmo PR antes do merge. Preservar os estados históricos acima; acrescentar evidências do novo SHA em adendo, sem substituir os runs antigos.


## Adendo observacional pós-merge — CI-V9-PROBE-01 / #1205 (29/10/2026)

**Mudança de estado desde o adendo de 28/10:** a [PR #1205](https://github.com/lucianox777/Jornada/pull/1205) foi **MERGED** em 29/10/2026 19:36:12 UTC, merge \`b4fdc3f80310eeff9b48a4cf01429f485583e49a\`. O HEAD testado foi \`7ca4fcec0ba67741b96b40dd0bcd0e21624ae5c5\`. Os runs [jornada-ci #40895343764](https://github.com/lucianox777/Jornada/actions/runs/40895343764), [fast PR #40895343762](https://github.com/lucianox777/Jornada/actions/runs/40895343762) e [probe #40895343763](https://github.com/lucianox777/Jornada/actions/runs/40895343763) concluíram \`SUCCESS\`; neste último, classificação, TRAIN físico 30k e agregador clean também foram \`SUCCESS\`.

**O que mudou:** o classificador integrado trata \`.github/dependabot.yml\` e \`.yaml\` e a composição com documentação permitida; diffs desconhecidos ou mistos com SQL/Gold/workflow/FS/TF continuam fail-closed. O script de testes contém os casos positivos e negativos correspondentes. Isso é evidência de implementação e de workflows verdes no SHA, **não** de economia medida nem de execução efetiva da via de dispensa em uma PR exclusivamente Dependabot.

**Baseline científico e financeiro inalterado:** não foi produzida comparação A/B com filas, runner-minutes, tempo de calibração TRAIN, mesmo corpus/seed/perfil e gates equivalentes. Portanto, a economia de wall-clock ou custo permanece **NÃO DEMONSTRADA** na [issue #1200](https://github.com/lucianox777/Jornada/issues/1200); não inventar minutos poupados. As observações históricas, inclusive CANCELLED e IN_PROGRESS dos SHAs anteriores, continuam válidas como retratos daquele instante e não devem ser reclassificadas retroativamente.

**Governança:** esta reconciliação documental foi feita **após** o merge da #1205, não no PR original; registrar explicitamente essa dívida de processo. Nenhuma dispensa de TEST independente, aprovação humana, política DT-10/DT-15 ou ativação V9 é inferida destes resultados. A ADR CI-V9-PROBE-01 e a matriz DT-17 recebem adendos correspondentes neste changeset.
