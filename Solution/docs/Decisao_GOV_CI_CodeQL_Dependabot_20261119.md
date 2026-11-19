# GOV-CI-CODEQL-DEPENDABOT-20261119 — Reconciliação de SHA CodeQL em PR de dependências

## Contexto e evidência

A PR #1450 (Dependabot, 77 arquivos) altera os pins do CodeQL no `.github/workflows/ci.yml` de `2892aa5e19bbd11bc0cff5427e3b750a04d9e3c2` (v4.38.2) para `33d7b85da1a04171e211befe2c739303b9a4c066` (v4.39.3), mantendo `init` e `analyze`. A execução #44595348811 fez checkout de `refs/remotes/pull/1450/merge` e falhou em `security-guards` e `unit-runtime`, no teste `test_security_parallel`, porque `Solution/tests/scripts/test_ci_required_gates.py` ainda exigia o SHA antigo. `classify-impact` e `deterministic-build` passaram.

## Encaminhamento técnico e justificativa

Na própria branch da PR #1450, alinhar exclusivamente as duas referências literais CodeQL do teste ao SHA presente no workflow proposto. Preservar o teste fail-closed, os pins de 40 caracteres, os dois passos CodeQL, `security-extended`, build manual, SARIF e agregação obrigatória. **Esta reconciliação não valida, por si só, a autenticidade externa do novo pin nem aprova a PR inteira.** Antes do merge, verificar a procedência do SHA e todos os demais contratos afetados pelas 77 alterações, com CI obrigatória verde.

## Impactos, precedência e limites

Correção limitada ao contrato estático da CI; não altera código de produção, TF, gerador, dados ou manifestos LOCKED. Mantém precedência de GOV-DOC-01 e P0 #1734: a integridade da `master` e a comprovação de CI pós-merge continuam pendentes. Não autoriza campanha científica, promoção ou relaxamento de gates.

## Rastreabilidade

- PR: #1450; execução: #44595348811; revisão problemática: `434698b` (`pull/1450/merge`).
- Arquivos: `.github/workflows/ci.yml` e `Solution/tests/scripts/test_ci_required_gates.py`.
- Estado: ajuste submetido à PR; CI da revisão corrigida e validação externa dos pins pendentes.
