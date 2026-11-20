# GOV-CI-PRESERVAR-MASTER-ENCERRAR-PR1759-20261120

**Estado:** APROVADA PELO PRODUCT OWNER em 20/11/2026.  
**Natureza:** decisão operacional e de governança de CI, GOV-DOC-01.  
**Referências:** issue #1734 (P0-A), PR #1759, índice de decisões vigentes.

## Contexto
A PR #1759 foi aberta para acrescentar prova read-only de execução pós-merge, monitor horário e testes. Na última inspeção, permanecia em draft, não integrada, com `mergeable=false` e sem comprovação de CI no HEAD. O PO informou que os workflows funcionavam anteriormente e questionou a necessidade de introduzir o monitor.

## Decisão aprovada
1. **Não integrar a PR #1759; encerrá-la sem merge**, mantendo seu histórico disponível para auditoria.
2. **Preservar os workflows atuais da `master` sem restaurar versões históricas indiscriminadamente**, sem modificar proteções, checks existentes ou referências científicas.
3. Investigar a causa original das falhas de CI e identificar o último estado comprovadamente funcional, com evidência de runs/jobs/logs e SHAs; correções mínimas, se necessárias, somente por PR específica.
4. Manter a issue #1734 aberta até comprovar seus critérios de aceite. Esta decisão não comprova `master` verde, validação DT-10 ou autoriza TRAIN/TEST V9.

## Justificativa e alternativas
O monitor da PR #1759 aumentaria a superfície de manutenção e de gates sem evidência de que resolve a falha original. Reverter workflows da `master` para versões antigas foi **rejeitado por ora**, pois poderia remover checks e decisões posteriores. A opção aprovada é manter a configuração integrada e investigar sua execução real.

## Impactos, precedência e limites
- Nenhuma alteração de arquivos de workflow na `master` por esta decisão; preservam-se checks, regras e LOCKs existentes.
- As propostas GOV-CI-POSTMERGE-PROOF-20261120 e GOV-CI-MONITOR-POSTMERGE-20261120 não serão integradas por meio da PR #1759; **não se tornam decisões aprovadas**.
- Prevalece sobre a proposta de integrar a PR #1759; não revoga GOV-CI-MASTER-CHECKS-AUDIT-20261120, GOV-CI-RULESET-AUDITOR-DEFER-20261120 nem contratos científicos.
- P0-A #1734 permanece em investigação. Nenhum merge, rebase, rerun ou execução científica por efeito desta decisão.

## Rastreabilidade
- Aprovação explícita do PO na conversa do projeto em 20/11/2026.
- PR #1759: confirmar `state=closed` e `merged=false` após encerramento.
- Índice oficial: `Solution/docs/Indice_Decisoes_Vigentes.md`.
- A documentação integrada é o registro normativo; o histórico da conversa não o substitui.
