# V9 — LOCK técnico de isolamento TF0 / M-U V2 versus V3 (06/11/2026)

**Hipótese causal que queremos testar:** a queda do TF0 do baseline V2 congelado (96% em VALIDATION da seed43, diferente população e fit) para ~90% no TRAIN_SELECTION V3 pode estar ligada ao estimador M/U, ao recorte, ao prior ou à interação TF/M-U. Não atribuir causa antes de medir o contraste **no mesmo frame**.

## Contraste primário

**Amostra conhecida, apenas regressão técnica:** 30 mil identidades sintéticas clean da seed 42, corpus gerado a partir de referências IBGE fixadas, uma única divisão interna por identidade (TRAIN_ADJUSTMENT / TRAIN_SELECTION). A VALIDATION externa, TEST, Gold e seeds científicas novas **não são abertas**.

O único executável que calcula M/U, decisões e agrupamento é o **Calibrador C# V9**, chamado uma vez com um plano congelado. Desenho fatorial fixado antes do resultado:

| Fator | Valores |
|---|---|
| M/U | DEFAULT (V2), CLASSWISE_SUPPORT_BACKOFF (V3, 0,99/M5/U5/cauda true) |
| TF | TF0 canônico A0.5_T0.95_TF0; ONE_MF simétrico W1/M0 |
| Blocking | 3 passes históricos (primário); 19 passes (controle técnico, rejeitar por fanout se aplicável) |
| Alfa, threshold, margem | 0,5 / 0,95 / 0,5 |
| Teto FDR observado e superior (no plano) | 1% / 1% |
| Prior | **Calculado uma única regra sobre o mesmo conjunto físico M/U de candidatos TRAIN**, verificado igual entre todos os quatro braços do mesmo blocking |
| Total | **8 configurações físicas no mesmo SQL/corpus/split** |

Para permitir o teste, o produtor usa o ID canônico `A0.5_T0.95_TF0`. A referência antiga `T0.95_TF0` descreve conceitualmente alfa=0,5, threshold=0,95 e peso TF=0, mas **a equivalência byte-a-byte do snapshot antigo não foi demonstrada pelo nome**. O teste exige exatamente o dicionário do gerador, sem `UnitTfOptions`, e rejeita ID antigo apresentado como nova configuração.

**Reuso obrigatório:** o script `v9-mu-tf0-controlled-plan.py` chama a transformação histórica validada `v9-mu-historical-plan.py`, sem duplicar estimadores; acrescenta apenas o TF0 ao plano e altera cardinalidade de quatro para oito.

## Evidência e gates

- Em cada bloqueio, validar **mesmo SHA de pares SQL**, M, U, denominadores e prior para V2/V3 e TF0/ONE_MF; sem truncamento de avaliações.
- Os oito braços devem medir o Resolve real, com `IdentityOutcomes` presentes; recalcular TP/FP/recall/FDR diretamente e calcular diferenças **em identidades-base emparelhadas**, inclusive ganhos e perdas.
- Sentinelas de regressão seed42 já conhecidas: ONE_MF V2 = 6.702 TP, ONE_MF V3 = 6.771 TP, TF0 V3 = 6.306 TP; todos com 7.011 fontes elegíveis (3 passes). Se não se repetirem, **falhar antes de produzir qualquer conclusão**.
- O contraste desconhecido/decisivo é **TF0 V2 vs TF0 V3**, sob esse mesmo recorte. O resultado não deve ser inventado nem forçado a 90% ou 96%.
- Registrar conflitos, pendências, FP por identidade, fanout, P95 e a grade M/U de 81 células. O controle de 19 passes não é candidato elegível se exceder o fanout P99.
- Os artefatos mantêm planos e hashes SHA-256, relatório agregador, auditoria por identidade **sem publicar nomes ou IDs brutos**, e recibo vinculado ao SHA do código e corpus.

A avaliação por identidades no mesmo TRAIN é **regressão técnica**; bootstrap por identidade não comprova IID dos **erros da resolução**, porque identidades competem por candidatos. Não conceder certificação FDR apenas porque o limite CP condicional está abaixo de 1%.

## Decisões após a medição

Se a **V2 TF0** também cair para ~90%, a anomalia entre estudos estará majoritariamente associada a diferenças de corpus/split/prior, sem inocentar completamente o método. Se V2 TF0 ficar perto de ~96%, há diferença V2/V3 grande **nesse mesmo frame**, que exige ablação das 81 células e análise de score/posterior por origem. Se ambos diferirem, comparar os deltas por identidade e investigar a interação TF x M/U.

**Qualquer resultado:** V2 histórico permanece preservado; V3 ainda não pode ser promovida a bootstrap, TEST não é aberto, nenhuma nova seed é consumida sem LOCK científico independente. As massas difíceis (homônimos, famílias, erros correlacionados e casos no limiar) são uma segunda campanha pré-registrada, **posterior** a este gate decisivo; jamais tratar 'zero FP observado' como certificação.

Este plano técnico não altera o FDR de 1%, não seleciona novo prior nem ativa DT-15.
