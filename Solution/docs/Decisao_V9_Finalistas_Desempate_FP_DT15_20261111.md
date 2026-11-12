# GOV-V9-FINALISTAS-FP-DT15-20261111 — decisão do Product Owner

**Estado:** APROVADA em 11/11/2026; implementação e testes dependem de CI; não equivale a certificação científica nem a ativação.

## Contexto e justificativa
O custo operacional de desfazer vínculos de identidade incorretos é elevado. A fronteira Pareto V9 já considera recall e FDR, mas não utiliza o número absoluto de falsos positivos (FP) como dimensão independente. O PO decidiu introduzir preferência por FP **somente depois** da seleção dos finalistas existentes, sem transformar o Pareto em três dimensões.

## Regra aprovada
1. Manter inalterados o Pareto 2D recall × FDR, o procedimento existente de seleção e o limite de **até três finalistas**. Não buscar nem reservar uma vaga para o menor FP de toda a fronteira.
2. Entre os finalistas congelados que satisfizerem os gates científicos e operacionais aplicáveis, preferir **menor FP absoluto observado na mesma coorte de validação**.
3. Em empate de FP, preferir maior recall; depois menor limite superior condicional de FDR; finalmente SHA em ordem ordinal, para reprodutibilidade.
4. Manter P95 como gate operacional, sem usá-lo na ordenação científica. Preservar multiplicidade, holdout, TEST, ausência de refit e ausência de promoção automática.
5. Apresentar na página/dossiê DT-15 **até três finalistas**, com TP, FP, recall, FDR, P95, elegibilidade, evidências, preferência calculada e comparação com ATIVO quando válida. Distinguir claramente **finalista**, **candidato preferencial condicional** e **modelo ATIVO**. Não afirmar que a preferência equivale a certificação ou autorização de ativação.

## Precedência e impacto
Esta decisão modifica **apenas a ordenação final** descrita em `V9_Selecao_Interna_TRAIN_Finalistas_20261019.md` (anteriormente maior recall dentre os elegíveis). Não altera a formação dos finalistas nem os gates estatísticos. Complementa `V9_Pareto_2D_Integracao_20261019.md` e `DT15_Governanca_Decisao_Modelo.md`, preservando a decisão humana de promoção.

## Rastreabilidade e limites
Evidência motivadora: campanha V9 100K/T4, run #43105369099, que apresentou alternativas com 7, 4 e 2 FP em TRAIN. Esses valores **não** são validação externa, certificação de risco real nem autorização de TEST ou ativação. A recomendação condicional somente existe após avaliação independente e gates aplicáveis. Implementação V9: `V9NestedCalibrationCli.SelectWinner`; apresentação DT-15: integração pendente de verificação de contratos e fontes de dados da página.

## Deliberação posterior do PO — prioridade de entrega do calibrador (11/11/2026)

O PO autorizou concluir e integrar por **squash merge** primeiro a alteração executável do calibrador V9, para permitir a campanha noturna de **324 hipóteses**, sem aguardar a apresentação visual de finalistas na DT-15. O componente de interface e o vínculo histórico DT-15 continuam requisitos aprovados, mas seguem em PR separada; a ausência de UI não altera os gates, nem implica ativação. A ampliação de pesos lambda foi explicitamente excluída desta entrega. **Após o fechamento e verificação da campanha de 324 hipóteses, congelar** plano, SHA de build/código, referências/corpus, resultados originais e seus SHA-256, métricas, finalistas, logs e evidências; o congelamento não é automático nem equivale a certificação ou ativação. Esta emenda altera a sequência de implementação, não a regra de preferência por FP nem a precedência científica e de governança.
