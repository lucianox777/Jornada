# V9 — auditoria do universo SQL: incidências completas versus pareamento adjacente

**Esclarecimento de escopo (12/10/2026):** o termo *censo* deste documento refere-se **exclusivamente à contagem opcional de pares de candidatos das chaves materiais SQL dentro de um subframe Gold TRAIN**. **Não é o Censo Demográfico 2022 do IBGE**, cujas distribuições publicadas alimentam o corpus sintético de 30.000 pessoas. A camada de referência IBGE já existe no sistema; não se deve recriá-la nem condicionar a evolução V9 a este diagnóstico opcional. O modo SQL opt-in existe apenas para limitar consultas de custo quadrático e investigar deduplicação de pares; não é pré-requisito de operação de referência IBGE. Reduzir, por exemplo, **120 candidatos para 60** pode ajudar no desempenho, mas não demonstra, isoladamente, benefício material na resolução, na segurança ou na calibração. A prioridade volta a ser o **único scorer FS V9**, seus estados/TF corretamente calibrados e a validação contra Splink/FP/FDR/recall.

**12/10/2026 — etapa 12 da issue #946.** Incremento diagnóstico **opcional e somente leitura**. Não altera V8 histórica, scorer, pesos FS, modelos ATIVOS, `Rank/Resolve`, `VALIDATE/ACTIVATE`, tabelas, índices, snapshots ou parâmetros publicados.

## Lacuna estatística identificada

O `BlockingConditionedUnmatchedPairReader` já utiliza `identidade.blocking_chave` e as regras selecionadas, com a semântica temporal canônica (`STABLE_IDENTITY_DATUM` e `VERSIONED_ALIAS`). Entretanto, **não enumera todas as combinações de pessoas elegíveis dentro de cada assinatura**. Para cada passe, os CTEs ordenam deterministicamente as pessoas por assinatura e formam **pares disjuntos adjacentes**, escolhendo as posições (1,2), (3,4), etc. Depois removem pares repetidos entre passes e escolhem uma fatia determinística do pool.

Esse procedimento é econômico, mas o total `CandidatePoolSize` é **o número de pares adjacentes distintos observados**, não o número de **todos os pares recuperáveis pelo blocking**. Tampouco se trata de sorteio equiprovável entre todos os pares ou de desenho com probabilidades individuais de inclusão conhecidas. Interpretá-lo como universo físico completo produziria um `u` ponderado e uma TF estatisticamente inválidos.

## O que o novo censo mede

Um terceiro result set SQL opcional agrega diretamente sobre **as mesmas chaves filtradas, projeção e passes** utilizados para formar o pool U. Ele não materializa a junção quadrática completa de pares, não exporta nomes, IDs, assinaturas ou hashes individuais. Para cada passe `p` e assinatura física `s`, com `n(p,s)` pessoas **distintas** sob aquela assinatura:

- **Incidências completas no passe:** `C(n,2)=n×(n-1)/2` pares possíveis para aquela assinatura.
- **Incidências dos pares adjacentes do algoritmo atual:** `floor(n/2)`, isto é, no máximo um parceiro para cada pessoa na assinatura.
- **Grupos recuperáveis:** número de assinaturas com `n>=2`; assinaturas singulares não possuem pares.
- **Totais por passe:** somas dessas grandezas por assinatura. O `COUNT_BIG` e o `decimal(38,0)` na consulta evitam a multiplicação inadvertida em `int32`/`bigint` de cardinalidades relevantes.

Um passe com uma única assinatura de quatro pessoas, por exemplo, contém **6 incidências de pares completas** e somente **2 incidências adjacentes**. Uma assinatura com três pessoas tem 3 incidências possíveis, mas somente 1 pareamento adjacente. Esta diferença pode ser material e não era exposta nos diagnósticos anteriores.

**Ressalva essencial:** se duas pessoas compartilham mais de uma assinatura, ou se os mesmos pares são recuperáveis em passes diferentes, o mesmo **par físico** aparece mais de uma vez nas somas de incidência. Logo, a soma `FullPairIncidences` **não é o número de pares únicos** do universo de todos os passes; não deve ser usada como denominador de `u`/TF, nem para inferir probabilidades individuais de inclusão. Contar incidências não atesta universo de pessoas/pares distintos, que exige deduplicação/censo ou desenho de inclusão formal.

## Segurança operacional e desempenho

A chamada SQL adicional **não roda por padrão**. A chave de configuração `LinkageParameters:EnableV9BlockingPairIncidenceCensus` tem valor efetivo `false` quando ausente. Apenas com `true` o leitor acrescenta uma consulta de agregação de assinaturas e consome o terceiro result set; sem ela, o texto das duas consultas existentes é preservado e `PassPairIncidences=null`.

O resultado é exclusivamente diagnóstico. `LinkageParametersWorker` registra somente `PassId`, número de grupos, grupos recuperáveis, incidências completas/adjacentes e o pool adjacente observado. Em todos os casos, os logs explicitam `uniqueFullPairPopulationKnown=false`, `pairInclusionProbabilityKnown=false`, `certified=false` e `published=false`.

O censo pode provocar custo adicional por passe, sobretudo com múltiplos aliases históricos, e deve ser ativado apenas em ambiente de diagnóstico homologado; a proteção padrão evita alongar a execução histórica V8 e a CI por acidente. As consultas são sucessivas, não constituem por si só um snapshot transacional atômico nem comprovam representatividade dos dados fora do subframe Gold amostrado.

## Testes e validação

Os testes unitários cobrem `n=0,1,2,3,4,5,6`, múltiplos tamanhos de grupos, risco de duplicação entre passes, cardinalidades que excedem `int32` e entradas inconsistentes. O código SQL usa as próprias CTEs canônicas dos passes e não gera a junção `n²`. A PR precisa atravessar gates `jornada-ci`, `jornada-linkage-independent-validation` e `jornada-windows-production-installer` no **HEAD exato**.

**Limite de evidência dos gates:** por ser opt-in, a execução padrão dos testes/CI verifica compilação, regressões e o contrato matemático, **não comprova a execução do terceiro result set em um banco de produção**. É necessária homologação SQL com `EnableV9BlockingPairIncidenceCensus=true` para atestar sua viabilidade, performance e semântica histórica.

## Complemento V9 — censo exato limitado de pares físicos distintos

O censo da PR #960 mede **incidências** que podem duplicar a mesma dupla entre diferentes assinaturas ou passes. O novo modo opcional `LinkageParameters:EnableV9ExactUniquePairCensus=true` também habilita o censo de incidências e, **somente quando a soma de incidências completas passa pelo limite**, executa uma segunda consulta read-only sobre as mesmas definições de `gold_sample`, `eligible_keys` e `pass_X_keys`. Para cada assinatura, cria pares **não ordenados canônicos** com `a.pessoa_uuid < b.pessoa_uuid` e, pela união dos passes, faz `GROUP BY (left_uuid,right_uuid)` para obter **`COUNT_BIG(*)` de duplas físicas distintas**. IDs e assinaturas não são retornados; somente a cardinalidade agregada.

**Custo e guardas:** desabilitado por padrão. Limite configurável `LinkageParameters:V9ExactPairCensusMaxIncidences` (default **100.000**, teto rígido **1.000.000**), aplicado à **soma de incidências**, antes de disparar qualquer junção completa de pares. Acima do limite, o sistema **não executa o censo quadrático**, informa `SKIPPED_FULL_PAIR_INCIDENCE_CAP` e mantém os relatórios adjacentes/por passe. Abaixo do limite, retorna `DistinctPairs` e `DuplicateIncidences = Σ C(n,2) - DistinctPairs` com status `DIAGNOSTIC_ONLY_UNIQUE_GOLD_SUBFRAME_NOT_OPERATIONAL_FRAME`. Contagens negativas, acima das incidências e configurações fora dos limites provocam recusa. Nenhum par ou hash é colocado em log.

**O que NÃO está comprovado:** este censo é exato **somente para o subframe Gold TRAIN e as chaves SQL físicas que a consulta leu**. Executa duas consultas sequenciais com reconstrução dos CTEs, **sem snapshot SQL atômico**, portanto mutações entre as leituras podem modificar os totais; inconsistências detectadas falham fechado, mas consistência não equivale a prova de snapshot. O resultado não demonstra que o subframe contém todos os candidatos de produção, que um par recebe probabilidade de inclusão conhecida, que M independente integra o mesmo frame, nem que U é representativo. Ainda requer homologação com o modo explicitamente habilitado em SQL, com monitoramento de tempo e concorrência; a CI padrão **não exerce essa consulta opcional**.

A distinção é fundamental: `CandidatePoolSize` permanece a contagem dos pares **adjacentes únicos**; `Σ FullPairIncidences` conta **incidências com duplicações**; `ExactUniquePairCensus.DistinctPairs`, quando presente, conta as **duplas únicas reais no subframe**, sob o limite. **Nenhum desses números por si certifica o desenho de amostragem ou habilita o scorer V9.**

## Próximos gates até a V9 operacional

1. Definir e provar o universo de **pares distintos** recuperáveis por todos os passes SQL materializados com semântica temporal, deduplicando assinaturas e evitando viés do pareamento adjacente.
2. Criar captura com probabilidade de inclusão auditável por par e suporte representativo dos rótulos M/U — positivos independentes precisam ter pertencimento físico demonstrado, não só semântica lógica sobre Gold atual.
3. Atestar a cadeia de custódia dos nomes brutos e padronização/corte TRAIN/VALIDATION/TEST por grupo/coorte.
4. Calibrar `m/u`, TF integral/conjunta/proxy conforme DC-LK-TF, intervalos de incerteza por conglomerado e FDR/FP/recall com conferência independente C# × Splink em mesmos pares.
5. Submeter decisão DT-15 sem antecipar DT-10, que permanece por último.

**Status:** `DIAGNOSTIC_ONLY_SQL_SIGNATURE_PAIR_INCIDENCES_NOT_PROBABILITY_FRAME`. Nenhuma conclusão de representatividade ou publicação V9 decorre deste censo.
