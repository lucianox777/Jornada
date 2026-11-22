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
