# V9 — assimetria TF × TF0 causada por frequências IBGE não publicadas

**Status: correção parcial em PR draft; V9 física da master original permanece congelada.** Issue de governança: #1257.

## Problema material

O `AbstainCandidate` opt-in da PR #1255 é conservador individualmente (não inventa `f_IBGE`), mas a grade compara 44 opções de TF positivo, que podem recusar um candidato, com um controle TF0 que não apresenta essa recusa. Uma diferença aparente TF × TF0 pode, portanto, misturar poder discriminativo da TF e custo de indisponibilidade da referência. A direção e tamanho do efeito *agregado pós Rank→Resolve* dependem das observações reais do ensaio e não são identificáveis a priori.

O corpus `clean` é sintético e possui tentativas de erros nominais (6% pessoa, 10% mãe). Ausência de nome na referência publicada **não implica** frequência zero, nem implica que a variação escrita tenha sido observada na população. Não são legítimos: atribuir TF0 ao ausente, declarar `f_IBGE` como conhecida ou usar TEST para decidir o piso.

## Correção parcial já implementada nesta branch

O diagnóstico existente `Audit()` produz `MissingPublishedTruePairs` e `MissingPublishedNonmatchPairs` por slot, porém somar esses contadores pode contar o mesmo par várias vezes. O novo `AuditUnion()` contabiliza **uma única vez cada par candidato SQL VALIDATION** que tem ao menos um TF positivo aplicável sem frequência publicada, com quatro contadores explícitos:

- `TrueCandidatePairs`;
- `NonmatchCandidatePairs`;
- `TrueCandidatePairsWithMissingFrequency`;
- `NonmatchCandidatePairsWithMissingFrequency`.

O agregado aparece em `AdaptiveCalibrationHoldoutMetrics.UnpublishedTfUnionImpact`, mantendo inalteradas pontuação, candidatos, pesos, `f_IBGE`, `U_ref`, thresholds, denominadores e decisões de classificação. Essa nova métrica não substitui métricas por **identidade**; o denominador verdadeiro aqui são os **pares candidatos após blocking**. Não confundir `Abstentions = truePairs - tp` com pares recusados especificamente por ausência IBGE.

Os testes unitários verificam sobreposição da mesma ausência em dois slots, rótulos M/U, deduplicação e recusa de TEST.

## Próxima correção funcional: piso apenas como sensibilidade explicitamente modelada

Após guardar evidência do 30k físico SHA `b3a93d90c4914fe33b2197f3ae40462601817e83`, implementar uma política **distinta**, versionada e opt-in de *frequência censurada* com limites/hipóteses predefinidos. O piso não será rotulado `f_IBGE` publicado. Preservar a fórmula do ajuste, `TF = peso × ln(U_ref / f)`, mas identificar `f` como hipótese de sensibilidade para o termo sem publicação.

Comparar **TF0**, **TF com abstenção** e **TF com piso/limites de censura**, usando exatamente a mesma massa, pares de blocking, margens, 81 células, `U_ref` TRAIN, divisões TRAIN/VALIDATION/TEST e perfis. Separar cobertura de TF e qualidade condicional dos pares que possuem TF válida. Examinar precisão/recall/F1, pares aceitos e pendentes, falsos positivos, grupos e FDR com dependência por identidade-base; medir sensibilidade ao piso e não escolher piso pelo TEST.

O valor ilustrativo de 1% dos verdadeiros afetados é somente triagem: **não é gate científico nem certificação**. Decisões de implementação, seleção e autorização DT-15 são independentes. A PR #1256, sobre persistência dos arquivos em `ref`, não resolve essa assimetria e não deve entrar na campanha em andamento.
