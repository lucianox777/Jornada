# Registro operacional — recuperação P0 dos NuGet locks (20/11/2026)

**Situação:** correção proposta na PR #1779 (draft); não integrada, não certificada. Registro de execução, não autorização de merge nem decisão científica.

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
