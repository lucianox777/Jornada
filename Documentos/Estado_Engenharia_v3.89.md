# Estado da Engenharia — v3.89

**Base Normativa:** v3.62
**SolutionSchema:** v3.68
**Solution Engenharia:** v3.89

## Objetivo

Fechar o achado da execução real da v3.88: a infraestrutura Integration já iniciava, porém o banco descartável gerado pela fixture não satisfazia os próprios guards fail-closed de nome.

## Correções

- `SqlServerIntegrationSetUp` cria `JornadaIntegrationTest_<guid>` em vez de `JornadaIntegration_<guid>`;
- os guards que exigem `Test`, `Dev` ou `Local` permanecem intactos;
- `scripts/local-clean.ps1` torna-se limpeza local canônica e preserva a imagem SQL Server;
- `scripts/local-validate-release.ps1` torna-se validação Release canônica e restaura explicitamente Solution/Unit/Integration em `--locked-mode`;
- o validador compila explicitamente os assemblies de teste antes de executá-los;
- o caminho Integration canônico força Testcontainers descartável, evitando conexão residual do shell;
- `local-validate-release-r4.ps1` e demais variantes temporárias não pertencem à release.

## Evidência externa conhecida da v3.88

- restore locked da Solution: OK;
- restore locked Unit: OK;
- restore locked Integration: OK;
- build Release: 0 warnings / 0 errors;
- Unit: 153/153 PASS;
- build Integration: 0 warnings / 0 errors;
- Integration: 58 executados, 5 PASS, 53 FAIL;
- as 53 falhas observadas compartilham a mesma causa: nome `JornadaIntegration_<guid>` recusado pelos guards Test/Dev/Local.

## Pendente

Reexecutar v3.89 em ambiente real, preferencialmente após `scripts/local-clean.ps1`, usando apenas `scripts/local-validate-release.ps1`.
