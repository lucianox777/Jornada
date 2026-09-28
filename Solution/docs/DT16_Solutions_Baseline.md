# DT-16 — primeira etapa: solutions especializadas

Esta etapa adiciona `Jornada.Core.sln`, `Jornada.Runtime.sln`, `Jornada.Linkage.sln`, `Jornada.Dev.sln` e `Jornada.Tests.sln`, com pertencimento exclusivo dos 21 projetos existentes. Os projetos continuam nos caminhos físicos atuais e as `ProjectReference` permanecem intactas; referências transitivas entre solutions são permitidas. A solução `Jornada.sln` é mantida temporariamente para compatibilidade com o CI e os scripts existentes.

O gate `scripts/dt16-solutions-gate.py` verifica cobertura integral, exclusividade e existência dos `.csproj`. O CI também compila as cinco solutions em Release. A migração de caminhos físicos, CI especializado e remoção da solution monolítica ficam para etapas posteriores, após inventário de Dockerfiles, scripts, workflows e documentação. Não se reivindica ganho de desempenho sem medição.

Rollback: reverter esta PR; não há alteração de namespace, API, código de execução ou localização de projetos. Referência: issue #576.

## Execução interina — uma CI integral por PR, sem fragmentar jobs

A configuração anterior de `.github/workflows/ci.yml` iniciava o mesmo pipeline nos eventos `push` e `pull_request` para uma mesma branch de trabalho com PR aberto. Como as referências de execução diferem (`refs/heads/...` e `refs/pull/.../merge`), o grupo de concorrência existente não impedia duas execuções integrais simultâneas.

A configuração interina preserva **um único workflow `jornada-ci` e todos os gates atuais**, incluindo os builds Release das cinco solutions e a `Jornada.sln`: `push` automático somente em `master` e em **todas as tags** (inclusive RC); `pull_request` continua cobrindo os PRs; `workflow_dispatch` preserva ensaios manuais. Commits em branches sem PR deixam de disparar CI integral automaticamente: abrir o PR ou executar `workflow_dispatch` quando necessário. O push para `master` após cada merge produz uma execução pós-merge distinta e intencional.

**Não houve divisão de jobs, remoção de teste, migração física nem mudança dos gates de release.** Um teste contratual versionado (`CiTriggerPolicyTests`) protege o escopo de eventos. Esta otimização reduz disparos redundantes, mas não comprova redução de duração ou consumo sem comparar Actions reais. Os jobs e a `Jornada.sln` continuam monolíticos até decisão futura da DT-16 (#576).

Rollback: reverter a alteração restrita de `on.push` e retirar este contrato; nenhum artefato runtime ou banco foi alterado.

## Escopo da rodada de 28/09/2026

Por decisão de execução, a especialização/divisão da CI e a retirada de `Jornada.sln` **não integram esta rodada**. Permanecem o único workflow `jornada-ci`, seus oito gates por PR e a correção de disparo duplicado do PR #585. É permitido desenvolver/testar mudanças em fatias pequenas localmente; o merge depende dos oito gates completos no HEAD exato, sem reiniciar Actions em andamento por timeout de consulta. A DT-10 deverá ser tratada por último, após os demais PRs autorizados. Nenhuma conclusão de CI sintética autoriza promoção de modelo, HML ou Produção.
