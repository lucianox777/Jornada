# GOV-CI-DOCS-ONLY-FAST-20261119 — dispensa de build e testes unitários de aplicação para PR documental

**Autoridade:** Product Owner, aprovação expressa em 19/11/2026. **Estado:** aprovado; implementação e CI nesta PR, integração pendente. **Política:** GOV-DOC-01.

## Contexto e justificativa
A decisão GOV-OPS-CORRECAO-SQUASH-CI-MASTER-20261111 já permite dispensar a CI completa para mudanças classificadas como `docs-only`, mas preservava os três jobs rápidos de PR. A PR documental #1744 ficou na fila para compilar .NET e executar testes de aplicação, sem alteração de código. O PO aprovou explicitamente estender a dispensa para esses trabalhos desnecessários.

## Decisão e limites
Para PRs comprovadamente `docs-only` pelo classificador conservador existente, dispensar as etapas pesadas de `unit-runtime` e `deterministic-build` (testes de aplicação e compilação .NET), preservando os jobs e seus nomes para as verificações obrigatórias. Manter classificação verificável, testes do próprio classificador, `security-guards`, integridade documental e proteção de branch. Mudanças mistas, desconhecidas ou alterações de workflows/código executam os gates pesados normalmente. `push` na master e dispatch preservam os checks rápidos completos; `jornada-ci` e o classificador físico 30k mantêm seus contratos próprios. Etapas de compilação e testes condicionais `skipped` não são evidência de testes de aplicação aprovados; são dispensa autorizada. Os jobs com nomes exigidos pelo ruleset permanecem presentes e devem concluir com sucesso após a classificação; seu sucesso em PR documental comprova somente a aplicação correta da dispensa, não a execução dos testes pesados. Não contornar proteção de branch nem permitir merge com classificador falho.

## Implementação, evidências e precedência
Workflow `.github/workflows/jornada-pr-required-fast.yml`: job `classify-impact` calcula `docs_only` usando diff base→HEAD da PR, com checkout de histórico; jobs `unit-runtime` e `deterministic-build` permanecem presentes, mas suas etapas pesadas executam somente quando não há classificação `docs-only`; `security-guards` continua após classificação bem-sucedida. Regra precede apenas a obrigação de executar build e unit tests em PRs documentais da GOV-OPS-CORRECAO-SQUASH-CI-MASTER-20261111; preserva o restante dessa decisão, GOV-DOC-01, LOCKs e gates científicos. A PR #1744 foi criada antes da implementação e sua execução anterior não é retroativamente dispensada.

**Aceite pendente:** regressões do classificador e workflow no HEAD da PR, verificação de `docs-only` real e de mudança substantiva, revisão de rulesets para garantir que os jobs obrigatórios continuem presentes e aprovados, sem confundir etapas dispensadas com testes executados. Não declarar integrado antes do squash.
