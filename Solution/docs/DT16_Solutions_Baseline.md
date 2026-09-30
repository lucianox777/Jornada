# DT-16 — solutions especializadas (REVERTIDA)

A primeira etapa da DT-16 foi integrada em 28/09/2026 pelo PR #580 como um experimento estrutural: foram adicionadas `Jornada.Core.sln`, `Jornada.Runtime.sln`, `Jornada.Linkage.sln`, `Jornada.Dev.sln` e `Jornada.Tests.sln`, além de um gate de cobertura/exclusividade e builds adicionais na CI. Os 21 projetos, seus caminhos físicos, namespaces, APIs e `ProjectReference` não foram alterados.

## Decisão de 30/09/2026

A separação foi **revertida integralmente antes da migração física ou da especialização da CI**. O repositório volta a ter uma única solution suportada: `Solution/Jornada.sln`.

Foram removidos:
- as cinco solutions especializadas;
- `scripts/dt16-solutions-gate.py`;
- o gate DT-16 e os cinco builds adicionais no workflow de CI.

A política posterior do PR #585 para evitar execuções duplicadas de CI em PRs é independente da DT-16 e foi preservada.

Não existe migração física, separação de projetos, mudança de namespace/API nem alteração de lógica de execução associada a esta reversão. A issue #576 fica encerrada pela decisão de manter a solution monolítica. Qualquer tentativa futura de voltar a separar solutions exige uma nova decisão arquitetural explícita.

Este arquivo é mantido somente como registro histórico da decisão e do rollback.
