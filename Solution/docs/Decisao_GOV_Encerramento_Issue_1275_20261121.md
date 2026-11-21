# GOV — Encerramento administrativo da issue #1275 (21/11/2026)

## Decisão aprovada pelo Product Owner
Encerrar a issue #1275 como **completed**, por entrega do mandato histórico de quatro campanhas físicas manuais 30k nas seeds 44–47 e consolidação comparativa das seeds 42–47. Esta decisão encerra somente o escopo administrativo da issue; não constitui certificação científica, autorização DT-15, promoção de modelo ou liberação de TEST.

## Contexto e evidências
- Issue #1275: mandato de 04/11/2026, quatro execuções separadas e consolidação.
- PR #1276: integrada em 05/11/2026, adiciona parametrização de seeds, manifesto LOCKED e gates de proveniência.
- Runs seed 44: 41942914164; seed 45: 41942917704; seed 46: 41942921106; seed 47: 41942924301. Comentários da #1275 registram sucesso, artefatos e checks de hash.
- Relatório oficial: `Solution/docs/V9_TF_ONE_MF_Reprodutibilidade_Seeds42_47_202611.md`, com TP/FP/FN, recall, FDR, fragmentação, fusão, P95, hashes, limites e comparações por seed.
- Manifesto: `Solution/docs/V9_TF_SEEDS_44_47_LOCK.json`, SHA-256 `822648cc3cca71368b77afad1fe816ae1ee9f87583311e1b25f18e19f01bfba3`.

## Justificativa e limites
A entrega de quatro campanhas e do relatório consolidado satisfaz o objetivo de reprodutibilidade empírica da #1275. A seed 44 já estava exposta; o teste Hoeffding pré-declarado não rejeitou H0; a independência entre identidades não foi validada; o estudo não certificou FDR ≤1%, desempenho operacional, Gold, TEST ou produção. O encerramento não elimina essas limitações nem autoriza reinterpretar resultados históricos como holdout confirmatório.

## Impactos, precedência e rastreabilidade
A decisão de encerramento aprovada em 21/11/2026 prevalece sobre o estado administrativo aberto da #1275, **sem alterar** os critérios científicos vigentes. O relatório e os artefatos permanecem como evidências históricas. Auditoria coordenada pela #1828; GOV-DOC-01. Não altera código, parâmetros, LOCK ou workflows.
