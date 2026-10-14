# V9 — Busca de blocking por células, memória primeiro (diagnóstico)

Relacionado à issue #999. Esta é uma política **experimental**, não uma certificação nem ativação.

## Elegibilidade proposta

No ramo probabilístico, **nome da pessoa utilizável é obrigatório** e ao menos **um** entre nome da mãe utilizável ou nascimento válido deve estar presente. As únicas combinações elegíveis são P+M, P+D e P+M+D. M+D sem P e P isolado resultam em abstenção. Placeholders, sentinelas e dados inválidos não contam. CPF válido mantém fluxo determinístico separado. A decisão de vínculo continua dependendo de FS/TF calibrado, conflitos e concorrência, nunca apenas da presença dos campos.

Esta proposta **substitui para o experimento** a regra anterior de quaisquer dois campos; não altera o comportamento operacional.

## Etapa 1 — laboratório em memória (TRAIN/VALIDATION)

- Carregar observações sintéticas normalizadas, identidades-base verdadeiras e campos faltantes preservados, **uma vez**; não criar novo corpus ou reamostrar em cada combinação.
- Gerar índices em memória por nome completo, tokens, prefixos, mãe e nascimento, sem alterar o IBGE original. O laboratório pode usar coleções/indexação no processo: **não exige** SQL Server In-Memory OLTP.
- Avaliar combinações de células de blocking: P exato + D exato; P exato + M exato; P prefixo3 + D; P prefixo3 + M prefixo3; P fonético + M; P tokens + D; e combinações progressivas com união deduplicada de pares. Ordenar pela seletividade medida e ganho marginal, não por preferência arbitrária.
- Tratar cada célula como predicado de geração de candidatos; **não** interpretar igualdade ou prefixo como decisão positiva. Nenhum early-stop pode omitir candidato verdadeiro sem medir o custo. Preservar bloqueio por gestor, NULLs, CPF ausente e integridade de UUID.
- Calcular para cada célula e conjunto: M_total, M_elegível, M_recuperado, U_sql, recall total/elegível, cobertura, abstenções, ganho incremental M/U, U/M, fanout P50/P95/P99 incluindo zero, custo de execução/memória e FDR hipotético U/(M_recuperado+U). FDR hipotético **não** é FDR do classificador.
- Estimar 81 células conjuntas nominais m/u e 7 DOB no **frame de candidatos produzido**; usar TRAIN para estimativa e VALIDATION para seleção. TF por indivíduos-base únicos; nomes de mães: frequência feminina Brasil; nomes de pessoas: São Paulo, somente nas unidades compatíveis. Não multiplicar marginais para fabricar conjunta. Reportar ESS/suporte e incerteza.
- Registrar fingerprint do corpus, predicados, ordem, parâmetros, normalização e custos. Se insuficiente, **INCONCLUSIVO**, sem ajustar usando TEST.

## Gate para passar à etapa física

**Somente** candidatos de blocking com resultados positivos e reproduzíveis em TRAIN e VALIDATION passam ao SQL físico. Antes do ensaio, versionar os limites mínimos de recall total/elegível, risco FDR e fanout/custo; reportar o conjunto não dominado (Pareto), incluindo alternativa de abstenção. Sem limites versionados ou sem configuração certificável, permanecer no laboratório, sem promoção.

## Etapa 2 — reprodução física, apenas após seleção

Para a configuração congelada e sua linha de base, executar no SQL Server `JornadaHarness` efêmero da CI: equivalência exata de pares (com divergências explicadas), índices e planos, tempo, memória, fanout, cap fail-closed, m/u/TF no mesmo frame. **A validação física é obrigatória antes de qualquer conclusão de V9**, mas não precisa ser repetida durante a busca exploratória de combinações.

Após validação física e congelamento do snapshot, executar TEST cego sem seleção/refit. Se reprovar, não reajustar pelo TEST; abrir novo ciclo TRAIN/VALIDATION com TEST novo e isolado. CI verde sozinha não autoriza DT15.

## Exclusões

Sem mudanças em Gold, Rank/Resolve, V8, IBGE original, JornadaLocal, NODE/NAS, HML/PROD ou ativação V9/DT15.
