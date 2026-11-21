# GOV-OPS-P0A-MERGE-MATRIZ-ARQUITETURAL-20261121

**Estado:** autorização expressa do Product Owner executada; saúde da master ainda não certificada.
**Política:** GOV-DOC-01.

## Decisão e contexto
O Product Owner autorizou expressamente: “Eu autorizo a fazer o merge com o master.” A autorização se refere à PR #1789, correção da matriz arquitetural dos projetos DEV Jornada.Linkage.SyntheticCorpus.Parametrized e PartitionIntegritySmoke. Merge squash realizado na master, SHA 3dda9e2557012dbf11cc45ab5294fc1b732fe85c.

## Justificativa e evidências
- Jornada CI run #44850469559, SHA 658889307efccc588c6d93139bfdc291c4655ac5: failure.
- unit-contracts job #136680823475: falha do architecture-dependency-gate.py por projetos sem política; unit falhou em cascata, runtime success.
- dependency-lock, integration-sql, e2e e unit-runtime: success nessa CI.
- Jornada CI Diagnostics run #44851594485: success; artefato #14684914174 publicado.
- PR #1789 altera apenas Solution/config/release/architecture-dependencies.json para refletir ProjectReference existentes nos dois csproj, sem relaxar invariantes do gate.
- CI rápida da PR #1789 run #44852319716: quatro jobs success; não abrange unit-contracts.

## Impactos, limites e precedência
A autorização cobre somente integração da correção. Não comprova saúde da master nem autoriza LOCK, TRAIN/TEST físico, V9, DT-15 ou release. Mantida precedência P0-A → DT-10 → P0-B e política fail-closed. Issue #1734 permanece aberta até CI integral pós-merge, Diagnostics do novo SHA e comprovação da proteção master.

## Rastreabilidade
- PR: https://github.com/lucianox777/Jornada/pull/1789
- P0-A: https://github.com/lucianox777/Jornada/issues/1734
- CI anterior: https://github.com/lucianox777/Jornada/actions/runs/44850469559
- Diagnostics anterior: https://github.com/lucianox777/Jornada/actions/runs/44851594485
- CI rápida: https://github.com/lucianox777/Jornada/actions/runs/44852319716
- Merge SHA: 3dda9e2557012dbf11cc45ab5294fc1b732fe85c
- Próximo checkpoint: CI push + unit-contracts + Diagnostics no novo SHA e branch protection.
