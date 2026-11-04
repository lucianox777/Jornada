# V9 TF-001 — custo já existente, sem Pareto V4

Decisão do operador (04/11/2026): **reutilizar as medições existentes** para a comparação de custo, sem criar V4, sem alterar os parâmetros sugeridos pelo Pareto V3 e sem executar uma campanha física em consequência deste inventário.

## Inventário de evidência do código existente

| Fonte | Indicador existente | Granularidade | Pode comparar custo entre 135 opções TF-001? |
|---|---|---|---|
| `V9PhysicalCalibrationRunResult` | incidências, pares SQL, TRAIN/VALIDATION | run/ruleset | **Não**, não há atribuição de custo por opção/margem no `study.json`. |
| `V9AdaptiveCalibrationStudyResult.CandidatePairsSha256ByRuleset` | hash do frame SQL | ruleset | **Não**: hash prova identidade das arestas, não número de operações. |
| `SharedSurnameCandidatePairsByOption` | pares candidatos com sobrenome compartilhado | opção TF | **Diagnóstico apenas**: subconjunto de pares; não é esforço SQL, CPU nem custo total. |
| `V9PhysicalEndToEndMeasurement` | P95, P99 e amostras fim a fim | opção + margem | **Sim, somente quando medido**, com pelo menos 20 amostras comparáveis, protocolos e ambientes iguais. |
| `V9PhysicalSqlCostQuantiles` | tempos repetidos de materialização SQL | execução/escopo SQL | **Não converter** em P95 do Resolve fim a fim. |
| `v9-evaluate-blocking-cost-budget.py` | grau U médio/P95/P99 em SQLite | plano/partição | Diagnóstico: **não** mede custo físico SQL Server por TF. |
| `V9CpuWorkloadBenchmark` | CPU e elapsed de kernel sintético | benchmark | **Não** imputar a cada opção TF. |
| `V9BootstrapPareto2D` da PR #1261 | Pareto recall × FDR; P95 como requisito operacional quando atestado | opção + margem | **Não** selecionar por custo implícito; contrato V3 preservado. |

## Campanha TF-001 histórica

- Run físico: [41681714081](https://github.com/lucianox777/Jornada/actions/runs/41681714081), SHA `3f21032e1b308df2d60fbf31dc92454efa3ab0a1`; 45 braços × 3 margens = 135 resultados, dos quais 4 braços/12 medições são diagnósticos.
- Conforme o dossiê verificado na [PR #1261](https://github.com/lucianox777/Jornada/pull/1261): `EndToEndMeasurements=[]` e `EndToEndRepetitions=0`. Portanto **não existe comparação de custo temporal físico fim a fim entre os 135 resultados daquela campanha**. Não é correto preencher 135 células com `0 ms`, presumir custos iguais ou recomendar um vencedor pelo custo.
- A comparação em reais ou CPU/RAM por configuração também **não foi demonstrada** pelas evidências armazenadas neste estudo. Não projetar o teste SQLite para SQL Server/Fabric nem somar treinamento a custo operacional por carga.
- O artefato integral do run ocupa aproximadamente 528 MB compactados e não foi importado nem reprocessado nesta PR. Os números por opção só devem ser apresentados após executar a extração local a partir do `study.json` original; nenhuma campanha nova foi executada.

## Relatório auxiliar sem mudança do modelo

Foi criado `Solution/scripts/v9-tf-existing-cost-inventory.py`. Ele lê exclusivamente o `study.json` congelado, valida pareamento das 135 linhas e snapshots, não usa TEST e gera um CSV de **135 linhas** + resumo Markdown + manifesto de proveniência. As colunas de custo P95 ficam **vazias e marcadas NOT_MEASURED** quando não há amostra física, preservando recall/FDR e a integridade dos dados.

Uso após baixar/extrair **fora do Git** o artefato histórico:

```bash
python3 Solution/scripts/v9-tf-existing-cost-inventory.py \
  --study /pasta-protegida/study-tf/study.json \
  --out-dir /pasta-protegida/v9-tf-cost-inventory \
  --sha 3f21032e1b308df2d60fbf31dc92454efa3ab0a1 \
  --run-id 41681714081
```

Saídas: `V9-TF-001-cost-inventory.csv`, `V9-TF-001-cost-inventory.md` e `V9-TF-001-cost-inventory.manifest.json`. O diretório de saída precisa ser **novo**; o script não reescreve o estudo nem seus hashes. O SHA/run-id são declarações fornecidas pelo chamador; **validar contra o GitHub Actions** antes de apresentar o relatório como evidência de proveniência.

**Limitação importante:** o inventário exige o formato original TF-001 (uma regra física, 45 braços, três margens), não é um motor genérico de novos estudos. As eventuais amostras válidas são comparadas com TF0 apenas na mesma margem, mesmo ruleset e mesmo protocolo/ambiente; não alteram `InitialSuggestedOptionId`, `ExploratorySuggestedOptionId` ou os gates. Casos de teste sintéticos cobrem lacunas, duplicidade, hashes, custo e ambiente incongruentes.

## Dependências

A PR #1261 permanece congelada para a prova 30k no SHA exato; **não modificar nem reconciliar a branch** por causa deste inventário. A #1262 mantém a prioridade de preflight de segurança SQL. Nenhum workflow `execute_30k`, TEST, publicação, autorização DT-15, alteração de modelo, merge ou ativação é feito por esta frente.

**Conclusão:** os indicadores de custo existem e devem aparecer no quadro de comparação conforme sua evidência; **medição por opção ausente permanece explicitamente ausente**. Só depois de avaliar o inventário físico real se decidirá se há dados suficientes para comparar custo de modelos sem novas medições.
