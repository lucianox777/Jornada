# GOV-V9-ETA-FISICO-UNICO-20261112 — ETA único a partir da execução física

**Decisão aprovada pelo Product Owner:** 12/11/2026. **Estado:** implementação proposta em PR, sem alteração científica.

## Contexto e justificativa
A campanha V9 TF4 de 324 hipóteses exibiu ETA inicial distorcido: a média acumulada dividia todo o tempo até a primeira conclusão por apenas uma hipótese. O PO determinou medir o início onde as hipóteses efetivamente rodam e simplificar a mensagem para apenas um ETA. Referência histórica: run #43159426682, SHA `32ac1845c10bfddb2a80f0be5e91fc99950588f4`, concluiu a etapa física 324/324 e produziu finalistas; falhou na VALIDATION independente.

## Decisão e implementação
- Iniciar cronômetro monotônico na entrada do primeiro grupo físico, imediatamente antes de `EvaluateAsync`, não na preparação da busca.
- Registrar cada conclusão no callback já existente, sem modificar ordem, agrupamento, paralelismo ou conteúdo científico.
- Exibir **um único** `ETA_min`, estimado pelo intervalo médio entre conclusões na janela de até 20 eventos (vazão efetiva observada, inclusive paralelismo).
- Nas primeiras sete conclusões, exibir `ETA_min=N/A | ETA_EM_AQUECIMENTO`, sem projetar timeout a partir de amostra insuficiente.
- Após o aquecimento, comparar o ETA único com o orçamento físico de 305 minutos; preservar contagem, hash abreviado, status e duração pós-SQL.
- Remover do console o ETA acumulado, o segundo ETA, a preparação interna e a margem redundante. Nenhuma modificação em SQL, fit, replay, critérios, parâmetros, TF, TEST ou evidências científicas.

## Impactos, precedência e rastreabilidade
Esta decisão substitui **somente** a apresentação e a origem temporal do ETA físico anteriormente documentado em `Decisao_GOV_V9_Progresso_Fisico_Hipoteses_20261111.md`. Preserva GOV-DOC-01, as decisões científicas V9 e a política de não iniciar automaticamente a campanha. A nova métrica continua uma estimativa baseada em vazão, sujeita a heterogeneidade dos quadros físicos; não garante conclusão dentro do timeout. Ver PR vinculada a esta alteração e histórico de execução #43159426682.
