# CI — Checkpoints independentes para contratos e testes unitários

**Decisão (12/10/2026):** dividir o antigo job `unit`, que misturava ~50 validações contratuais rápidas com compilação, navegador, testes unitários e geração de SBOM, em duas etapas independentes e paralelas. Não remover controles nem tentar herdar aprovação de outro commit.

## Diagnóstico mensurado

Na execução de referência `38206621483`, `unit` levou ~2,78 minutos: ~33 segundos até iniciar a compilação, build ~35 segundos, Chromium ~23 segundos, testes unitários ~23 segundos, aceitação HTTP ~13 segundos, browser ~15 segundos, auditoria de vulnerabilidades ~13 segundos. Se um teste de contrato que rodava antes da compilação falhasse, um reparo da falha de infraestrutura com `rerun-failed-jobs` fazia o job inteiro ser repetido. A alteração isola checkpoints mas **não substitui** revalidação de nova HEAD após correção de código.

## Novo grafo

- `unit-contracts`: mantém todos os guardas pré-build do antigo `unit`: Compose, HML, source sanity, source pinning, segurança, matriz de autorização, governança, DDL destrutivo, compatibilidade, verificações C3 de trabalhadores, esquema, arquitetura, JSON Schema, filtros de dependência, testes de scripts, etc. Nenhum acesso a DB/volume real.
- `unit-runtime`: mantém o build Release completo, aceitação HTTP/Chromium, equivalência do corpus, NUnit/coverlet + baseline de cobertura, testes OpenAPI de 19 operações, auditoria de vulnerabilidades, SBOM CycloneDX e **as três evidências originais sem skips**.
- `unit`: nome do check obrigatório preservado. Depende de `unit-contracts` **e** `unit-runtime` e só passa se ambos forem `success`; valida existência e integridade mínima de TRX, resumos, cobertura Cobertura XML, evidência OpenAPI e SBOM baixados do **mesmo run**. Com falha de qualquer filho, `always()` evita ocultação por `skipped`.

Os dois produtores continuam com `needs: [impact,dependency-lock]`, cache NuGet por conjunto completo de locks, `dotnet restore Jornada.sln --locked-mode`, toolchain .NET 10.0.112 e token apenas leitura. O teste `test-ci-locked-nuget-cache-contract.py` é ajustado à contagem real: 12 setups .NET no workflow principal (13 incluindo DT10 reutilizável). O gate obrigatório `unit` permanece com o mesmo nome para as regras de release e branch protection.

## Expectativa e limites

Esse desdobramento permite que uma falha infraestrutural em `unit-contracts` seja reexecutada sem recriar evidências de cobertura, Chromium e build do `unit-runtime`, e vice-versa, sempre que a política de `rerun-failed-jobs` admitir recuperação. O tempo de parede pode cair discretamente por paralelização; o total de runner-minutos **pode aumentar** em razão do segundo runner, checkout e restore. Não declarar melhoria de performance até comparação dos runs na HEAD exata da PR e master.

**Aceitação:** observar ambos os jobs e o agregador `unit` verdes, evidências completas, `integration-sql`, `security-analysis`, `deterministic-build`, `e2e` paralelo, DT10 quando aplicável, e nenhuma nova falha. Os testes de contrato no `impact` devem impedir a remoção de gates e verificar os três downloads exigidos pelo agregador.

**Isolamento:** só runners GitHub e massas sintéticas descartáveis; jamais tocar `JornadaLocal`, IBGE original, NAS, NODE, HML/PROD, bancos nem volumes do usuário.
