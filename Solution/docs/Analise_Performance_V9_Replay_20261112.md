# Análise de performance V9 — preparação do replay (12/11/2026)

## Contexto
O PO solicitou prosseguir na investigação de otimizações do calibrador TF4/324, sem mudar o algoritmo científico. A campanha física vigente utiliza `V9NestedCalibrationCoordinator.EvaluateAsync` e `V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync`.

## Evidência no código
Antes desta proposta, cada hipótese elegível executava `frame.Where(x => x.Partition == "VALIDATION").ToArray()` e criava `c.Execution with { TotalComparedConfigurations = 1 }` dentro do laço. Os objetos são equivalentes entre hipóteses do mesmo grupo físico. A criação de assinaturas, a carga SQL, a materialização de pares e o ajuste FS são realizados por grupo; `EndToEndRepetitions=0` já desativa as repetições completas de benchmark na busca.

## Alteração proposta
Materializar uma vez por grupo `validationFrame` e `replayExecution`, reutilizando os dois valores nas chamadas a `EvaluateFrozenHoldoutAsync`. Nenhum replay é eliminado. Preservam-se a seleção de TRAIN/VALIDATION, todas as métricas, snapshots, checks de divergência, 324 hipóteses, TF4, AUTO_0, FDR, limites e tratamento de erro.

## Impactos, riscos e validação
A alteração reduz alocações e varreduras de memória, mas **não há evidência de ganho significativo no tempo total**: o custo dominante pode estar em SQL/FS/Rank-Resolve. É necessário CI, testes de equivalência e benchmark físico antes de atribuir ganho. A avaliação de compartilhamento de tabelas SQL entre conexões independentes permanece investigação separada, não autorizada nem implementada.

## Precedência, governança e rastreabilidade
Proposta de engenharia decorrente do pedido de investigação do PO, **não constitui aprovação de nova política científica ou arquitetural**. A precedência das decisões vigentes, inclusive GOV-DOC-01, GOV-V9-MARGIN-REUSE-20261111 e GOV-V9-RESTAURAR-PROGRESSO-BASELINE-20261112, permanece intacta. Branch `perf/v9-hoist-validation-replay-input-20261112`, arquivo `Solution/src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCoordinator.cs`. Não integrar na master antes dos gates e aprovação aplicáveis.

## Continuação — indexação dos resultados físicos (12/11/2026)
Após a PR #1462, a primeira amostra pós-SQL caiu de 107,8 s para 72,5 s, mas essa observação não estabelece causalidade. Uma otimização adicional, isolada na branch `perf/v9-index-physical-results-20261112`, substitui buscas `Single(predicate)` repetidas nas coleções de métricas, snapshots e opções por índices `ToLookup` construídos uma vez por grupo físico. Cada consulta mantém `Single()`, preservando falha para chaves ausentes ou duplicadas. Os replays congelados, o SQL, os dados científicos e as políticas permanecem inalterados. Impacto esperado: menos varreduras CPU por hipótese; ganho de tempo global desconhecido e possivelmente pequeno. Validar build e testes antes de integrar. Esta proposta não aprova reutilização de tabelas SQL entre conexões, nem modifica decisões vigentes ou o índice de decisões.


## Comparativo observacional das execuções #43292437608 e #43295710416 (12/11/2026)

**Escopo:** primeira execução corresponde ao merge da PR #1462 (SHA `0f685b9855b4ce8072d7e04209484df33d2ab52c`), e a segunda ao merge da PR #1463, cumulativa à #1462 (SHA `ebd5ce8b4b1bfef4f0ec4945fa433414e354a476`). As duas campanhas foram canceladas, após respectivamente 10 e 18 hipóteses. A comparação pareada se limita às dez primeiras hipóteses, cujos identificadores e ordem coincidem.

| Hipótese | #1462 pós-SQL (s) | #1462 + #1463 pós-SQL (s) |
|---:|---:|---:|
| 1 | 72,5 | 79,7 |
| 2 | 70,7 | 80,8 |
| 3 | 71,8 | 77,8 |
| 4 | 69,8 | 78,8 |
| 5 | 73,8 | 80,6 |
| 6 | 76,3 | 81,5 |
| 7 | 75,7 | 77,7 |
| 8 | 77,2 | 77,8 |
| 9 | 73,2 | 80,6 |
| 10 | 73,7 | 78,3 |

Médias aproximadas conforme logs: 73,6 s versus 79,1 s por hipótese pós-SQL; diferença observada de +5,5 s (+7,5% tomando #1462 como base). Tempo decorrido até a décima hipótese: 14,7 min versus 16,1 min. Na segunda campanha, o ETA caiu de 506,8 min na décima hipótese para 451,9 min na décima oitava; isso não demonstra aceleração comparativa perante a primeira campanha, cancelada na décima.

**Interpretação:** as dez diferenças são positivas, indício observacional consistente de maior tempo na segunda execução, porém **não comprovam causalidade atribuível à #1463**. O diff da PR #1463 constrói três `ToLookup` uma vez por grupo e substitui buscas lineares `Single(predicate)` por buscas indexadas com `Single()`; mantém SQL, replay, seleção e validações científicas. O custo de construir os índices pode importar se coleções forem pequenas, mas essa hipótese ainda não foi medida. Entre os fatores confundidores estão diferenças de CPU, cache, ambiente e custo dos replays, além de variação temporal entre campanhas.

**Próximo experimento recomendado (não executado):** comparar A/B pareado com mesma entrada, ordem, SHA de corpus e condições SQL controladas; executar múltiplas repetições alternadas; registrar separadamente tempos de montagem dos três índices, consultas de resultados, preparo de validação e cada replay, além dos tempos de SQL e do ambiente runner. Conferir equivalência de identificadores, métricas, snapshots, falhas e decisões científicas. Não extrapolar a média das primeiras dez hipóteses para as 324 nem reverter a #1463 sem experimento controlado.

**Referências oficiais:** [PR #1462](https://github.com/lucianox777/Jornada/pull/1462), [PR #1463](https://github.com/lucianox777/Jornada/pull/1463), [run #43292437608](https://github.com/lucianox777/Jornada/actions/runs/43292437608), [run #43295710416](https://github.com/lucianox777/Jornada/actions/runs/43295710416). Esta seção registra evidência e plano de medição; **não constitui nova decisão aprovada do PO**, não altera a precedência das decisões vigentes nem exige alteração do índice de decisões.
