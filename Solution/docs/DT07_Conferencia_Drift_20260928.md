# DT-07 — conferência de drift documental e evidência de engenharia

**Data:** 28/09/2026. **Escopo:** fechamento da auditoria **documental/técnica** de versões, SQL executável e contrato de varredura de segredos. Não representa auditoria de segurança do histórico, homologação institucional, validação estatística do linkage ou publicação normativa. [Inventário DT](Dividas_Tecnicas.md).

## Confronto dos artefatos correntes

| Assunto | Evidência do código/artefato | Resultado e restrição |
|---|---|---|
| Release selada | `../../RELEASE_INFO.txt` declara engenharia `v4.05`, `schema_solution=v3.69` e Base Normativa declarada `v3.64`. | Histórico imutável, **não** atualizar a release selada com o código candidato. |
| Candidata/RC | `../../CANDIDATE_INFO.json` declara `v5.00` com `release_status=NOT_RELEASED` e `solution_schema=v3.70`; `v5.00-rc.1` tem `release_effect=NONE`. `../../LEIA-ME.txt` e `../../SECURITY.md` distinguem os estados. | RC técnica cortada **não** é v5.00 final nem homologação. `Documentos/README.md` registra a Especificação Técnica v3.62 materializada como documento histórico e a Base Normativa v3.64 declarada na release. |
| SQL da Especificação candidata | `../../Documentos/Especificacao_Tecnica_Jornada_v5.00_Candidata.md`, §§14/18, aponta para `Solution/database/Jornada_Fase1_v3.70.sql`; o arquivo existe e foi gerado de `Solution/database/migrations/manifest.txt`. A instalação completa inclui `database/Jornada_Fase1.sql` e migrações, não a base isolada. | Fonte canônica correta no estado 3.70; migrações aditivas 3.71 não promovem `Jornada.SolutionSchema` sem rebind e gates. |
| Varredura da árvore | `../../.github/workflows/ci.yml` instala Gitleaks **8.30.1** com SHA-256 fixo `551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb` e `sha256sum --check --`; executa `scripts/security-secret-scan.sh --current-tree-only` no job `security-analysis`. | Gate automatizado no CI, com evidência local/artefato; não afirma ausência de achados em commits históricos. |
| Contrato do scanner | `../../.gitleaks.toml` herda as regras mantidas (`[extend] useDefault = true`), sem allowlist global. A exceção `generic-api-key` tem apenas três caminhos específicos de fixtures sintéticas. | Qualquer exceção nova exige revisão direcionada; não desabilitar detector amplo. |
| Histórico alcançável | `../../.github/workflows/secret-history-audit.yml` usa `workflow_dispatch`, `fetch-depth: 0`, mesma versão/hash fixos e publica **apenas agregados**, sem secret bruto. O histórico também é varrido em PR que altera **esse workflow**. | Auditoria de histórico e triagem humana de achados da [issue #405](https://github.com/lucianox777/Jornada/issues/405) são **pendências separadas**; não são gate rotineiro nem estão homologadas por este documento. |
| Fronteira HML/PROD | `../../SECURITY.md` e as APIs preservam `deny-by-default` fora de Development até identidade corporativa e segregação ambiental [#378](https://github.com/lucianox777/Jornada/issues/378). | CI verde, RC e varredura corrente **não** autorizam HML/Produção nem substituem governança [#379](https://github.com/lucianox777/Jornada/issues/379). |

## Verificação de ligação de código — DT-08 já entregue

[PR #508](https://github.com/lucianox777/Jornada/pull/508) integrou os quatro arquivos que estavam vinculados fisicamente de modo frágil ao `Jornada.Linkage.Core`. Na conferência de 28/09, os **21 projetos** listados em `Solution/Jornada.sln` (19 src, 2 tests) não apresentam `<Compile Include=` nem links físicos de **código C#**. O projeto `src/Jornada.Linkage.Core/Jornada.Linkage.Core.csproj` referencia `Jornada.Contracts` e contém fisicamente `CombinedIdentityCandidatePlanner.cs`; o Runner referencia o Core por `ProjectReference`. Links `None` para fixtures nos projetos de testes são **recursos de teste**, não dívida de linkagem de código.

A etapa inicial de [DT-16 PR #580](https://github.com/lucianox777/Jornada/pull/580) acrescentou cinco solutions com pertencimento exclusivo dos mesmos 21 projetos, gate de cobertura e builds Release [CI #36409672890](https://github.com/lucianox777/Jornada/actions/runs/36409672890). A `Jornada.sln` monolítica permanece para compatibilidade; mudança física futura e CI especializado seguem [#576](https://github.com/lucianox777/Jornada/issues/576). DT-08 não obriga antecipar a DT-16.

> **Atualização de 30/09/2026:** este parágrafo registra o estado histórico da conferência de 28/09. A DT-16 foi posteriormente revertida; as cinco solutions auxiliares e o gate específico foram removidos, sem migração física dos 21 projetos.

## Critério de aceite e reversibilidade

[TechnicalDebtEvidenceDocumentationTests](../tests/Jornada.Tests/Unit/TechnicalDebtEvidenceDocumentationTests.cs) e [DocumentationDriftContractTests](../tests/Jornada.Tests/Unit/DocumentationDriftContractTests.cs) verificam o acordo entre texto e artefatos, inclusive origens de dados da release/RC, referências SQL e controles mínimos do scanner. A suíte unitária e os gates CI devem passar no **HEAD exato** da PR que introduz este documento, além das regressões preexistentes. Reabrir DT-07 somente se uma mudança real voltar a produzir drift; não encerrar #405, #378 ou #379 por inferência, nem reescrever o arquivo histórico de release.
