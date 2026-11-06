# V9 — isolamento de conflitos TF0 V3: razões do Resolve e pesos das 81 células (06/11/2026)

## Escopo e provenance

A [PR #1296](https://github.com/lucianox777/Jornada/pull/1296) foi integrada no SHA `2c8b2e053293953db148e0c6318cb55a430f9af4`, e a [CI pós-merge #42253637178](https://github.com/lucianox777/Jornada/actions/runs/42253637178) terminou **SUCCESS**.

A [corrida física #42250957008](https://github.com/lucianox777/Jornada/actions/runs/42250957008), artifact `13519808130`, utilizou **o mesmo TRAIN_SELECTION seed42 de 30 mil identidades-base sintéticas**, o mesmo split, blocking de 3 passes e prior calculado fisicamente do mesmo conjunto de pares. O result SHA source `400d4355257268fa67729566f65425bec8ed70a9`, SHA de pares SQL `b10f77f9bad0156448e28fb521cd28a79a7de7034f6ca113ec8d99e2690d05bc` e relatório SHA256 `6833ddaf4ea4279a533407c67711122f505d3ca4abea81837e3574912ae07142` permanecem imutáveis.

| Mesmo frame, 3 passes | TF0 V2 | TF0 V3 | ONE_MF V2 | ONE_MF V3 |
|---|---:|---:|---:|---:|
| TP | **6.758** | **6.306** | 6.702 | 6.771 |
| FP | 0 | 0 | 0 | 0 |
| Conflitos | **151** | **610** | 207 | 145 |
| Não resolvidos | 2.331 | 2.324 | 2.331 | 2.324 |
| Recall | 96,3914% | 89,9444% | 95,5926% | 96,5768% |

A perda de 452 TP na V3 TF0 com +459 conflitos é inequívoca **nesse conjunto técnico**, mas a causa dentro do motor ainda exige um diagnóstico por tipo de decisão.

## Revisão do mecanismo decisório (código de produção, sem alteração)

Em `ProbabilisticLinkagePolicy.cs`, `ResolveRankedCore` faz, nesta ordem:

1. Se não houver candidatos, `NAO_RESOLVIDO`.
2. Se o maior posterior for inferior ao threshold, `ABAIXO_T_LINKAGE`.
3. Se a guarda demográfica for habilitada, pode gerar `NUCLEO_DEMOGRAFICO_EXATO_NAO_UNICO`.
4. Se a guarda de segundo candidato for habilitada e ultrapassar o limite, gera `DOIS_CANDIDATOS_ACIMA_T_LINKAGE` ou `SEGUNDO_CANDIDATO_ACIMA_PISO_CONFLITO`.
5. Se a **diferença de log-odds**, NÃO a diferença de probabilidades posteriores, for menor do que a margem 0,5, gera `MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE`.
6. Caso contrário, `RESOLVIDO`.

Isso é crucial: não atribuir automaticamente os 459 novos conflitos à margem nem à saturação do posterior. O campo `ReasonsByResolveStatus` já é registrado por `V9SourceResolveAccumulator`, mas não estava exposto no resumo estatístico das oito avaliações.

**Mudança implementada nesta frente:** acrescentar ao relatório `mu-statistical-analysis.json` somente as contagens agregadas `resolveReasonCounts` e `allSelectionObservations`; nenhuma alteração no ranking, score, limiar, FDR ou decisão. O auditor `v9-mu-tf0-controlled-audit.py` agora reconcilia, por `RequestSha256`, cada contagem com TP, FP, conflitos e pendências, produzindo deltas V3−V2 separados por motivo. Dados individuais, nomes, UUIDs, scores e identidade-base não são exportados.

## Ablação das 81 células M/U — inspeção preliminar da evidência existente

Reconstrução independente a partir de `observedSupportAudit.Cells` e `estimatorJointSupport.Cells` do artifact físico **#13519808130**. Usa o mesmo estimador M/U publicado na `V9JointSupportEstimation` e identifica alterações de `ln(m/u)`, **não alteração da probabilidade posterior individual nem atribuição de conflitos**.

| Característica das 81 células | 3 passes | 19 passes (reprovado por fanout) |
|---|---:|---:|
| Estados M: core / cauda suportada / fallback | 15 / 8 / 58 | 17 / 10 / 54 |
| Estados U: core / cauda suportada / fallback | 8 / 6 / 67 | 17 / 23 / 41 |
| Células com alteração de log-odds V3−V2 | **75** | **66** |
| FallbackScaleM | 0,841744 | 1,002548 |
| FallbackScaleU | **0,092341** | 0,710099 |

Exemplos do frame de 3 passes, `(estadoPessoa, estadoMãe)` usando os enums ordinais do código:

| Célula | TRAIN M/U | Δ log-odds V3−V2 (aprox.) | Interpretação limitada |
|---|---|---:|---|
| (1,1) | 9.220 / 0 | **+1,905** | U de uma classe importante usa fallback mesmo sem observações U |
| (1,4) | 1.567 / 0 | **+3,969** | Evidência relativa M/U cresce |
| (1,2) | 207 / 0 | **+5,815** | Evidência relativa M/U cresce bastante |
| (7,1) | 135 / 0 | **−3,674** | Evidência relativa M/U diminui, apesar do mesmo suporte U=0 |

Esses deslocamentos têm sinais opostos. É plausível que reduzam margens ou façam candidatos errados ultrapassarem um limiar, mas **ainda não está comprovado que esses estados aparecem nas decisões perdidas**, tampouco a contribuição de cada célula por candidato. O teste físico da nova versão separará conflitos por **motivo**; só depois justificará um eventual replay específico com scores agregados.

## Gates, sem contornar governança

A frente atual não altera o estimador V2 nem V3, TF, política de sobrenome, FDR declarado em `plan.json` (1%), ou autorização DT-15. Executa novamente o mesmo ensaio técnico conhecido usando o executor C# existente (8 configurações) e exige:

- mesmo SHA das requisições de pares SQL por frame e mesmo prior; mesmo conjunto de identidades-base nas comparações;
- somatória de `resolveReasonCounts` exatamente igual a `allSelectionObservations`; `RESOLVIDO=TP+FP`; motivos de `CONFLITO` iguais a conflitos, restantes iguais a pendências;
- verificação dos 81 estados, contagens, massa total e normalização das probabilidades reconstituídas de M e U;
- sentinelas TF0 V2=6758, TF0 V3=6306 e ONE_MF V3=6771 em 3 passes;
- hashes de `plan`, `mu-statistical-analysis`, `same-frame-audit`, `tf0-cell-ablation` e corpus fixados no recibo; artefato obrigatório;
- compilação C#, testes offline, CI geral, nenhuma abertura de TEST/Gold/holdout independente.

**Status científico:** regressão técnica confirmada; mecanismo específico ainda em investigação; inferência de FDR, validação externa, novo bootstrap e ativação permanecem bloqueados.
