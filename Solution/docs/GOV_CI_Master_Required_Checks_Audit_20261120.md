# GOV-CI-MASTER-CHECKS-AUDIT-20261120 — proposta de diagnóstico P0-A

## Contexto
A issue #1734 exige comprovar que a proteção da branch master impede integração sem checks. CI verde e jobs definidos no YAML não demonstram branch protection/rulesets. O conector disponível não expõe leitura de configuração de proteção.

## Proposta técnica
Adicionar auditoria **somente leitura** usando API GitHub autenticada, com `GITHUB_TOKEN`, para inspecionar `/branches/master/protection` e `/rulesets`. O verificador exige, conservadoramente, os contextos `classify-impact`, `unit-runtime`, `security-guards` e `deterministic-build` e `strict=true` em proteção clássica. Configuração parcial, ausência de token, erro HTTP ou ruleset de escopo não comprovado permanecem **UNVERIFIED**, jamais PASS. Os nomes e a política de estrita atualização precisam ser confrontados com a configuração real antes de adoção como gate.

## Limites e precedência
O script não altera branch protection, não concede permissões, não modifica workflows, não publica modelo nem executa TRAIN/TEST. Um resultado UNVERIFIED não prova que a master está desprotegida. Rulesets ativos exigem auditoria de condições de branch e regras completas; a listagem isolada não é evidência suficiente. Precedência: issue #1734, GOV-DOC-01, preservação dos gates e dos LOCKs científicos.

## Validação e decisão
Testes unitários offline cobrem ausência, configuração parcial, estrita completa e ruleset sem escopo. Proposta em PR Draft, sem aprovação do PO. Execução autenticada e validação dos contextos reais pendentes; merge requer autorização explícita.
