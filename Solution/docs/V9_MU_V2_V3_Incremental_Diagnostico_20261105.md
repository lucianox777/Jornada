# V9 — protocolo incremental com base V2 e diagnóstico de divergência histórica

**Data:** 05/11/2026. **Status:** diagnóstico reproduzido em artefatos já executados; ablações adicionais propostas, ainda **não executadas**. Classificação: regressão técnica/exploratória, não confirmatória.

## 1. Pergunta exata

Por que o incremento **V2 congelada → V3 histórica** alterou o número de TP em **−4**, enquanto o incremento **DEFAULT/V2 → CLASSWISE/V3 no Calibrador V9** alterou em **+69**?

A V3 histórica foi derivada do **mesmo snapshot V2 congelado**, mudando somente as 81 probabilidades conjuntas M/U, a proveniência, o fingerprint e a versão do schema. Já a comparação V9 foi um **novo fit TRAIN** sobre outro frame/partição. Essas duas diferenças **não** constituem uma estimativa da melhoria entre versões sobre a mesma população.

Fonte histórica: `v2_v3_independent.zip`, contendo `v3-independent-comparison.json` SHA256 `362311738ccb47a0018cb03db134e9dca4d48b1474c2a1712143006e4b4875f7`; fit V3 arquivado SHA256 `8bf6d92db9efffa7cfdc26bc8d23ade01a6fc3cb430b5a5b0e9c425557cbdefc`.

Fonte V9: Action [42120197234](https://github.com/lucianox777/Jornada/actions/runs/42120197234), SHA de código `4d5718e57f33db6d506cfce929230ca6069a1602`, Artifact `13459820384`, relatório `mu-statistical-analysis.json` SHA256 `f418874fd595ba3959080eb651becebe8fe2222ed6d4663cb8343bd0dc5eecb3`. Dados resumidos em [`mu-v2-v3-incremental-drift.json`](../data/calibration/v9/mu-v2-v3-incremental-20261105/mu-v2-v3-incremental-drift.json).

## 2. Variáveis divergentes de fato

| Variável | V3 histórica | V9 atual, 3 passes | Interpretação |
|---|---:|---:|---|
| Contagem física M no TRAIN | 19.896 | 13.997 | TRAIN/candidatos/split distintos |
| Contagem física U no TRAIN | 26.378 | 13.472 | Redução proporcional de U maior que de M |
| Proporção U/M | 1,32579 | 0,96249 | Muda o prior estimado do par candidato |
| **PRIOR_MATCH_PROBABILITY** | **0,429962** (valor do snapshot) | **0,509556** (**reconstruído** com fórmula C#, não lido do snapshot atual) | **+7,9594 pontos percentuais** |
| Odds a priori | referência | **1,37745 ×** a referência | Deslocamento de log-odds de **+0,32023** |
| Política TF para termo não publicado | **1 — AbstainCandidate** | **0 — RejectOperation (default C#; campo omitido do plano)** | Diferença de contrato concreta, **impacto em dados ainda não medido** |
| Número de células | 81 | 81 | Mesmo contrato nominal |
| Células core M / core U | 14 / 8 | 15 / 8 | Mudança discreta de suporte |
| Distância de variação total nas frequências observadas M / U (81 células) | base | **0,007388 / 0,006326** | Desvios marginais das distribuições *observadas*, não de todo posterior |
| Blocking histórico (3 passes) | 3 passes | **mesmos passes e campos** | Controlado |
| Alfa, threshold, margem | 0,5 / 0,95 / 0,5 | 0,5 / 0,95 / 0,5 | Controlados |
| População efetivamente avaliada | VALIDATION congelada, 7.918 fontes elegíveis | TRAIN_SELECTION interno, 7.011 elegíveis | **Não emparelhável entre campanhas** |

O prior antigo reconcilia exatamente com a fórmula do adaptador:
`prior = (M + alfa) / (M + U + 2·alfa)`. No V9 o valor **inferido** de `M=13.997`, `U=13.472` e `alfa=0,5` é `0,509555879`; o prior antigo preservado no fit é `0,429962183`. A equivalência numérica dos odds implica que, **se todo o restante das evidências por par permanecesse idêntico**, trocar o prior sozinho faria o limiar 0,95 comportar-se aproximadamente como **0,9324** sob o prior anterior. **Essa equivalência é matemática, não um resultado de replay físico.**

A diferença entre **AbstainCandidate=1** no fit histórico e **RejectOperation=0** no plano V9 exige teste próprio: os termos não publicados podem ser ausentes no corpus atual, e **não sabemos** quantos candidatos foram efetivamente afetados. Não afirmar que essa política já explica a mudança dos TP.

A variação total pequena das proporções das 81 células **não** demonstra que os pesos de log(M/U) ou suas caudas são pouco relevantes; células raras podem produzir efeitos fortes perto do limiar. Os parâmetros V3 `0,99; M=5; U=5; RetainSupportedTail=true` foram os mesmos em ambas as execuções.

## 3. Evidência emparelhada histórica já disponível

Na VALIDATION histórica de 5.521 identidades-base, o incremento V2→V3 afetou apenas **30 identidades**: **15 melhoraram** (+22 TP somados) e **15 pioraram** (−26 TP somados), saldo **−4 TP**, sem aumento de FP. As demais 5.491 identidades ficaram inalteradas. Este saldo é **descritivo**, pois o experimento original não foi uma certificação confirmatória.

No ensaio técnico do V9 sobre TRAIN_SELECTION, o incremento DEFAULT→CLASSWISE na configuração de 3 passes produziu **+69 TP**, mas não temos, nesse relatório agregado, a matriz por identidade equivalente à comparação histórica. **Não confundir esses 69 TP com ganho entre a V3 antiga e a V9 atual.**

## 4. Experimento incremental pedido: V2 como única base

**Regra de ouro:** alterar **uma dimensão por vez**; todos os braços do mesmo bloco usam **idênticos SHA do corpus, split, catálogo de blocking, TF, prior, evidência de nascimento, threshold, margem e protocolo Resolve**. Nenhum parâmetro deve ser escolhido por observar o resultado do próprio braço. Não sobrescrever snapshots V2/V3 congelados.

| Etapa | Única alteração em relação à anterior | Observação |
|---|---|---|
| **E0** | V2 original congelada | Baseline; reexecutar somente se o snapshot e a base original estiverem disponíveis |
| **E1** | Trocar estimação M/U por CLASSWISE, cobertura 0,99, M mínimo 1, U mínimo 1, sem preservar cauda | Primeiro contraste estrutural dentro do contrato do grid atual (mínimo ≥1) |
| **E2** | Alterar somente o mínimo M de 1 para 5 | Testar efeito do piso M |
| **E3** | Alterar somente o mínimo U de 1 para 5 | Testar efeito do piso U |
| **E4** | Ativar somente `RetainSupportedTail=true` | V3 histórica em termos da política M/U |
| **D1** | Partindo de E4, alterar **apenas o prior** histórico para o valor do novo frame | Contrafactual diagnóstico; snapshot derivado e hash próprios, sem re-fit |
| **D2** | Partindo de E4, alterar **apenas a política TF para termo não publicado** de 1 para 0 | Contar termos afetados e falhas; não transformar rejeição em aceite |
| **D3** | Refit com a partição/frame atual, mantendo política V3 e dados de avaliação comuns | Muda classe de experiência: separar mudanças de frame de prior, birth e M/U |

**Por que E1–E4 antes de D1–D3?** Assim, primeiro medimos o efeito incremental da V3 em relação à V2 **na mesma base**, depois investigamos os fatores de contexto que fazem o resultado parecer diferente. Os mínimos 1 não reproduzem o valor final histórico M=5/U=5; são contrafactuais exploratórios explicitamente rotulados.

Para cada transição relatar TP, FP, recall, conflitos, não resolvidos, fusões, fragmentação, latência P95, fanout P99, elegibilidade e delta por **identidade-base**. Exigir diagnóstico de origem dos aceites: mudança no score, cruzamento de `threshold=0,95`, bloqueio, termo TF não publicado, margem ou agrupamento. Relatar separadamente os termos com maior Δlog(M/U) nas 81 células e o efeito de prior na posterior; **não** atribuir causa a uma variável sem replays emparelhados.

## 5. Decisão simplificada — prior calculado, NÃO parâmetro selecionável

**Orientação posterior do responsável:** não introduzir `PriorEstimationMethods`, `PriorOptions` nem outro parâmetro de busca de prior. O prior permanece **calculado** pelo método do Calibrador: `(M + alfa)/(M + U + 2*alfa)`, usando o frame físico de candidatos do TRAIN. O IBGE é referência de frequências nominais/TF, não fonte suficiente para determinar sozinho a prevalência de match condicionada ao blocking.

Para diagnosticar a variável de maior efeito, aplicar a **única intervenção experimental explicitamente fixada**: copiar o snapshot atual V9, buscar `PRIOR_MATCH_PROBABILITY` do snapshot histórico V3 SHA `8bf6d92db9efffa7cfdc26bc8d23ade01a6fc3cb430b5a5b0e9c425557cbdefc` e substituir apenas essa chave em `Parameters` na cópia. O valor comprovado é `0.4299621826039978`; o prior V9 `0.509555879140881` foi reconstruído das contagens da Action e deve ser conferido nos bytes do fit atual antes do replay. Snapshot novo e digest novo, originais imutáveis. Não recalcular as 81 células M/U, TF, U nominal, birth, passes, thresholds nem corpus; recalcular **somente scores/posteriores, ranking e decisões** no mesmo frame.

Para isolar a interação, realizar os quatro braços fixos `DEFAULT/prior V9`, `DEFAULT/prior V3`, `CLASSWISE/prior V9`, `CLASSWISE/prior V3`. É um experimento contrafactual de diagnóstico com referência histórica pinada no SHA, **não** opção do usuário nem alteração de valor-padrão operacional. Validar que só a chave prior foi alterada, registrar o delta por identidade-base, limites de fanout/latência e contagem de candidatos TF ausentes. Não inferir que o prior V3 é o valor correto para a base V9; eventual mudança futura de **regra de cálculo** exigiria estudo separado.

**Estado:** a cópia e a execução física desses quatro braços **ainda não foram feitas**; sem novos resultados estatísticos. A origem do prior histórico é a contagem M/U do TRAIN original, não um cálculo direto das tabelas IBGE.

## 5. Fronteiras e critérios

O gate automático seed42 é apenas teste técnico, não autorização para usar VALIDATION independente, seeds novas, Gold ou TEST. Estes dependem de solicitação manual, plano `LOCKED`, hipótese e alfa fixados antes da execução. Nenhum experimento deve converter `FP=0` observado em FDR certificada, especialmente com `ClusterIidIndependentlyValidated=false`. A autorização DT-15 continua distinta.

**Status atual:** E0→E4 com o snapshot V2 histórico **já foi comparado como contraste V2→V3 final** (−4 TP no conjunto histórico), e o contraste V2→V3 no novo frame técnico (+69 TP) também existe; **os braços intermediários E1–E3 e contrafactuais D1–D3 não foram executados**. Este documento não substitui esses experimentos nem afirma ter identificado causalidade. Prior e política de termos não publicados são os **primeiros suspeitos mensuráveis**; a mudança de população é um fator estrutural incontornável.
