# GOV-PR-ENCERRAMENTO-20261119 — Higiene de PRs sem exclusão de branches

**Decisão do Product Owner, 19/11/2026.** Na organização do repositório Jornada, priorizar **encerrar PRs obsoletas ou substituídas**, sem excluir branches por rotina e sem abrir novas PRs apenas para organização. Preservar histórico e rastreabilidade. PRs já integradas estão encerradas pelo GitHub; PRs ativas e necessárias permanecem abertas. Uma branch isolada não possui estado «fechada»: somente a PR pode ser fechada, enquanto a branch é preservada.

## Contexto e justificativa

A correção SCALE da PR #1738 ficou com conflito após o merge da PR #1739. Foi aberta a PR #1740 sobre a master atual, e a PR #1738 foi **fechada sem merge**, com comentário de substituição (comentário GitHub #7016069676). O PO esclareceu que a finalidade é encerrar trabalhos duplicados, não apagar branches nem multiplicar PRs.

## Procedimento vigente

1. Identificar PR aberta redundante e sua sucessora, verificando se a sucessora cobre o escopo e se não há alterações exclusivas não preservadas.
2. Fechar **somente** a PR redundante, com comentário de motivo e referência à sucessora; manter a branch.
3. Não fechar PR com trabalho ainda necessário, revisão pendente relevante ou implementação não incorporada sem análise.
4. Preferir continuar a PR existente; abrir substituta apenas diante de justificativa técnica comprovada (ex.: conflito inviabilizando integração segura).
5. Registrar as decisões e resultados nos documentos oficiais e manter o índice sincronizado (GOV-DOC-01).

## Limites, precedência e impactos

Esta decisão disciplina higiene operacional, não modifica critérios científicos, CI, TRAIN/TEST, proteções da master ou histórico de commits. Complementa GOV-DOC-01 e não autoriza fechamento indiscriminado de PRs. Exemplo auditável: PR #1738 (encerrada, sem merge), PR #1740 (sucessora ativa), PR #1739 (integrada). Branches preservadas para consulta. A verificação geral de PRs antigas depende de evidência individual de redundância.
