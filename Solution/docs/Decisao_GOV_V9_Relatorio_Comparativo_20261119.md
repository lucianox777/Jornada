# GOV-V9-RELATORIO-COMPARATIVO-20261119 — relatório autônomo por campanha

**Estado:** aprovada pelo Product Owner; implementação proposta em PR, sujeita à CI e integração. **Política:** GOV-DOC-01.

## Contexto e justificativa
Campanhas de 10K/100K executam 324 hipóteses, são demoradas e seus resultados estão distribuídos entre artefatos de TRAIN, AUDIT, VALIDATION, TEST e E2E. O PO aprovou relatório comparativo automático para permitir consulta independente de análise manual do ChatGPT.

## Decisão
O encerramento E2E gera, em todas as campanhas (inclusive falhas e ausência de vencedor), artefato separado com `campaign-report.md` e `campaign-report.json`: SHA, execução, população solicitada, estados de cada etapa, número de finalistas quando comprovado, SHA do vencedor quando comprovado, situação de recibos, nomes de artefatos científicos e indicação de que produção exige autorização exclusiva do operador master.

O relatório é **observacional**: não muda TRAIN, AUDIT, VALIDATION, TEST, gates científicos nem status de sucesso/falha. TEST não elimina vencedor. Dados ausentes são marcados como indisponíveis, nunca estimados. Não copiar corpus, dados pessoais ou segredos para o relatório. Métricas detalhadas por hipótese permanecem nos artefatos científicos de origem; esta primeira versão consolida estados e rastreabilidade, não recria uma tabela integral de 324 métricas.

## Impactos, precedência e rastreabilidade
Implementação em `Solution/scripts/v9-campaign-comparative-report.py` e `.github/workflows/v9-standard-linear-100k.yml`. Artefato `v9-campaign-comparative-report-<run>-<attempt>`, retenção de 30 dias. Preserva decisões GOV-V9-TEST-INDEPENDENTE-RELATORIO-20261119 e GOV-V9-TEST-APTIDAO-PRODUCAO-20261119; não substitui os recibos oficiais. A execução já iniciada usa SHA antigo e **não** recebe retroativamente o novo relatório. CI e integração ainda pendentes.
