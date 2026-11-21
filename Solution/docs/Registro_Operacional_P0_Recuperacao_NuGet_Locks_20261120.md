# Registro operacional — recuperação P0 dos NuGet locks (20/11/2026)

**Situação:** PR #1779 pronta para integração; autorização expressa do Product Owner em 20/11/2026 (conversa do projeto), condicionada à proteção do GitHub. Integração e CI pós-merge **comprovadas quanto ao `jornada-ci`** em 21/11/2026 (ver aditamento abaixo); encerramento integral P0-A pendente; nenhuma certificação científica.

## Contexto e causa
Após as PRs #1769 e #1778, o job `dependency-lock` da `master` falhou por ausência de `src/Jornada.Linkage.SyntheticCorpus.Parametrized/packages.lock.json`. Também faltava `tests/PartitionIntegritySmoke/packages.lock.json`. Ambos os projetos estão fora de `Jornada.sln`; a restauração somente da solução não produz seus locks. Evidência: runs 44835180396, 44835833637 e 44838562061; issue #1734.

## Abordagem operacional e justificativa
Para não depender de instalação do SDK/NuGet no ambiente DEV bloqueado por proxy, foi utilizado workflow temporário no branch isolado `fix/p0-generate-nuget-locks-actions`, SDK fixado em 10.0.113. O workflow restaura a solução com force-evaluate e recusa alterações nos 22 locks existentes; restaura explicitamente `tests/PartitionIntegritySmoke/PartitionIntegritySmoke.csproj`, verifica os dois locks, executa restore locked e gate estrutural, e publica artefato.

**Evidência de execução:** GitHub Actions run [44839190646](https://github.com/lucianox777/Jornada/actions/runs/44839190646), job 136644027715, conclusão success, artefato `p0-generated-nuget-locks` id 14680405754 (expira 27/11/2026). Dois locks recuperados do artefato e adicionados à PR #1779. Manifesto `config/release/nuget-lock-provenance.json` atualizado somente em `candidateGraph` (22→24); histórico v4.05 preservado.

## Impactos e limites
- Nenhuma modificação nos manifests científicos congelados, seeds ou fingerprints.
- Não alterar a composição de `Jornada.sln` nesta correção.
- Não inferir DT-10, TRAIN/TEST ou CI de `master` aprovados a partir do job de geração.
- Workflow temporário deve ser avaliado para remoção antes da integração; não constitui autorização para execução científica.
- PR #1779 depende de gate de proveniência, verificações obrigatórias, revisão e validação pós-merge; manter issue #1734 aberta até evidência física.

## Precedência e rastreabilidade
GOV-DOC-01; índice de decisões vigentes; decisões científicas LOCKED prevalecem. A correção é operacional e subordinada aos gates e ao protocolo canônico de CI/squash da `master`. Referências: PR #1779, issue #1734, run 44839190646 e PR-required-fast run 44840282297.

## Decisão do PO — autorização de integração (20/11/2026)
**Contexto:** recuperação P0 de dois NuGet locks ausentes; PR #1779, HEAD `38a0eb764750c81a32dc2fbb518b56f832e78b0c`, sem revisão formal registrada. **Decisão:** PO respondeu expressamente «Autorizado» à proposta de integrar a PR #1779 na `master` sem revisão adicional, desde que o GitHub permita. **Justificativa:** gates estrutural e de proveniência aprovados (run 44842133581), CI rápida aprovada (44842405531) e diagnóstico TF LOCKED aprovado (44842405528); workflow temporário removido. **Impactos e limites:** permitir merge operacional observadas regras de proteção; não dispensar verificações obrigatórias nem certificar DT-10, TRAIN/TEST, modelo ou publicação. **Precedência:** GOV-DOC-01 e decisões científicas LOCKED permanecem superiores; validar CI da `master` após integração e manter issue #1734 aberta até evidência física. **Rastreabilidade:** PR #1779, commits `f0e6df60` e `38a0eb76`, runs citados e autorização explícita do PO.

## Aditamento de evidência pós-merge — 21/11/2026 (sem nova decisão do PO)

A PR #1779 foi integrada na `master` por squash `d57fc721a4bfb0ddd60d386b528ee2249551cfec`. O diagnóstico original e a autorização do PO em 20/11 permanecem inalterados; a condição de validação pós-merge registrada no início deste documento foi **atendida quanto à CI principal** no SHA posterior `8c651501bd10c73840a1fef5b38d327feb7e263f` (merge documental PR #1800), sem atribuir a esse SHA o status de HEAD permanente.

**Prova:** `jornada-ci` evento `push` na `master`, run [44861177485](https://github.com/lucianox777/Jornada/actions/runs/44861177485), 21 jobs concluídos: **16 success, 5 skipped, zero failure**. `dependency-lock` job 136720511487 **success**, `harness-smoke` job 136720652998 **success**; também `impact`, `ddl-upgrade`, `security-codeql`, `e2e-private-sql`, `e2e-console`, `unit-runtime`, `deterministic-build`, `unit-contracts`, `security-guards`, `dt10-evidence`, `integration-sql`, `unit`, `security-analysis` e `e2e` **success**. `scale-harness`, `bronze-restore-drill`, `rc-publish`, `release-promotion` e `rc-evidence` **skipped**, não executados nem certificados.

**Integridade LOCKED:** auditor `audit-v9-tf-locked-generator.py --repo . --require-pinned` registrado no checkout da PR #1800: 25 arquivos, digest `924ad6455e15b73093f7d4834174966717de13be9b89b14b9640d11ffbea8deb`, digest de partição `83015950790a5be15a5fb843c8330c8cccb10610f1a208ee46eeb7fc348c18e4`, `pinned=true`. Trata-se de prova no checkout da PR; confirmação explícita no SHA `master` e reconciliação completa dos critérios de #1734 permanecem pendentes. Não alterar manifests ou fingerprints.

**Proteção e precedência:** a decisão `GOV-CI-RULESET-AUDITOR-DEFER-20261120` adiou correção do auditor clássico para dívida técnica #1765, sem revogar proteções; não inferir ausência de ruleset por HTTP 403/404. A decisão `GOV-CI-DOCS-ONLY-FAST-20261119` dispensa etapas pesadas apenas para PR documental e preserva o contrato de `push master`/`jornada-ci`; **não existe aprovação nesta atualização para reduzir o `push` documental da master**. DT-10 possui aceite expresso do PO (`GOV-DT10-ACEITE-EXPRESSO-PO-20261120`); o sucesso de `dt10-evidence` aqui não equivale a nova autorização científica.

**Estado:** recuperação de `dependency-lock` e CI principal pós-merge **comprovada**; encerramento integral P0-A #1734 **não declarado** até reconciliação dos critérios remanescentes. Aditamento estritamente documental, sem decisão nova, sem autorização TRAIN/TEST/100k/LOCKED/Gold/Serving. Rastreabilidade: issue #1734 e run #44861177485. GOV-DOC-01.
