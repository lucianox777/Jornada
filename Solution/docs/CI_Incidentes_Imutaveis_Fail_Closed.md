# CI — Incidente estruturado por SHA e run_id, sem privilégios de correção

**Data:** 12/10/2026. A CI já tem checkpoints nativos de jobs aprovados e um watchdog para reexecuções de infraestrutura (#930, #934, #938). Falta ao agente de diagnóstico um artefato **persistente, legível por máquina e minimizado**, que sobreviva ao fim da execução e identifique o ponto exato a investigar sem reiniciar jobs para observar o problema.

## Novo artefato

O workflow já existente `jornada-ci-diagnostics`, acionado pelo término de `jornada-ci`, continua com **somente `actions:read` e `contents:read`** e checkout **exclusivamente de `master` confiável**, jamais do código PR. O novo script `ci-failure-checkpoint.py` lê **somente** o JSON de execução concluída e a lista de jobs da execução, já coletados pelo perfil (#931). Depois de validar `run_id`, tentativa, HEAD de 40 caracteres, job IDs, `status=completed` e totalidade da paginação, produz `incident.json`.

Conteúdo: versão do schema, ID estável por `run_id + SHA` (não muda no `rerun-failed-jobs`), tentativa atual, SHA, totais de jobs aprovados/ignorados, jobs falhos e seus passos falhos resumidos, e classificação **preliminar**. Se `unit`, `e2e` ou `security-analysis` falhar na verificação inicial de dependências, o incidente registra o estado dos produtores, **mas jamais atribui** causa infraestrutural sem logs de runner. Cabe ao watchdog independente e à análise do agente verificar logs e decidir; o JSON explícita `retry_authorization: none_read_only_observation`.

O artefato `ci-failure-checkpoints` fica disponível no GitHub Actions por **7 dias**, na própria execução de diagnóstico, para consulta na rotina horária e futura correção autônoma. O relatório agregado entra no `GITHUB_STEP_SUMMARY`. O JSON não incorpora logs, variáveis de ambiente, argumentos de execução, tokens, conteúdo de testes nem dados pessoais; nomes e passos são sanitizados e limitados.

## Limites de segurança

- **Não** modifica PRs, código, checks, banco, branch, logs nem aprova merge; não adiciona `actions:write`, `contents:write` ou `pull_request_target`.
- Execuções ou listagens incompletas não produzem relatório falsamente verde; a ferramenta falha fechada.
- ID só retém continuidade da **mesma execução e SHA**. Um commit novo precisa refazer seus gates exigidos; o checkpoint serve à triagem e rastreabilidade, não a herdar aprovações.
- A recuperação seletiva infra mantém sua política de prova explícita de runner, HEAD atual e uma só reexecução; correção autônoma de código em outro SHA continua condicionada à validação independente.
- Uma CI obrigatória falha não é transformada em sucesso por existir `incident.json`.

## Validação

A suíte de testes offline, executada cedo no gate `impact`, verifica paginação completa, agendamento de reexecução com mesmo ID, dependência agregada sem falsa prova infra, escape de nomes injetados, isolamento do workflow e **ausência de privilégio de escrita**. Após merge, conferir um evento de `workflow_run` de verdade e o artefato produzido na execução `jornada-ci-diagnostics`, sem comprometer os demais gates.

**Dados:** SQL dos runners é descartável. Não acessar JornadaLocal, HML/PROD, NODE, NAS, volumes ou corpus IBGE original.
