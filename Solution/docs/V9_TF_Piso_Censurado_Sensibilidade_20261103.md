# V9 — hipótese de piso censurado para sensibilidade TF × TF0

**Status: EXPERIMENTAL, draft, não integrado e NÃO incluído nas 45 opções da execução física oficial.** Issue principal #1257; PR independente da correção do crash físico (#1258).

## Motivação e assimetria

A execução oficial pós-#1255 permite `AbstainCandidate` nos braços TF positivos, mantendo TF0 inalterado. Isso mede desempenho **com indisponibilidade da referência** e pode penalizar a comparação com TF0. A falta do termo numa marginal publicada IBGE não é frequência zero; também não prova que o termo seja necessariamente raro, já que o corpus sintético clean inclui erros de grafia.

## Três políticas distintas, com identidades/fingerprints diferentes

1. `RejectOperation=0`: comportamento histórico, falha se falta frequência TF positiva.
2. `AbstainCandidate=1`: fluxo oficial #1255, nenhum score para candidato com uma TF positiva necessária não publicada; mantém o par em seus denominadores.
3. `CensoredFloorSensitivity=2`: **somente diagnóstico opt-in**. Para uma igualdade exata de unidade nominal sem frequência publicada, calcula `f_hyp = max(minimo_publicado_do_mesmo_slot, minimo_TF_predeclarado)`. A mínima frequência é obtida exclusivamente dos valores positivos da marginal IBGE **já congelada** desse lado/unidade, sem atualizar ou adicionar nomes à tabela. A TF original conserva sua forma `peso × ln(U_ref / f_hyp)`, usando o mesmo `U_ref` observado exclusivamente em TRAIN. Não substitui a referência oficial nem certifica a população.

A convenção de `f_hyp` é uma **hipótese declarada de sensibilidade**, não uma frequência IBGE medida ou um limite estatístico demonstrado. É conservadora apenas sob a hipótese adicional de que termos não publicados teriam frequências menores que o menor valor publicado; corrupção nominal ou recortes populacionais podem violar essa hipótese. O componente de sobrenome permanece identificado como proxy ANY-POSITION, não distribuição empírica da última posição.

Os previews com frequência hipotética carregam `FrequencyEvidence=CensoredFloorHypothesis`; valores publicados carregam `FrequencyEvidence=Published`. A referência IBGE congelada e o seu SHA não mudam, não há fallback invisível para TF0. A política é serializada no snapshot V9 e altera o fingerprint da configuração. O runtime portátil reproduz o mesmo piso para o mesmo snapshot; não recalibra U.

## O que foi feito

- Contrato explícito opt-in, sem mudar os defaults;
- helper puro e verificável do piso, rejeitando ausência fora do opt-in **e marginal publicada vazia para o slot**, sem fabricar frequência hipotética sem base;
- publicação de hipótese separada da frequência observada;
- conexão ao avaliador TRAIN/VALIDATION e ao scorer portátil do snapshot;
- cálculo do menor valor publicado por slot uma só vez, evitando `Min()` sobre milhões de nomes para cada candidato;
- teste de origem publicada versus hipótese, reprodução do mesmo score no snapshot, não contaminação do dicionário IBGE e fingerprints distintos.

**Não foram alterados:** workflow do 30k, 45 configurações, 41 opções elegíveis, quatro diagnósticas oficiais, três margens, TEST lacrado, U_ref TRAIN, 81 células conjuntas, peso/threshold, autorização DT-15, #1242, #1256, HML ou PROD.

## Ainda necessário antes de interpretação científica ou merge

1. Gates de CI completos e testes negativos em LocalDB descartável (ambos os slots pessoa/mãe × prenome/sobrenome; caso com termo publicado; score e Rank→Resolve, par verdadeiro/não verdadeiro).
2. Conferência conjunta da correção física #1258 no SHA de master já atualizado. Esta PR foi criada a partir da mesma master **não empilhada**; reconciliar após eventual merge da #1258, exigindo nova CI.
3. Experimento pré-registrado **em plano próprio**, sem ampliar silenciosamente a grade original. Medir `TF0`, `TF-abstain` e `TF-floor` no mesmo corpus, mesmo blocking, mesmas partições, mesmo fit TRAIN e margens. A seleção automática deve continuar operando apenas sobre os braços elegíveis estabelecidos antecipadamente; o piso não pode ser escolhido pelo TEST.
4. Reportar o **percentual da união de pares verdadeiros** afetados pela ausência IBGE em relação ao denominador de pares candidatos e, separadamente, o denominador de identidades/fontes pré-blocking. Comprovar F1, recall, precisão e FDR por identidade (com dependência intragrupo), Pareto e tempo por braço. A regra ilustrativa de 1% não é um gate.
5. Validar limites superiores/inferiores da frequência censurada e sensibilidade a múltiplos valores pré-declarados; não classificar o mínimo publicado como limite de confiança.

**Não fazer merge ou campanha física de piso sem gates, revisão da hipótese estatística e autorização operacional apropriada.** Uma eventual integração de código não autoriza publicação/ativação do modelo.
