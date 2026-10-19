# V9: cache exato dos primitivos de blocking — 19/10/2026

A descoberta dos mesmos 30.000 registros TRAIN passou de mediana 19,69 s para
4,32 s: 4,56 vezes mais rápida nesta execução. A alocação cumulativa média caiu
de 8.804,25 para 1.192,92 MiB, redução de 86,45%. O working set observado depois
da execução ficou em até 392,34 MiB. Não houve redução da carga ou dos orçamentos.

O diagnóstico do código identificou trabalho redundante na avaliação de cada
primitivo: o otimizador completo calculava contribuições leave-one-pass-out para
um único passe, repetia varreduras dos dicionários e criava closures/enumeradores
por observação. O pool usa apenas os diagnósticos exatos desse único passe.
`BlockingRuleSetSearch` agora projeta uma matriz de colunas ternárias uma vez
(ausente/falso/verdadeiro) e calcula os mesmos totais em loops indexados. Pesos,
denominadores e somas continuam em `decimal`; só as razões finais viram `double`,
como no diagnóstico canônico. O otimizador final, ordenação, critérios de suporte,
IDs e enumeração das uniões permanecem os mesmos.

| Observações TRAIN | Antes | Depois | Alocação antes → depois |
|---:|---:|---:|---:|
| 300 | 211,11 ms | 40,34 ms | 90,47 → 12,54 MiB |
| 1.500 | 333,68 ms | 196,18 ms | 442,36 → 60,51 MiB |
| 3.000 | 888,76 ms | 204,09 ms | 882,37 → 119,83 MiB |
| 30.000 | 19.693,50 ms | 4.320,45 ms | 8.804,25 → 1.192,92 MiB |

Mesma máquina, Release, .NET 10.0.13, um aquecimento por escala e três amostras.
O caso maior preserva 30.000 positivos pré-blocking completos, 90.000 não-vínculos,
378 primitivos, pool explícito de 16 e todas as 136 uniões. O caso Pareto de 8.000
configurações ficou em 237,46 ms depois, dentro da variação observada; esse código
não foi alterado. Tempos possuem variação de JIT/GC e atividade concorrente; não são SLA.

Evidência de equivalência:

- 249 testes Blocking/replay passaram, incluindo seis casos parametrizados que
  cobrem 72 corpus aleatórios com pesos fracionários, entradas ausentes, null,
  valores falsos/verdadeiros, passes de um a três campos e suporte U obrigatório
  ligado/desligado. O oracle usa o otimizador/diagnóstico canônico anterior para
  comparar todos os IDs/passes/uniões e as estatísticas finais serializadas.
- `V9_Discovery_Exact_Equivalence_Baseline_20261019.json` e
  `V9_Discovery_Exact_Equivalence_Optimized_20261019.json` são byte a byte iguais:
  contêm SHA-256 de **todas as uniões ordenadas** e fingerprints do TRAIN projetado,
  amostra e schema, além das contagens, nas quatro escalas até 30.000 registros.
- `V9_CPU_Workload_Benchmark_20261019.json` preserva as medições anteriores e os
  hashes das assemblies; `V9_CPU_Workload_Benchmark_Optimized_20261019.json` registra
  todas as amostras posteriores e assemblies consumidas. O benchmark posterior
  também calcula o hash das uniões dentro do trecho cronometrado. No artefato
  anterior, o campo de fingerprint da descoberta cobre o frame projetado/amostra;
  a prova adicional de uniões foi capturada antes de recompilar o código alterado.

O modo `--discovery-proof-only` do benchmark reproduz os arquivos de equivalência.
O modo padrão reproduz a medição; instruções de build estão em
`V9_GPU_CPU_Benefit_Assessment_20261019.md`. Builds Release do Worker e do benchmark
passaram sem avisos. Não foi executado TEST, SQL ou GPU, nem instalada dependência.
