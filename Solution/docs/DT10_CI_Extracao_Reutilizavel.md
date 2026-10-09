# DT-10 — Extração incremental do gate SQL de evidência para workflow reutilizável

**Escopo:** primeira separação real de execução da CI, mantendo o grafo de
gates obrigatórios e as evidências do mesmo `github.run_id`. **Não é**
a separação completa de todas as trilhas e não elimina os builds repetidos
dos demais jobs.

## Alterações desta PR

- `.github/workflows/ci.yml` mantém o job `dt10-evidence` com o mesmo
  `needs: [impact, dependency-lock]` e a mesma condição de pull request
  `run_dt10=true`. O job passa a chamar
  `./.github/workflows/dt10-evidence.yml` como reusable workflow;
  não há segundo `workflow_dispatch`, push, runner E2E ou build duplicado.
- O novo workflow chamado preserva o SQL Server efêmero
  `2022-CU26` fixado em digest, database sintética
  `JornadaSyntheticDev`, artefato `nuget-lockfiles` vindo do
  `dependency-lock` da **mesma execução**, restore `--locked-mode`,
  build Release, execução dos testes `TestCategory=DT10Evidence`,
  gate `--forbid-skipped --minimum-tests 4` e publicação de
  `dt10-evidence` mesmo quando o teste falha.
- O classificador agora liga `run_dt10` também quando **o próprio**
  reusable workflow sofre alteração; sem isso, uma PR poderia quebrar
  o script do gate enquanto o job era ignorado.
- Testes de contrato da CI exigem todos os dez jobs no workflow
  principal, dependências obrigatórias, condição de impacto, SQL
  sintético, restore bloqueado, aceitação real e artefatos obrigatórios.
  Nenhum gate foi removido ou tornado `continue-on-error`.

## Como avaliar corretamente

Para esta PR, a CI precisa executar realmente a chamada aninhada do
`dt10-evidence` e publicar o artefato. A aprovação de testes
**estáticos** não é aceitação operacional: verificar no GitHub os
status dos dez gates principais e a chamada `dt10-evidence` (filha)
na HEAD exata. Se um job ficou `skipped` indevidamente ou mudou o
status obrigatório esperado, corrigir antes do merge.

A extração do DT-10 **não acelera automaticamente os demais builds
.NET**; esses continuam nas trilhas `unit`,
`deterministic-build`, `integration-sql`, `security-analysis`,
`harness-smoke` e `e2e`. Reuso de artefato compilado entre
runners exigirá proveniência por SHA, configuração unificada,
testes de equivalência e aceitação de segurança em PR separada.

## Limites de segurança

Não criar/acessar, parar, recriar, migrar ou limpar `JornadaLocal`,
IBGE original, SQL/Compose/NODE do usuário, volumes ou contêineres
ordinários, HML/PROD. A evidência DT-10 usa somente SQL efêmero
do runner GitHub e base sintética própria. Nunca executar
`docker prune`, `down -v` global nem reset local. Trilha 4
permanece suspensa. Outras lacunas da Console, incluindo o
cancelamento explicitamente confirmado de RunOnce, não são
resolvidas por esta PR.
