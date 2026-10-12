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

## Invariantes

- O workflow diagnóstico mantém `permissions: actions: read; contents: read`. O checkout da rotina de análise aponta explicitamente a `master` confiável, não o código do evento `workflow_run` ou de PR.
- O job `impact` da CI obrigatória executa os testes de perfil. Nenhum gate é removido, e não são alterados o contrato lockfile, SQL, integração, E2E, CodeQL ou segurança.
- Não inserir em artefatos dados de produção, tokens ou logs brutos; somente timestamps e nomes dos passos com escape HTML/Markdown.
- Nunca usar `JornadaLocal`, cluster DEV do usuário, HML/PROD, IBGE original, volumes ou contêineres existentes. Apenas APIs do GitHub e runners efêmeros.

## Continuação da refatoração integral

Com duas ou mais execuções comparáveis, priorizar uma PR separada para eliminar duplicidade de build com artefatos atestados por **HEAD SHA exato**, versão .NET, os locks versionados e proveniência; validar equivalência de binários antes de trocar cada build. O `deterministic-build` duplo continua como gate até substituição comprovadamente equivalente.

Agente autônomo de correção de código por PR é etapa distinta: precisa credenciais temporárias com escopo próprio, isolamento de PR não confiável e testes integrais. O watchdog #930 só reexecuta falhas de infraestrutura confirmadas, não altera código.
