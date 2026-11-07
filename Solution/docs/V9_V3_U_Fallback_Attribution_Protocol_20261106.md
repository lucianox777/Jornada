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

## Implementação incremental da instrumentação (não é atribuição pareada completa)

O worker C# passa a produzir o objeto `sourceGapAttribution` por configuração física,
com histogramas pré-declarados do gap top1−top2, contagens de fontes sem candidato,
estados nominais (pessoa/mãe) dos dois representantes, e acerto do melhor/segundo
**somente em agregados**. O agrupamento continua BLIND_FS e os labels são consultados
apenas na avaliação pós-ranking; nenhuma alteração de Rank/Resolve. Os bins de 81
células com menos de cinco ocorrências são suprimidos, mantendo contagens globais
censuradas para fechar os totais; não há IDs de fonte/candidato, nomes, scores
individuais ou CPF no novo objeto.

O auditor independente `v9-v3-source-gap-audit.py` exige fechamento contábil,
suporte físico do mesmo frame e política de privacidade, e produz deltas V3−V2
apenas **de histogramas agregados**. O workflow `v9-mu-tf0-controlled-same-frame.yml`
agora executa esse auditor na campanha TRAIN seed42/30k e publica o recibo SHA-256
correspondente se o run completar.

**Limite essencial:** histogramas separados V2 e V3 não demonstram que a mesma fonte
ou o mesmo segundo candidato cruzou o limiar; tampouco atribuem o deslocamento
de score ao fallback U por candidato. Isso exige o passo ainda pendente de análise
pareada por fonte, implementada com agregação em memória e sem exportar linhas.
Nenhuma alternativa de fallback U foi calibrada, selecionada ou ativada nesta PR.

## Comparação pareada por fonte adicionada (instrumentação; não correção do U)

O worker agora retém internamente, **somente em memória**, os IDs sintéticos
das fontes/candidatos, a assinatura canônica do agrupamento blind, os dois
primeiros log-odds, os estados M/U da pessoa e da mãe, a exatidão e o motivo
do Resolve em cada um dos dois métodos. O campo `SourceRankInternal` tem
`[JsonIgnore]`; nenhuma linha por fonte entra em relatórios, recibos nem
artefatos.

Antes de escrever o `mu-statistical-analysis.json`, o Calibrador pareia
V2 e V3 **pelo mesmo ID de fonte** e exige os mesmos SHA de pares SQL,
blocking, prior, threshold, margem e braço TF. Os resultados publicados
são exclusivamente contagens agregadas: TP/FP por método, fontes corretas
perdidas para conflito/pendência/erro, ganhos, mudanças de primeiro e segundo
candidato, mudanças na composição do grupo blind, cruzamentos da margem 0,5,
histograma da variação pareada do gap de log-odds e número de perdas em
células do candidato V3 cujo U usa fallback.

O auditor separado `v9-v3-source-paired-audit.py` exige os quatro estratos
físicos (3/19 passes × TF0/ONE_MF), soma perdas e ganhos aos TP/FP do Resolve,
confere suporte, SHA, ausência de detalhes por fonte e indicação explícita de
**não certificação**. O workflow conserva o novo recibo SHA256. Esta é
instrumentação **pareada**, mas a associação entre perda e presença de fallback
U ainda não é prova de que todo o deslocamento de score veio de U; para tanto,
o experimento posterior precisa decompor o score nominal por candidato,
incluir os controles M, TF e prior e testar intervenções parametrizadas.

A mudança não redefine o estimador, não seleciona parâmetros com TEST/Gold
e não autoriza uso operacional. O merge segue bloqueado até os checks físicos
no SHA exato.


## Resguardo de memória na instrumentação pareada (07/11/2026)

A atribuição pareada por fonte exige temporariamente os rankings privados
V2/V3, mas **não** é justificativa para manter listas de top-two de todas as
configurações em um experimento exclusivamente V3, principalmente quando
há 160 combinações. A separação agora é explícita:

- `includeIdentityOutcomes` preserva os resultados por identidade necessários
  para os limites estatísticos/FDR em **todas** as calibrações.
- `includeSourceRankDiagnostics` pede as listas privadas por fonte somente
  se o plano declara **simultaneamente** `DEFAULT` (V2) e
  `CLASSWISE_SUPPORT_BACKOFF` (V3), caso em que o relatório precisa fazer
  atribuição pareada. Pedir rankings sem resultados por identidade falha fechado.
- Campanhas exclusivamente V3, incluindo a regressão de 160 configurações,
  não retêm essas listas extras. Não se alteram o score, a decisão Resolve,
  as métricas, os 81 estados, os hashes, nem se usa disco como memória.
- A campanha pareada ainda mantém as listas privadas até a redução final
  em agregados; esse volume **residual** deverá ser medido no SHA combinado
  com a PR #1300. Não apresentar este resguardo como uma prova geral de
  ausência de OOM, nem como causa estatística identificada para o fallback U.

Esta frente não faz merge automático, não acessa TEST ou Gold e não autoriza
modelo operacional. O gate físico de V2/V3 deve ser reexecutado para o SHA
que incorpore esta alteração, antes de qualquer integração.
