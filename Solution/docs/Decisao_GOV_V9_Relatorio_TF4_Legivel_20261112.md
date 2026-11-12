# GOV-V9-TF4-RELATORIO-LEGIVEL-20261112 — apresentação legível dos finalistas

**Autoridade:** Product Owner, 12/11/2026. **Status:** REVOGADA pelo Product Owner em 12/11/2026; implementação da PR #1449 revertida por solicitação posterior.

## Contexto e decisão

Os relatórios de calibração 100K TF4 apresentavam identificadores compactos (por exemplo, `PF10_PL5_MF10_ML10`) sem explicar os quatro pesos. O PO aprovou exibir, para cada finalista congelado, os nomes completos e os valores: **prenome da pessoa (PF)**, **sobrenome da pessoa (PL)**, **prenome da mãe (MF)** e **sobrenome da mãe (ML)**. A escala do identificador é 0 → 0,0; 5 → 0,5; 10 → 1,0. Esses valores são pesos do ajuste por frequência, não passes de blocking nem probabilidades de acerto. Peso zero desliga o ajuste TF daquele campo, não necessariamente o uso do campo no linkage.

A identificação `OptionId` e o SHA do snapshot devem ser preservados para rastreabilidade, juntamente com métricas e os três passes fixos de blocking, em seção distinta. A associação entre finalista e parâmetros deve ser feita pelo SHA do snapshot medido, nunca pela posição na lista.

## Justificativa, impactos e precedência

A apresentação legível permite auditoria e comparação pelos leitores não especializados sem alterar os cálculos científicos, a seleção de finalistas, a política M/U, os passes ou o resultado da VALIDATION. A saída JSON acrescenta `optionId`, `tf4Fields` e métricas em `finalistDetails`; a saída Markdown lista os quatro campos em português. A regra prevalece sobre relatórios que exibam apenas siglas. Relatórios históricos não são reescritos retroativamente; novos relatórios são gerados pelo código atualizado.

## Rastreabilidade

Execução TRAIN histórica #43105369099; decisão GOV-V9-PERSISTENCIA-SNAPSHOTS-20261112; implementação `Solution/scripts/v9-v3-four-name-100k-audit.py`; PR #1449; índice `Solution/docs/Indice_Decisoes_Vigentes.md`. A execução histórica concluiu TRAIN, mas não VALIDATION independente, TEST, certificação ou ativação.

## Retificação posterior do PO — 12/11/2026 (prevalece)

O PO esclareceu que **não deseja a tradução por extenso** no relatório: manter os identificadores compactos originais `PF10`, `PF5`, `PL5`, `MF10`, `ML10`, etc., como `TF4_PF10_PL5_MF10_ML10`, **sem tabela adicional de pesos**. Esta instrução substitui a exigência de nomes completos acima. O relatório preserva `OptionId`, SHA, métricas e os passes de blocking em seção distinta. Implementação retificada no mesmo script, antes do merge da PR #1449. Justificativa: preferência explícita do PO pela notação técnica já conhecida, sem redundância visual.

## Revogação pelo PO — 12/11/2026 (decisão vigente)

O PO solicitou desfazer o relatório detalhado dos finalistas. O script de auditoria retorna integralmente ao conteúdo anterior à PR #1449 (commit de referência `32ac1845c10bfddb2a80f0be5e91fc99950588f4`), sem `finalistDetails` enriquecido, métricas adicionais por finalista ou listagem detalhada de passes por finalista. Esta revogação prevalece sobre a aprovação e a retificação anteriores. Motivação/contexto: simplificar a saída e eliminar a alteração recente do relatório durante investigação de consumo de memória; não há prova de que esse relatório tenha causado a falha de VALIDATION. Preservam-se o restante da calibração, o ETA e a correção independente do envelope >2 GiB (PR #1452). O registro histórico permanece para rastreabilidade.
