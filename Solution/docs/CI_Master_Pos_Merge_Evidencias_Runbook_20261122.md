# CI da master pós-merge — roteiro de coleta de evidências

**Escopo:** diagnóstico operacional da issue #1895. Este roteiro não altera critérios de integração, não constitui decisão nova do Product Owner e não certifica o E2E físico, o Runner V9 ou TRAIN/TEST científico.

## Problema de observabilidade

O conector GitHub `fetch_commit_workflow_runs` disponível neste fluxo filtra execuções acionadas por `pull_request` e retorna somente a primeira página. Portanto, resultado vazio para o SHA de um commit squash na `master` **não demonstra** ausência de execução `push`. A configuração `.github/workflows/ci.yml` contém gatilhos `push` para `master` e `workflow_dispatch`, mas a configuração, isoladamente, tampouco demonstra execução.

## Procedimento verificável

1. Registrar o SHA exato da `master` imediatamente após o merge, a data/hora UTC, a PR e o SHA do HEAD previamente validado.
2. Na aba **Actions** do GitHub, selecionar o workflow `jornada-ci`, filtrar por branch `master` e evento `push`; alternativamente, consultar a API GitHub Actions de **list workflow runs** com `branch=master` e `event=push`, com paginação.
3. Localizar a execução cujo `head_sha` seja **exatamente** o SHA registrado no passo 1. Registrar `run_id`, `run_attempt`, `event`, `head_branch`, `head_sha`, `status`, `conclusion`, horários e link permanente.
4. Consultar **list jobs for a workflow run**, incluindo paginação e tentativa correspondente. Registrar cada job e suas conclusões. Nunca converter `queued`, `pending`, `in_progress`, `skipped`, `neutral`, ausência de job ou lista vazia em `success`.
5. Se não houver execução `push` correspondente, registrar explicitamente **SEM EVIDÊNCIA DE RUN**, não **PASS** nem **FALHA DE RUNNER**. Verificar filtros, retenção, permissões, fila, concorrência e indisponibilidade de runners usando dados específicos antes de atribuir causa.
6. Anexar evidências auditáveis à issue #1895 e relacionar a issue #1851 quando houver impacto no E2E DEV. Encerrar a #1895 apenas com causa/impedimento documentado e evidência pós-merge exigida por seus critérios.

## Modelo de registro

| Campo | Valor a preencher |
| --- | --- |
| PR / HEAD antes do merge | Não verificado |
| SHA squash na master | Não verificado |
| Workflow / evento / branch | Não verificado |
| Run ID / tentativa | Não verificado |
| Status / conclusão do run | Não verificado |
| Jobs e conclusões | Não verificado |
| Evidência (URL permanente) | Não verificado |
| Limitações / impedimentos | Não verificado |
| Responsável / instante UTC | Não verificado |

## Critérios independentes

- **Gate de PR:** `unit-runtime`, `deterministic-build` e `security-guards` devem estar `completed/success` no **HEAD exato** da PR antes do squash merge, além de `mergeable=true`.
- **Validação da master:** evidência de execução pós-merge para o **SHA exato** na `master`. A aprovação anterior da PR não substitui esta evidência.
- **Certificação científica/produtiva:** permanece separada, sujeita às autorizações e critérios vigentes.

**Rastreabilidade:** GOV-DOC-01; issues #1895 e #1851; `.github/workflows/ci.yml`; `AGENTS.md`.

## Incidente unit-runtime no SHA 0052680f — 22/11/2026 (issue #1895)

**Evidência primária:** `jornada-ci` `push/master`, run [`45063196111`](https://github.com/lucianox777/Jornada/actions/runs/45063196111), tentativa 1, SHA exato `0052680fbb4d5bd6546c06665de0a39eea37dfe1`, conclusão `failure`. O job `unit-runtime` `137338287703` falhou no teste NUnit `Second_worker_can_start_while_first_large_group_is_still_running`, com `MaximumSimultaneousWorkersObserved` esperado `>=2` e observado `1`. O job agregador `unit` `137339485630` falhou por dependência de `unit-runtime`, sem ser uma segunda causa raiz. O upload OpenAPI sem arquivo após a falha é efeito posterior, não evidência independente de defeito funcional.

**Diagnóstico e causa específica:** o teste de `V9CalibrationRuntimeSchedulerTests.cs` usa `Parallel.ForEachAsync`, que não garante qual índice alcançará primeiro o gate de admissão. Antes da correção, o caso `i==1` sinalizava `secondStarted` e podia terminar antes de `i==0` ser admitido, tornando possível observar dois índices executados em instantes distintos com máximo simultâneo `1`. A prova pretendida é capacidade de admitir dois workers **simultâneos**, não ordem natural de disparo dos índices. A hipótese de corrida é fundamentada no código e no resultado observado; a reprodução física do interleaving específico não foi realizada fora da CI.

**Correção proposta nesta branch:** sincronizar o caso `i==1` com `await firstStarted.Task.WaitAsync(ct)` antes de sinalizar `secondStarted`. O `i==0` continua bloqueado pelo `releaseFirst` até a confirmação do segundo worker; portanto, o teste passa a medir sobreposição real independentemente de qual índice entrou primeiro. Nenhum limite de RAM/CPU, política do Scheduler, parâmetro científico, TEST, modelo, gate ou workflow é alterado. Commit da correção de teste `4c090b980e153655b4aea3e2eed8321193e43889`.

**Estado e precedência:** investigação e patch mínimo de teste em PR separada, sem afirmar validação antes dos checks; a execução original permanece `failure`, sem retry indiscriminado. Este registro não constitui nova decisão do PO nem atestado de equivalência científica. Aplicam-se GOV-DOC-01 e GOV-OPS-CORRECAO-SQUASH-CI-MASTER-20261111. Integração exige os três checks da PR no HEAD exato e verificação de `jornada-ci` pós-squash no novo SHA da master.
