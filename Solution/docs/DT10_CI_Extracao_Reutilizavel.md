> **Revisão transversal 09/10/2026:** este documento registra a
> primeira extração SQL da CI, integrada por #858 e documentada
> adicionalmente por #859. A mudança **não removeu nem
> desmembrou todos os builds**; o trabalho restante deve seguir os
> gates condicionais e artefatos da HEAD exata. Para a visão de todo
> o sistema, consulte o
> [Manual integrado](Manual_Sistema_Consolidado_20261009.md).
> “DT-10” aqui é extração de CI; a dívida histórica DT-10 de
> publicação SQL tem aceite técnico próprio e não deve ser confundida.

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

## Resultado verificado da integração (09/10/2026)

- PR [#858](https://github.com/lucianox777/Jornada/pull/858) integrada em `master` por **squash merge** em 2026-10-09 18:10:20 UTC; commit de integração `97efa6e3261d84d14c8d9553e7f83f76eaed7e5d`.
- HEAD validada: `871e1baee41672097a19b582a78b9ee7c9cfbfd9`. Execução [jornada-ci #9796](https://github.com/lucianox777/Jornada/actions/runs/37970495157), conclusão **success**.
- Dez gates executados com **success**: `impact`, `dependency-lock`, `ddl-upgrade`, `deterministic-build`, `harness-smoke`, `security-analysis`, `integration-sql`, `e2e`, `unit` e `dt10-evidence / dt10-evidence` (workflow reutilizável aninhado, não skipped).
- Artefato `dt10-evidence` publicado na mesma execução: ID `11635467318`, 21.470 bytes, digest `sha256:28351cd12fad41be6ee66d79334250921005cdcaf98fdf9b09386246fcd05268`. A existência do artefato e o sucesso do job foram verificados pela API do GitHub; esta atualização documental **não** reanalisa o conteúdo interno do ZIP nem atesta separadamente as contagens individuais dos testes.
- `scale-harness`, `bronze-restore-drill` e jobs de publicação RC/release foram `skipped` nesta execução por escopo/condição; não integram a lista dos dez gates executados acima.
- Nenhuma revisão ou thread bloqueante foi encontrada antes do merge. A PR ficou fechada e marcada como merged após a operação.

**Limite de conclusão:** a DT-10 entregou apenas a **primeira extração incremental** do gate SQL para reusable workflow. Não declarar otimização completa de toda a CI, publicação normativa, homologação ou implantação em HML/PROD. Não foram executadas ações locais sobre JornadaLocal, IBGE original, dados reais ou volumes/containers do usuário nesta integração.
