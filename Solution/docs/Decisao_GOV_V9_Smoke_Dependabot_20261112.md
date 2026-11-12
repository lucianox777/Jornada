# GOV-V9-SMOKE-DEPENDABOT-20261112 — Desativar execução do smoke ABCD em PRs Dependabot

**Aprovação:** Product Owner, 12/11/2026. **Estado:** implementação em PR, pendente de integração.

## Contexto e justificativa
A execução automática #43201910830 de `v9-abcd-fast-smoke`, originada por PR de atualização de dependências do Dependabot, terminou com sucesso, mas não é uma calibração V9 e consome verificações não solicitadas. O PO aprovou desativar permanentemente esse teste para PRs do Dependabot, preservando testes de PRs humanas e execução manual.

## Decisão
Adicionar condição ao job `paired-blocking` do workflow `.github/workflows/v9-abcd-fast-smoke.yml` para não executar em `pull_request` cujo autor seja `dependabot[bot]`. Preservar o gatilho `workflow_dispatch` e o comportamento para PRs dos demais autores. A verificação do workflow ainda poderá aparecer como **skipped** no GitHub; não serão executados passos nem alocado runner para esse job. Não desativar o Dependabot, a CI de outras alterações ou o calibrador de 324 hipóteses.

## Impactos, precedência e rastreabilidade
Esta regra tem precedência apenas sobre a execução automática desse job para PRs Dependabot. Preserva GOV-DOC-01 e as regras de validação científica V9; não altera ciência, SQL ou dados. Motivação rastreável: run https://github.com/lucianox777/Jornada/actions/runs/43201910830. Reversão: remover condição `if` no job após nova decisão do PO.
