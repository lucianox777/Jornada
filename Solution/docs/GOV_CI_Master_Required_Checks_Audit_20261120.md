# GOV-CI-MASTER-CHECKS-AUDIT-20261120 — implementação aprovada de diagnóstico P0-A

## Contexto
A issue #1734 exige comprovar que a proteção da branch master impede integração sem checks. CI verde e jobs definidos no YAML não demonstram branch protection/rulesets. O conector disponível não expõe leitura de configuração de proteção.

## Proposta técnica
Adicionar auditoria **somente leitura** usando API GitHub autenticada, com `GITHUB_TOKEN`, para inspecionar `/branches/master/protection` e `/rulesets`. O verificador verifica os três contextos diretamente exigidos nas evidências do PO: `unit-runtime`, `security-guards` e `deterministic-build`, sem exigir `strict=true` (a opção up-to-date está desmarcada). `classify-impact` é dependência indireta fail-closed do workflow, não um quarto required check direto. Configuração parcial, ausência de token, erro HTTP ou ruleset de escopo não comprovado permanecem **UNVERIFIED**, jamais PASS. Os nomes e a política de estrita atualização precisam ser confrontados com a configuração real antes de adoção como gate.

## Limites e precedência
O script não altera branch protection, não concede permissões, não modifica workflows, não publica modelo nem executa TRAIN/TEST. Um resultado UNVERIFIED não prova que a master está desprotegida. Rulesets ativos exigem auditoria de condições de branch e regras completas; a listagem isolada não é evidência suficiente. Precedência: issue #1734, GOV-DOC-01, preservação dos gates e dos LOCKs científicos.

## Validação e decisão
Testes unitários offline cobrem ausência, configuração parcial, união de contextos, política não estrita e ruleset sem escopo. **Decisão do PO (20/11/2026): aprovada a integração da PR #1764 à master**, mantendo a auditoria autenticada dos rulesets como pendência da P0-A. Execução autenticada e validação dos contextos reais continuam pendentes; aprovação de implementação não equivale à certificação da proteção.

## Revisão técnica de implementação
A API de proteção pode expor os contextos exigidos em `required_status_checks.contexts` (legado) e `required_status_checks.checks` (vinculados a apps). A auditoria considera a **união** dessas duas listas, evitando falso negativo quando ambas estão preenchidas parcialmente. Testes cobrem união completa e ausência de um contexto. Essa correção não muda o requisito de comprovação do escopo da proteção da master nem autoriza merge.

## Vinculação à CI obrigatória
A suíte `test_audit_master_required_checks.py` foi explicitamente adicionada ao job `classify-impact` do workflow `jornada-pr-required-fast`, inclusive para PRs docs-only. A mudança evita que os novos testes fiquem apenas no repositório sem execução pelos gates requeridos. A execução autenticada de auditoria continua separada e pendente; nenhum token é exposto no teste offline.

## Correção de aderência às evidências do PO — 20/11/2026 (ajuste técnico anterior à aprovação)
A primeira versão exigia indevidamente quatro checks diretos e `strict=true`. A implementação e os testes foram ajustados à configuração já documentada pelo PO. O campo `verified` **somente** demonstra que os três contextos aparecem em proteção clássica consultável; **não** comprova exigência de PR, regras de ruleset, ausência de bypass, nem proteção integral da master. Quando a proteção existe exclusivamente em ruleset, a listagem de `/rulesets` continua insuficiente: resultado `UNVERIFIED` significa falta de comprovação via este mecanismo, não desproteção. A evidência visual existente continua válida e não requer novas capturas. Correção técnica na PR #1756, ainda draft; sem modificação de governança aprovada, CI de integração ou LOCK.

## Decisão vigente GOV-CI-MASTER-CHECKS-AUDIT-20261120 — aprovação do PO
**Contexto:** P0-A #1734, conflito da PR #1756 após #1760/#1763 e necessidade de auditoria reproduzível da proteção master.
**Deliberação:** o PO autorizou explicitamente o merge da PR #1764 após CI verde, mantendo pendente a auditoria autenticada de rulesets e da proteção real.
**Justificativa:** os quatro jobs do workflow `jornada-pr-required-fast` (run 44687873787, HEAD `7810226fb84971c8e129a7aa4aede9058ebf74b5`) passaram; o diagnóstico somente leitura falha fechado quando evidência é insuficiente.
**Impacto:** introdução de script e testes offline; nenhuma alteração de proteção, ruleset, LOCK, corpus, TRAIN, TEST ou produção. `verified` cobre exclusivamente os checks de proteção clássica; não certifica regras completas de master.
**Precedência e rastreabilidade:** GOV-DOC-01; issue #1734; PR #1756 substituída pela #1764; PRs #1760/#1763 preservadas. P0-A permanece aberta até auditoria autenticada e comprovação integral; P0-B e ciência V9 continuam bloqueadas pelos respectivos gates.
