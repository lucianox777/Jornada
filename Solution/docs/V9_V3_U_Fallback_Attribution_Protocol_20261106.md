# V9 V3 — protocolo pré-declarado de atribuição da regressão TF0 por candidato e célula (06/11/2026)

**Estado:** desenho de experimento, não execução física nem correção certificada. A PR #1297 é a evidência integrada; esta frente não altera scorer, Resolve, M/U, política TF, SQL, Gold ou DT-15.

## Pergunta e controle
No mesmo frame TRAIN_SELECTION seed42/30k, 3 passes, opção A0.5_T0.95_TF0 e margem 0.5, V2 tem 6758 TP, V3 6306 TP, 0 FP e mesmo blocking. Nos 19 passes, separar efeito do suporte U da elegibilidade/fanout. Examinar a **diferença entre o primeiro e o segundo log-odds**, e NÃO inferir causalidade só de TP ou contagens agregadas de células.

## Atribuição exigida antes de alterar U
- Reutilizar exatamente as mesmas observações, split, pares SQL, política TF0, threshold, prior treinado correspondente e candidatos entre V2/V3. Registrar SHA completo do código, plano e corpus, SHA do frame/pairs, ID do run.
- Para cada fonte, reconstruir em memória (sem exportar nomes, UUID, CPF ou registros) o ranking congelado V2 e V3, melhor e segundo candidato, estado de decisão, motivo, log-odds de ambos, diferença (best-second), posterior e a célula M/U de 81 categorias de cada aresta.
- Associar a mudança a **mesmo candidato correto/incorreto**, inversão de ranking, segundo candidato promovido, limiar independente, margem, ausência de candidato, e fonte sem segundo candidato. Discriminar os casos que saíram de RESOLVIDO na V2 para CONFLITO na V3.
- Reconstituir a contribuição de cada célula para o score dos **dois candidatos** e comparar delta V3−V2 da separação. Produzir somente histogramas/contagens agregadas por bins pré-declarados, sem traços por fonte, sem PII, sem labels TEST e sem ranking individual no artefato.
- Bins pré-declarados para gap em log-odds: [-inf,0), [0,.25), [.25,.5), [.5,1), [1,2), [2,4), [4,+inf). A classe “sem segundo candidato” é separada; censurar células pequenas por limite de divulgação, declarando o número censurado.
- Fechar os totais por braço: resolvidos, TP, FP, conflitos por motivo, abaixo de threshold, sem candidato, pendências, por identidade-base e 3/19 passes. Reconciliar contagens e hashes com os envelopes da #1297; sem exportar novos dados pessoais. Reprovar se qualquer total ou SHA divergir.
- Para hipótese de fallback U, reportar estratos de estado U (core, tail suportada, fallback), suporte observado U=0 ou >0, fator de escala, variação do ln(m/u) do melhor **e** do segundo candidato. Prever controle negativo: fontes que preservam ranking/margem apesar de peso U alterado.

## Hipóteses concorrentes (não implementar sem atribuição)
Declarar no **plan.json** e congelar antes de executar:
1. Baseline V2 e V3 original sem mudança, mantendo score e decisão.
2. V3 com escala do fallback U limitada por parâmetro, mantendo distribuição normalizada; nunca truncar probabilidade isolada sem renormalizar.
3. V3 com piso U nas células sem suporte observado, regra matemática explícita e normalização.
4. V3 com fallback U condicionado a suporte independente mínimo, com comportamento fail-closed declarado nos casos sem suporte.

Executar ablação pareada TF0 e ONE_MF, 3 e 19 passes, mesma seed/corpus/pairs. Variar **uma** dimensão por vez. A margem 0, .25, .5, 2 e 4 é um estudo de sensibilidade separado (#1298), não substituto de consertar o estimador U.

## Critérios de decisão e bloqueios
- Prioridade: recuperar perda de TF0 com 3 passes sem aumentar falsos vínculos; medir recall por identidade-base, conflitos, métricas source-level, B-cubed, fragmentação/fusão, fanout, p95 e FDR com limite de incerteza e multiplicidade pré-declarados.
- A comparação dos 19 passes não prova elegibilidade operacional enquanto houver violação de fanout.
- Evidência TRAIN conhecida é diagnóstico técnico, não validação independente. Não consultar TEST/Gold para escolher alternativas. Seleção final deve ser avaliada em holdout independente e passar governança DT-15 antes de qualquer uso operacional.
- Não modificar plano da V2 congelada, evidência histórica ou resultado ativo. PR de código deve ser separada da PR de instrumentação quando a atribuição for demonstrada.

## Gate desta frente
Somente desenho/contrato: nenhuma alegação de atribuição causal ou de V3 corrigida. A primeira implementação seguinte deve prover instrumentação agregada e testes negativos de reconciliação, sem dados por fonte em artefatos.
