# DT-12 — inventário remoto para aprovação, sem exclusões


**Proteção da evidência contra falso positivo do Gitleaks:** os SHA-1 completos dos heads foram separados do CSV com nomes de branches. `DT12_Branches_20260926.csv` contém `index` estável, nome e flags; `DT12_Branch_Heads_20260926.csv` contém `index` e `head_sha`. Reconstituir por `index`, nunca por ordem implícita. Essa separação preserva todos os 429 SHAs sem colocar um hash com aparência de token na mesma linha de uma branch cujo nome contém `secret`/`password`/`key`; nenhuma exceção de Gitleaks foi criada. Recoletar ambos os arquivos antes de qualquer decisão destrutiva.

**Coleta em 26/09/2026:** 5 páginas de 100/100/100/100/29 branches de `lucianox777/Jornada`. `master` observada em `975911d0ce6f1678cfc42241e99cdc6c6362aaba`; árvore dos arquivos raiz em `975911d0ce6f1678cfc42241e99cdc6c6362aaba`; branch deste relatório partiu de `3f583901df9b784c852c04135555647d3dc2c1bf`. Como outras sessões fazem push/merge em paralelo, são **snapshots de leitura**, não transação GitHub atômica. Recoletar e comparar SHA antes de qualquer decisão destrutiva. PRs abertos abaixo foram capturados **antes da criação desta própria PR**.

**Escopo: somente inventário e proposta de critérios para aprovação. Nenhum `git push --delete`, DELETE na API, exclusão, movimentação ou renomeação de branch ou arquivo foi executado.**

## Resumo do inventário

- Branches: **429** (paginação 100+100+100+100+29); **15** head branches de PR aberto no mesmo repositório, mais `master`; **19** branches classificadas com bloqueio explícito na fotografia.
- Branches nominalmente ligadas a RC/release/segurança/auditoria e que exigem checagem extra: **86**. **Todas as outras branches também estão em revisão**, nunca aprovadas automaticamente para remoção.
- Arquivos `NOTA_*` na raiz: **37**; arquivos `RELEASE*`: **24** (inclui `RELEASE_INFO.txt`); total: **61** preservados.
- Planilhas de conferência: [branches, flags de proteção, PR e classe de retenção](DT12_Branches_20260926.csv), [SHA-1 dos respectivos heads, vinculados por `index`](DT12_Branch_Heads_20260926.csv) e [arquivos NOTA/RELEASE com Git blob SHA](DT12_Root_NOTA_RELEASE_20260926.csv).

### Distribuição por família

| Família | Quantidade |
| --- | ---: |
| `fix` | 161 |
| `feat` | 121 |
| `work` | 47 |
| `docs` | 35 |
| `chore` | 13 |
| `dependabot` | 12 |
| `test` | 12 |
| `(sem prefixo)` | 11 |
| `refactor` | 5 |
| `diag` | 4 |
| `arch` | 1 |
| `automation` | 1 |
| `db` | 1 |
| `design` | 1 |
| `dev` | 1 |
| `post-rc` | 1 |
| `scripts` | 1 |
| `tech` | 1 |

## Bloqueios de exclusão

**Imutáveis sem decisão formal:** branch default `master`; toda branch de PR aberto; qualquer branch em uso em worktree ou outra sessão; branch apontada por tag, issue, documentação, workflow/evidência de release ou auditoria; código ainda não absorvido por `master` nem explicitamente descartado. O flag `protected=false` retornado pela API não é critério de elegibilidade.

**Preservação específica já identificada:** `tech/dt08-linkage-core-physical-ownership` (PR #508 mergeado em 26/09), `docs/dt07-governance-rn-sql-security` (PR #509 recém-mergeado na coleta) e `test/linkage-twin-like-adversarial` (referência histórica de validação). `automation/rc-v500-rc1` e branches RC/segurança/evidência devem aguardar verificação dos vínculos com a tag `v5.00-rc.1` e a cadeia de auditoria.

### PRs abertos durante a coleta

| PR | Branch de origem | Rascunho |
| --- | --- | --- |
| [#514](https://github.com/lucianox777/Jornada/pull/514) | `test/dt01-handcrafted-boundary-conference-20260926` | sim |
| [#513](https://github.com/lucianox777/Jornada/pull/513) | `scripts/dt11-unified-safe-runner` | sim |
| [#511](https://github.com/lucianox777/Jornada/pull/511) | `db/dt06-fresh-baseline-ledger` | sim |
| [#510](https://github.com/lucianox777/Jornada/pull/510) | `fix/dt04-central-access-auth-20260926` | não |
| [#420](https://github.com/lucianox777/Jornada/pull/420) | `dependabot/nuget/Solution/src/Jornada.Api/nuget-minor-patch-9116d52178` | não |
| [#359](https://github.com/lucianox777/Jornada/pull/359) | `dependabot/nuget/Solution/src/Jornada.Pipeline.Coordination/Microsoft.Data.SqlClient-7.1.0` | não |
| [#357](https://github.com/lucianox777/Jornada/pull/357) | `dependabot/nuget/Solution/src/Jornada.Operational.Sql/Microsoft.Data.SqlClient-7.1.0` | não |
| [#126](https://github.com/lucianox777/Jornada/pull/126) | `dependabot/nuget/Solution/src/Jornada.Processor.Worker/Microsoft.Extensions.Hosting-10.0.12` | não |
| [#124](https://github.com/lucianox777/Jornada/pull/124) | `dependabot/nuget/Solution/src/Jornada.Operations.Maintenance.Worker/Microsoft.Extensions.Hosting-10.0.12` | não |
| [#122](https://github.com/lucianox777/Jornada/pull/122) | `dependabot/nuget/Solution/src/Jornada.Linkage.Runner/Microsoft.Extensions.Hosting-10.0.12` | não |
| [#120](https://github.com/lucianox777/Jornada/pull/120) | `dependabot/nuget/Solution/src/Jornada.Linkage.Parameters.Worker/Microsoft.Extensions.Hosting-10.0.12` | não |
| [#118](https://github.com/lucianox777/Jornada/pull/118) | `dependabot/nuget/Solution/src/Jornada.Bronze.Maintenance.Worker/Microsoft.Extensions.Hosting-10.0.12` | não |
| [#86](https://github.com/lucianox777/Jornada/pull/86) | `dependabot/nuget/Solution/src/Jornada.Bronze.Verify/Microsoft.Extensions.Configuration.Json-10.0.12` | não |
| [#85](https://github.com/lucianox777/Jornada/pull/85) | `dependabot/nuget/Solution/src/Jornada.Bronze.Verify/Microsoft.Extensions.Configuration.EnvironmentVariables-10.0.12` | não |
| [#83](https://github.com/lucianox777/Jornada/pull/83) | `dependabot/nuget/Solution/src/Jornada.Api/nuget-minor-patch-bf69a00f4e` | não |

## Arquivos NOTA e RELEASE

A raiz contém `NOTA_CONVERGENCIA_V3.68.txt` e notas `NOTA_ENGENHARIA_*` até V4.05, com lacuna de V3.81; `RELEASE_INFO.txt` e o conjunto de arquivos `RELEASE_v*` V3.83–V4.05. Os SHA Git por arquivo constam na planilha. Esses documentos podem comprovar transições de arquitetura, artefatos de release e trilha de decisão. **Manter 100% até que a retenção/arquivo de auditoria seja aprovada**. `CANDIDATE_INFO.json` e a tag `v5.00-rc.1` permanecem fora de qualquer proposta de limpeza.

## Procedimento de aprovação para uma eventual PR posterior

1. Aprovação explícita por responsável da política de retenção, prazo, destino de arquivamento e **lista nominal** de candidatos. Este relatório não autoriza exclusão.
2. Recoletar no dia da execução branches com SHA, PRs abertos e fechados, tags, issues, workflows, references em docs e worktrees. Alteração de HEAD, PR aberto ou uso ativo remove o candidato do lote.
3. Verificar `git merge-base --is-ancestor <branch_sha> master` e dependências de tags/releases/evidência; se não for ancestral, exigir decisão técnica explícita sobre o conteúdo não integrado. Não presumir que branch antiga seja descartável.
4. Para NOTA/RELEASE, produzir mapa de links históricos e classificação auditável antes de propor mover para `docs/archive/`, preservando conteúdo e histórico. Nunca reescrever história para ocultar releases.
5. Antes de exclusão aprovada, produzir espelho/`git bundle` autorizados com verificação de recuperação, diff/`dry-run` e novo PR separado; parar diante de qualquer dúvida sobre auditoria ou execução paralela.

### Branches com indícios nominais de auditoria/RC (não são lista de exclusão)

- `automation/rc-v500-rc1`
- `chore/post-rc-secret-hardening-405`
- `chore/rc-cut-contract-hardening-20260920`
- `chore/security-historical-gitleaks-audit`
- `chore/security-invariants-pre-rc-398`
- `chore/technical-rc-versioning`
- `chore/405-sqlcmd-password-env-20260925`
- `diag/linkage-candidate-pair-prior`
- `docs/align-v500-rc-gates`
- `docs/candidate-info-146`
- `docs/candidate-nomemae-current-state`
- `docs/candidate-reference-baseline-20260920`
- `docs/dt12-branch-release-inventory-approval`
- `docs/mark-pendencias-v147-historical`
- `docs/post-rc-governance-404`
- `docs/pre-hml-sqlserver-single-runtime-cleanup`
- `feat/gitleaks-ci-405`
- `feat/hml-scale-evidence`
- `feat/identity-decision-evidence-taxonomy-20260921`
- `feat/identity-structured-evidence-ledger-391`
- `feat/linkage-abbreviation-compatibility-audit`
- `feat/linkage-additional-evidence-readiness`
- `feat/linkage-audit-roundtrip-20260920`
- `feat/linkage-calibration-audit-export-20260920`
- `feat/linkage-conference-evidence-20260920`
- `feat/linkage-decision-quality-audit`
- `feat/linkage-evidence-dependency-diagnostic`
- `feat/linkage-score-evidence-decomposition`
- `feat/linkage-truth-candidate-audit`
- `feat/scale-observability-evidence`
- `feat/synthetic-evaluation-evidence-416`
- `fix/candidate-schema-provenance`
- `fix/consolidacao-schema-requisitos-release`
- `fix/evidence-gates-determinism`
- `fix/gitleaks-scan-history-after-tree-findings`
- `fix/identity-confirmation-rc-hardening-20260921`
- `fix/linkage-decision-evidence-v6`
- `fix/linkage-evidence-profiles`
- `fix/linkage-incremental-candidate-side-418`
- `fix/linkage-pass-fanout-scale-evidence`
- `fix/linkage-scale-evidence-gates`
- `fix/linkage-scale-evidence-provenance`
- `fix/linkage-single-birth-evidence`
- `fix/linkage-single-birth-evidence-v2`
- `fix/local-audit-container-discovery`
- `fix/rc-evidence-dispatch-preflight-20260920`
- `fix/rc-evidence-preflight-hardening-20260920`
- `fix/rc-provenance-hml-environment-guard`
- `fix/rc-restore-drill-seed-objects`
- `fix/scale-evidence-quality-corpus`
- `fix/scale-evidence-quality-corpus-v2`
- `fix/security-backup-sqlcmd-env-405`
- `fix/security-ci-sqlcmd-env-405`
- `fix/security-compose-require-sql-password`
- `fix/security-ddl-synthetic-sqlcmd-405`
- `fix/security-dev-sql-shell-entrypoints-405`
- `fix/security-e2e-cluster-sqlcmd-405`
- `fix/security-e2e-sqlcmd-env-405`
- `fix/security-gitleaks-evidence-failclosed-405`
- `fix/security-ibge-sqlcmd-argv-405`
- `fix/security-independent-linkage-validation-sql-env-405`
- `fix/security-linkage-flow-evaluation-sql-env-405`
- `fix/security-linkage-workflow-sql-env-405`
- `fix/security-local-db-sqlcmd-env-405`
- `fix/security-lock-probe-sqlcmd-env-405`
- `fix/security-rc-sqlcmd-env-405`
- `fix/security-readiness-fail-closed`
- `fix/security-release-sql-probe-env-405`
- `fix/security-scale-sqlcmd-env-405`
- `fix/security-schema-ci-env-405`
- `fix/security-schema-ci-sqlcmd-env-405`
- `fix/security-synthetic-ps-sqlcmd-405`
- `fix/technical-rc-routing-evidence`
- `fix/405-cluster-sqlcmd-env-20260925`
- `fix/405-deterministic-cpf-test-fixtures`
- `fix/405-evidence-triplet-secret-env`
- `fix/405-multiline-docker-secret-argv-gate`
- `fix/405-secure-first-run-local-env`
- `fix/405-sqlcmd-argv-recurrence-gate`
- `fix/416-frozen-fs-test-failure-evidence`
- `post-rc/integration-410-412`
- `test/blocking-pass-evidence`
- `test/linkage-candidate-side-eligibility-wording`
- `work/identity-history-closure`
- `work/identity-history-serving`
- `work/postgresql-linkage-v2-candidate-universe`

**Decisão aguardada:** aprovação da política de retenção e autorização de uma futura etapa de verificação detalhada, sem qualquer remoção neste DT-12.
