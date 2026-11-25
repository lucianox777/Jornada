# Relatório 1k — fase 4 V9, S4b como fator principal

**Escopo:** DEV sintético; 1.000 pessoas-base, 2.349 versões, 1.957 origens, 315 CPF; 2.405 pares verdadeiros ao fim das duas ondas. Vencedor #38 `6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee`, sem refit/ativação; nenhuma publicação de variante. HEAD físico `ad991d2daf035b15876fbf9ffc98ae68e836877b`, [run 45628489423](https://github.com/lucianox777/Jornada/actions/runs/45628489423), [artefato 15029273008](https://github.com/lucianox777/Jornada/actions/runs/45628489423/artifacts/15029273008), SHA-256 ZIP `da1148a2ae187256653f8b41a58dd4731f2d5b7a459d8f4f0fb3710e8f2d78a5`; CRC, SHA e commit.txt verificados. GOV-DOC-01, #1939/#1851/#1926/#1931. `E2E_NUCLEO_PASS=true` na baseline física única, `E2E_INTEGRAL_PASS=false`; fase 4 não é certificação produtiva/científica.

## Desenho e integridade

48 combinações (S4b i/ii × b1/b2/resumo × cardinalidade registros/pessoas × r1/r2 × blocking amplo/seguro), 96 trajetórias e 144 estados medidos. Rodada única após ambas as ondas versus uma rodada após cada onda, sem INCREMENTAL. Mesma massa gerada uma vez por tentativa, ordem, vencedor, gabarito externo e fingerprint `8c6304770fb015ab8079c6aa0e34271534e8b641a32007d56a43a3115490d2d5`; pseudônimos distintos entre tentativas, não afirmar igualdade de bytes. Estado separado por trajetória, somente predecessor próprio; cache de grafo imutável compartilhado por onda. Reuso do scorer e grupo congelados, r1, origem líder maior versão/ID, recomposição por verificação/recência/ID. Oráculo mede propostas sem alimentar scorer. Baseline i/b1/registros/r1/amplo uma rodada: paridade exata de UUIDs publicados, união candidata, grupos, motivos e scores verificada. S4b ii com continuidade reproduz recibo inicial. Prior de cardinalidade não altera kernel DECISION_EVIDENCE_SCORING; paridade dos scores/motivos/partição em 72 pares de estados, cardinalidades distintas apenas contabilizadas. R2 experimental restrita a COM_INDICIO e destino único, nenhuma herança em conflito/proteção/abstenções restantes. S4b ii usa um fundador hipotético por identidade coletiva (menor origem entre líderes efetivos); outras origens não seguras. Esta é escolha técnica delegada, não evento fundador oficial nem aprovação de política.

## Efeito principal e ordem de chegada (b1, registros, r1, amplo)

| S4b | Regime | Rodada | Versões | Pares verdadeiros | TP | FP | FN | Recall % | Pessoas desdobradas | Indefinidas (versões) | Novas IDs distintas (total) | FN intraorigem/gestor | FN intergestor | Versões antigas resolvidas nesta rodada |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| i | single | 1 | 2349 | 2405 | 563 | 0 | 1842 | 23,41 | 0 | 1528 | 315 | 244 | 1598 | 0 |
| i | per_wave | 1 | 1957 | 1392 | 273 | 0 | 1119 | 19,61 | 4 | 1272 | 328 | 0 | 1119 | 0 |
| i | per_wave | 2 | 2349 | 2405 | 606 | 0 | 1799 | 25,20 | 4 | 1493 | 329 | 231 | 1568 | 10 |
| ii | single | 1 | 2349 | 2405 | 2255 | 0 | 150 | 93,76 | 0 | 135 | 771 | 18 | 132 | 0 |
| ii | per_wave | 1 | 1957 | 1392 | 963 | 0 | 429 | 69,18 | 6 | 408 | 684 | 0 | 429 | 0 |
| ii | per_wave | 2 | 2349 | 2405 | 2283 | 0 | 122 | 94,93 | 6 | 118 | 777 | 16 | 106 | 306 |

Em todos os 144 estados: zero FP (por rodada/acumulados), FDR observado zero, zero identidades falsamente fundidas. S4b ii recupera 1.692 pares versus i no FULL único e 1.677 pares no estado final das duas rodadas. Duas rodadas versus única: i +43 TP e 4 pessoas desdobradas; ii +28 TP e 6 desdobramentos, já presentes na primeira rodada. Nenhuma identidade nova anterior passou a ser duplicada por novo desdobramento na segunda rodada (não significa ausência dos desdobramentos já presentes). Não comparar recall de rodada parcial como se denominador fosse 2.405; primeira onda tem 1.392 pares verdadeiros. A origem é protegida contra troca; nenhuma alternativa funde referências distintas preexistentes. Os 244 FN intragestor oficiais são versões da mesma origem, excluídas do grafo deliberadamente; mantidos no global, medidos separadamente. S4b iii controle já medido [relatório](Resultado_V9_Fase4_S4b_1k_20261125.md): 0 pares recuperados literal/226 com continuidade, 456 pessoas desdobradas; fora do fatorial principal. S4b ii grupo literal 1.028 pares recuperados; com continuidade 1.692, 0 FP. Todos 457 grupos elegíveis do primeiro contraste têm posterior mínimo interno >=0,9999 (não probabilidade conjunta), tamanhos 2/3/4: 260/138/59 grupos.

## Todas as combinações finais

Cada célula abaixo representa os cinco números TP/FP/FN/fusões falsas/pessoas desdobradas. A cardinalidade por pessoas reproduz exatamente a de registros, portanto a tabela cobre 48 células × 2 regimes sem duplicar linhas. A série dos 144 estados está no artefato.

| Evidência | Resolução | Blocking | Única i | Única ii | Duas ondas i | Duas ondas ii |
|---|---|---|---|---|---|---|---|
| b1 | r1 | broad | 563/0/1842/0/0 | 2255/0/150/0/0 | 606/0/1799/0/4 | 2283/0/122/0/6 |
| b1 | r1 | secure | 563/0/1842/0/0 | 2255/0/150/0/0 | 606/0/1799/0/4 | 2283/0/122/0/6 |
| b1 | r2 | broad | 563/0/1842/0/0 | 2255/0/150/0/0 | 606/0/1799/0/4 | 2283/0/122/0/6 |
| b1 | r2 | secure | 563/0/1842/0/0 | 2255/0/150/0/0 | 606/0/1799/0/4 | 2283/0/122/0/6 |
| b2 | r1 | broad | 563/0/1842/0/0 | 2255/0/150/0/0 | 606/0/1799/0/4 | 2283/0/122/0/6 |
| b2 | r1 | secure | 563/0/1842/0/0 | 2255/0/150/0/0 | 606/0/1799/0/4 | 2283/0/122/0/6 |
| b2 | r2 | broad | 563/0/1842/0/0 | 2255/0/150/0/0 | 606/0/1799/0/4 | 2283/0/122/0/6 |
| b2 | r2 | secure | 563/0/1842/0/0 | 2255/0/150/0/0 | 606/0/1799/0/4 | 2283/0/122/0/6 |
| summary | r1 | broad | 563/0/1842/0/0 | 2255/0/150/0/0 | 606/0/1799/0/4 | 2283/0/122/0/6 |
| summary | r1 | secure | 563/0/1842/0/0 | 2255/0/150/0/0 | 606/0/1799/0/4 | 2283/0/122/0/6 |
| summary | r2 | broad | 563/0/1842/0/0 | 2255/0/150/0/0 | 606/0/1799/0/4 | 2283/0/122/0/6 |
| summary | r2 | secure | 563/0/1842/0/0 | 2255/0/150/0/0 | 606/0/1799/0/4 | 2283/0/122/0/6 |

b2 exclusiva correta/incorreta por faixa de score: zero associações exclusivas em todas as faixas; r2: zero heranças efetivadas; blocking restrito: zero candidata exclusivamente não segura e zero casos amplo INDEFINIDO→restrito nova identidade nesta massa. **Não equivale a prova de equivalência ou segurança populacional**: perfil limpo não discriminou esses mecanismos. b1 permanece controle, r1 e amplo não mudam. O S4b principal dominou recall; interação com ordem de chegada demanda atenção aos desdobramentos.

## Abstenção bruta e efetiva

No relatório agregado há motivos brutos por decisão e efetivos por origem (inclusive propagações), além de métodos hipotéticos, pares por rodada, fusões, desdobramentos, resolvidos tardiamente, duplicação de identidades anteriores, estratos de b2 e blocking. No 1k, i rodada única: 1.528 versões indefinidas, efetivo SEM_INDICIO 1.393, SILVER_CANDIDATO_NAO_RESOLVIDO 105, ABAIXO_T_LINKAGE 30. ii única: 135 indefinidas, 105 SILVER_CANDIDATO_NAO_RESOLVIDO, 30 ABAIXO_T_LINKAGE. ii duas rodadas: 118, das quais 61 SILVER_CANDIDATO_NAO_RESOLVIDO, 55 ABAIXO_T_LINKAGE, 2 GRUPO_GOLD_CONFLITANTE. Não confundir a origem metodológica de cada versão com as decisões da publicação SQL oficial.

## Custo de FS/índice em segunda rodada (r1, amplo, registros)

| S4b | Evidência | FS comparações | FS/registro média | P95 | P99 | Máx | Seguros/pessoa média | P95 | P99 | Máx | Chaves/pessoa média | P95 | P99 | Máx | Índice lógico bytes |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| i | b1 | 923 | 0,4538 | 2 | 3 | 7 | 1,444 | 3 | 4 | 6 | 30,301 | 37 | 42 | 47 | 664009 |
| i | b2 | 1176 | 0,5782 | 3 | 6 | 8 | 1,444 | 3 | 4 | 6 | 30,301 | 37 | 42 | 47 | 664009 |
| i | summary | 601 | 0,2955 | 1 | 1 | 4 | 1,444 | 3 | 4 | 6 | 30,301 | 37 | 42 | 47 | 664009 |
| ii | b1 | 2055 | 1,0103 | 2 | 3 | 7 | 1,269 | 2 | 4 | 6 | 29,740 | 36 | 41 | 47 | 1114937 |
| ii | b2 | 4013 | 1,9730 | 4 | 6 | 8 | 1,269 | 2 | 4 | 6 | 29,740 | 36 | 41 | 47 | 1114937 |
| ii | summary | 1698 | 0,8348 | 1 | 2 | 4 | 1,269 | 2 | 4 | 6 | 29,740 | 36 | 41 | 47 | 1114937 |

b2 com ii: 4.013 vs 2.055 FS b1 (+95,28%) sem ganho observado; resumo 1.698, menos custo mas não preserva a autoridade de registro. Métricas de chave e byte lógicos do índice pré-rodada, não espaço físico do SQL. Contagem de registros seguros por referência antecede scoring e não equivale a pares únicos.

## Recursos e limites

| Medida | Resultado |
|---|---:|
| Soma do motor 144 estados (sem grafo) | 14,23 s |
| Grafo imutável calculado duas vezes (ondas 1/2) | 2,43 s |
| Soma motor + grafo | 16,65 s |
| Mediana por estado | 66,55 ms |
| P95 por estado | 246,46 ms |
| Máximo por estado | 315,49 ms |
| Pico do processo da matriz | 625,45 MiB |
| Pico amostrado da matriz | 625,85 MiB |
| Maior índice lógico experimental pré-rodada | 1,06 MiB |
| Índice físico SQL reservado após FULL | 19,21 MiB |
| Snapshot da captura pré-FULL | 77,69 ms |

| Baseline FULL físico | Valor |
|---|---:|
| Tempo FULL | 34,38 s |
| Leitura física | 5,19 s |
| SNAPSHOT real do leitor | 5,18 s |
| Agrupamento | 0,11 s |
| FS Gold | 0,03 s |
| Tradução | 0,01 s |
| Restante do ciclo FULL (inclui publicação) | 29,03 s |
| Pico amostrado version store atribuído ao banco C07 | 13,12 MiB |
| Pico amostrado de memória FULL | 800,96 MiB |
| Pico do processo FULL | 835,33 MiB |

Amostras FULL: 36; período nominal 1000 ms. Pico amostrado é limite inferior do pico real. Cronômetro begin→commit mede SNAPSHOT do leitor; o restante do FULL não é tempo puro de publicação.

Alternativas em memória não executam SQL: duração SNAPSHOT e tempdb de alternativa são N/A, não evidência de custo zero numa implementação SQL futura. Os tempos são de um ambiente efêmero CI, não SLA; o grafo imutável é medido uma vez por onda, não somado 144 vezes. A série por estado e por FULL está no recibo. Nenhuma PII/dado real; dados privados descartados. O teste não valida qualidade real, não calibra, não aprova política nem escala maior.

## Ponto de parada

Relatório 1k entregue; [plano 10k](Plano_V9_Fase4_10k_20261125.md) é **proposta, não execução autorizada**. Política oficial S4b(i)/b1/r1/amplo permanece. Não rodar 10k/100k, TRAIN/TEST científico, HML/PROD, INCREMENTAL, ativação ou fase 5 de código. PO decide autorização de escala/perfis e tolerâncias de qualidade; eventual mudança S4b/autoridade fundadora fica para decisão posterior específica.