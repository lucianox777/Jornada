# V9 — resultado técnico M/U V2 × V3 (seed 42, 05/11/2026)

**Estado: prova física técnica concluída, NÃO confirmatória.** PR [#1287](https://github.com/lucianox777/Jornada/pull/1287) integrada; [Action #42120197234](https://github.com/lucianox777/Jornada/actions/runs/42120197234) aprovada no SHA exato `4d5718e57f33db6d506cfce929230ca6069a1602`. O pacote original é o [Artifact #13459820384](https://github.com/lucianox777/Jornada/actions/runs/42120197234/artifacts/13459820384) (retenção 90 dias); seu relatório estatístico JSON tem SHA-256 `f418874fd595ba3959080eb651becebe8fe2222ed6d4663cb8343bd0dc5eecb3`, verificado contra o recibo. Este resumo compacto está congelado em [`metrics-summary.json`](../data/calibration/v9/mu-seed42-technical-20261105/metrics-summary.json), e **não substitui** o relatório original com todas as 81 células.

## Desenho efetivamente executado

Mesmo corpus sintético **30 mil pessoas-base, seed 42 conhecida**, mesmo pin IBGE, SQL descartável e split interno de TRAIN (`TRAIN_ADJUSTMENT`/`TRAIN_SELECTION`). A opção comum é `TF_ONE_MF_W1_M0_A0.5_T0.95`, alfa 0,5, limiar 0,95 e margem 0,5. Comparações emparelhadas dentro de cada blocking: 3 passes históricos ou controle completo de 19 passes. O único fator alterado em cada par é o **método de estimação M/U**:

- `DEFAULT` = cálculo histórico V2 em 81 células;
- `CLASSWISE_SUPPORT_BACKOFF` = V3 histórica com `ClassMassCoverage=0,99`, `MinimumM=5`, `MinimumU=5`, `RetainSupportedTail=true`.

Foram **quatro avaliações medidas**, duas elegíveis e duas inelegíveis. O Calibrador retornou um finalista interno TRAIN: o snapshot V3/3 passes `5ddd4667db422a56371283725aaa874db87a17eded10c51855315438c99b62b1`. A razão de parada foi `COMPUTE_BUDGET_EXHAUSTED_NOT_OPTIMALITY`: o orçamento fechado de quatro testes foi consumido, **não** houve prova de ótimo global.

## Resultados por frame, por observação-fonte

| Blocking | M/U | TP aceitos | FP aceitos | Recall global | Conflitos | FDR observada | P95 de replay | Elegibilidade |
|---|---|---:|---:|---:|---:|---:|---:|---|
| 3 passes | DEFAULT / V2 | 6.702 | 0 | 95,5926% | 207 | 0% | 1.176,0021 ms | Elegível |
| 3 passes | CLASSWISE / V3 | 6.771 | 0 | 96,5768% | 145 | 0% | 768,8721 ms | Elegível |
| 19 passes | DEFAULT / V2 | 6.819 | 0 | 97,2614% | 164 | 0% | Não aferido | **Inelegível: fanout P99** |
| 19 passes | CLASSWISE / V3 | 6.826 | 0 | 97,3613% | 154 | 0% | Não aferido | **Inelegível: fanout P99** |

Denominador de recall nos quatro braços: **7.011 observações-fonte elegíveis antes do blocking**. No mesmo frame de 3 passes, a V3 ganhou **69 TP** e **0,9842 ponto percentual de recall**, com **62 conflitos a menos** e nenhum FP observado em ambos. No frame de 19 passes, a V3 ganhou **7 TP** (+0,0998 p.p.) e teve 10 conflitos a menos, porém **ambos os braços foram reprovados pelo fanout P99**, portanto não são candidatos de seleção. A diferença de P95 nos braços elegíveis é descritiva de uma execução técnica, não evidência causal/estatística de redução da latência.

No frame de 3 passes, a auditoria reconstruída encontrou, em ambos os estimadores, **13.997 pares M** e **13.472 pares U**, distribuídos nas 81 células: 30 com M observado, 20 com U, 13 com ambos e 44 sem nenhum. No frame 19 passes, ambos compartilham 14.314 M e 505.169 U (38/48/30/25 células por suporte M/U/ambos/nenhum). A política V3 afeta a estimativa, não os pares recuperados no mesmo blocking.

## Limites inferenciais e decisão de continuidade

1. As FDRs de 0% são **observadas**, não garantias de risco zero. Os limites Clopper–Pearson condicionais presentes no relatório **não são certificados**: `ClusterIidIndependentlyValidated=false` para os quatro braços; não converter resultados de pares dependentes em confiança independente.
2. A seed42 é conhecida, usada reiteradamente em regressão e potencialmente inspecionada na engenharia. **Não** usá-la como VALIDATION confirmatória ou TEST independente. Não confundir essas medidas de `TRAIN_SELECTION` com os antigos resultados congelados V2×V3 de outra execução.
3. A V3/3 passes foi o **finalista interno técnico**, não modelo certificado, nem escolha operacional automática. `ExternalValidationUsed=false`, `TestUsed=false`, `Activated=false`. A DT-15 permanece separada.
4. Para confirmar superioridade M/U: pré-registrar hipótese, unidade estatística por identidade-base, limite alfa e multiplicidade, plano LOCKED e coorte/seed realmente independente; executar **somente mediante solicitação manual explícita**. Não abrir novas seeds, Gold ou TEST pela CI.

**Proveniência:** SHA de bytes do plano `593ad66a287824aa48c2c3fd1057d11d2a1a8bead27934a049469e06eb191dc6`; SHA canônico do plano C# `57757fff562d55e0ee943a3b5aa4f1d3f0fb79eb54d5237385cdb707bd3e5c20`; corpus `28bbb9f4c63c97c353e7f29cdbc77091a4773b2f4c596e46103edadd93635d17`. Esses digests são distintos por contrato, e nenhum indica ativação.
