# GOV-CI-DOCS-ONLY-FAIL-CLOSED-20261120 — proposta técnica

## Contexto e causa
Durante a revisão P0-A da issue #1734, a classificação `Solution/scripts/ci-impact-classifier.py` dispensava o CI completo de **qualquer** arquivo dentro de `Solution/docs/`, independentemente da extensão. Assim, um script `.py`, `.sh`, configuração `.json`, migração `.sql` ou workflow `.yml` nesse diretório seria erroneamente tratado como documentação. Trata-se de um risco de seleção de gates, não de evidência de exploração ou bypass já ocorrido.

## Correção proposta e justificativa
Reconhecer como documentação dispensada apenas arquivos com extensão `.md` ou `.txt`, em qualquer diretório, mantendo a exclusão explícita dos manifestos E2E baseline e o comportamento conservador para arquivos desconhecidos. Adicionar regressões para executáveis/configurações dentro de `Solution/docs/` e para Markdown/texto genuínos. Mudanças de código continuam exigindo `run_full=true`.

## Impactos e precedência
Precedência: diretriz P0-A da issue #1734, GOV-DOC-01 e política vigente de CI fail-closed. Não altera workflows, checks requeridos na proteção de branch, referências científicas, LOCKs, TRAIN/TEST, modelo ou política de publicação. O patch é preventivo; não resolve por si a verificação remota de branch protection/rulesets.

## Estado e rastreabilidade
Proposta em PR separada, dependente de CI e de autorização explícita do PO para merge. O índice de decisões registra a proposta sem atribuir aprovação ao PO.
