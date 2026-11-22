# GOV-V9-TF4-PF-GRADE-MARGEM-20261122 — ajuste aprovado da grade de busca TF4

**Decisão do Product Owner (22/11/2026), issue #1865:** após avaliação técnica, implementar e integrar a alteração da grade padrão do novo estudo TF4 do Calibrador V9. Esta aprovação sucede a fase de proposta, que não autorizava implementação. Não equivale à autorização de executar campanha científica, selecionar modelo, promover, ativar ou certificar resultados.

## Contexto e justificativa
O produtor aplicável ao estudo TF4 de 100 mil observações é `Solution/scripts/v9-v3-four-name-100k-plan.py`, não o produtor histórico escalonado `v9-tf-option-grid.py`. O estudo TF4 vigente enumerava 3⁴ = 81 vetores nominais, duas variantes de mistura M/U e duas margens (0,5 e 2), totalizando 324 hipóteses. A proposta do PO prioriza resolução do peso TF do prenome da pessoa (PF) sobre a exploração da segunda margem, preservando o número de hipóteses. A superioridade científica **não está demonstrada** e exige estudo novo. Os resultados do run #33 não foram auditados diretamente nesta alteração.

## Contrato aprovado para novos planos TF4
- PF: `[0, 0.125, 0.25, 0.375, 0.5, 1]`.
- PL, MF, ML: `[0, 0.5, 1]` cada.
- Margem de conflito: `[0.5]` apenas.
- M/U V3 mistura: `[0.5, 1]`; um ruleset físico; `6 × 3³ = 162` opções TF, `162 × 2 × 1 = 324` hipóteses.
- `(0,0,0,0)` continua representado pelo **TF0 histórico**. Não duplicar controle nem excluir pesos zero das demais unidades.
- Identificadores de pesos históricos (0, 0,5 e 1) são preservados; PF fracionário novo usa codificação `Q125/Q250/Q375` para evitar colisões. O identificador de estudo/fingerprint é novo; não reescrever artefatos já congelados.

## Implementação, impactos e limites
Alterar produtor TF4 e testes de grade/contagem; alinhar auditoria e handoffs de novos estudos à cardinalidade 162, preservando o orçamento 324 e falha fechada. O produtor histórico escalonado não é objeto desta decisão. Contratos de planos já emitidos, snapshots, SHA e replays históricos permanecem imutáveis; auditoria histórica deve usar a versão pinada do respectivo estudo. Não alterar scorer C#, FS, TF/M/U, thresholds, blocking, limites FDR/P95, partições, TEST ou modelo ativo. Alteração da distribuição de hipóteses pode modificar custo físico, resultados e vencedor; validar com evidências TRAIN/VALIDATION e TEST independente antes de qualquer uso operacional.

## Precedência e rastreabilidade
- Implementa a issue #1865, aprovada expressamente pelo PO após análise; substitui **somente para novos estudos** a grade TF4 e margens da decisão [GOV-V9-V3-TF4-324-20261111](Decisao_GOV_V9_V3_TF4_324_100k_20261111.md).
- Preserva o protocolo de 324 hipóteses, governança de execução e gates científicos; não revoga campanhas históricas.
- Índice vigente: `Indice_Decisoes_Vigentes.md`. Evidência de testes e CI pertence à PR de implementação, não a esta decisão isoladamente.
