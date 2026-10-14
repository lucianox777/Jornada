# V9 — rede de candidatos U e reamostragem das duas pontas por pessoa-base

## Evidência e decisão (#999)

O *blocking* SQL físico da V9 em `JornadaHarness` já identifica todos os candidatos distintos **M** e **U** do baseline ATIVO+C1 e das onze famílias exploratórias (seis antigas e cinco novas) no mesmo frame. A PR #1017 mediu a variabilidade exploratória do **recall M** reamostrando cada identidade-base sintética, em vez de fingir que seus vários pares M são independentes. **Para U essa solução isolada não é suficiente**: cada candidato U contém dois cidadãos-base *diferentes*, e cada um aparece em várias arestas da rede. Contar U como pares independentes ou computar apenas a concentração de incidências por base (PR #1009) não estima a variação da exposição em uma reamostragem de bases.

## Contrato do novo cálculo

`V9SqlUNetworkBaseBootstrapAudit` recebe, **somente dentro do processo**:

1. Conjunto **completo** de identidades-base sintéticas de observações elegíveis em TRAIN ou VALIDATION (**inclusive pessoas-base sem qualquer candidato U**). Cada identidade aparece em um único split.
2. Todas as arestas físicas **U** da união SQL deduplicada de onze passes. Duas arestas distintas entre as mesmas identidades-base permanecem distintas se vieram de **pares de observações diferentes**; não há deduplicação errônea por par de pessoas-base. Cada aresta deve tocar duas identidades-base **distintas** do mesmo split, já validadas contra o gabarito sintético independente antes da etapa de estimação.
3. Quantidades oficiais `ElevenUnionU` do mesmo relatório SQL e os mesmos seed/fingerprint usados na medição M. Divergência entre número de arestas e U SQL ou `Σ incidence=2*U` aborta.

Em cada split, são realizados **512 replicates** determinísticos de amostragem multinomial *n com reposição de n identidades-base*, ordenadas por identificador apenas em memória. Uma aresta U `(i,j)` recebe o **produto `w_i*w_j` das multiplicidades amostradas de seus dois endpoints**, em vez de ser sorteada como par independente ou contada a partir de um único endpoint. Para `i≠j`, sob a reamostragem multinomial, `E[w_i*w_j]=(n-1)/n`; o total sintético reponderado aplica explicitamente `n/(n-1)` para corrigir somente esse viés finito da média. Esse fator **não corrige** incompletude do universo, desenho do gerador, dependência entre identidades ou seleção dos candidatos pelo próprio SQL.

O relatório publica para cada split: total de identidades-base elegíveis, quantas possuem ao menos uma aresta U, número físico de arestas U, número de incidências (duas pontas por aresta) e os quantis empíricos 2,5%, mediana e 97,5% **da contagem de candidatos U reponderada**. Nomes, CPF, chaves de pessoas-base, gestor, IDs de observações ou listas de arestas **não são serializados**. A CI exige contagem e incidências U exatamente iguais aos do censo SQL, percentis ordenados, fingerprint/seed consistentes e flags explícitas de não promoção nas seeds 42/43/44. NUnit verifica rede multipar com mesmas duas bases, grau zero, ordem determinística, falha por TEST, U intrabase proibido, ponta de split diferente, base duplicada e contagem incorreta.

## O que NÃO representa

Esta **não é** uma estimativa de `u` por comparação FS, nem FP entre vínculos *aceitos*, nem `FDR`, nem um intervalo frequentista validado sobre 11 milhões de cidadãos. É a variabilidade **condicional ao corpus sintético e à rede construída pelo SQL atual**, sob a reamostragem multinomial de bases. Não foi estimado ESS independente para células `m/u`, nem incerteza robusta da fração de falsos *vínculos decididos*, nem viabilidade de consulta a 11M de fontes reais. Não há limite operacional ou orçamento FDR pré-registrado que esse bootstrap satisfaça automaticamente.

O algoritmo exige `UsesBothEndpointsOfEveryUEdge=true`, `TreatsCandidatePairsAsIndependent=false`, `IsProductionUncertaintyOrIndependentEss=false` e `MayPromoteModelOrBlockingPolicy=false`. Todos os resultados são rotulados *descriptive*, não *certified*. A CI verifica integridade de dados e código, **não** libera V9 ou DT-15.

Para calibrar futuramente `m/u` nominal das **81 células conjuntas**, sem reduzir estado desconhecido a neutralidade, e as **sete** células de nascimento, será preciso associar frequência e variabilidade por célula às identidades-base e à rede U **do frame SQL candidato efetivo**, mantendo treino TRAIN, thresholds por VALIDATION e teste cego TEST. Frequências `TF f(value)` devem ser contadas **por uma pessoa-base sintética única em TRAIN**, não por pares ou observações duplicadas. As marginais originais IBGE congeladas só são utilizáveis para unidades semanticamente compatíveis, nunca multiplicadas para inventar distribuição conjunta. Este incremento **não conclui** a suficiência dos 30 mil nem recalibra FS/prior/TF.

**Barreiras imutáveis:** apenas `JornadaHarness` efêmero, `DOTNET_ENVIRONMENT=Development`, nenhum reuso de TEST como seleção; sem alterações em ruleset operacional ATIVO, Rank/Resolve, V8 histórica, V9 decisória, DT-15, IBGE original, JornadaLocal, Gold, NODE/NAS, HML/PROD.
