# V9 TF-001 — 135 resultados reais recuperados do log de leitura do estudo

**Escopo:** reconstrução somente-leitura de 135 **métricas agregadas de VALIDATION** que já estavam impressas no log do workflow histórico. Não é reexecução física, não gera um novo `study.json`, não certifica FDR, não consulta TEST, não modifica o Calibrador nem o Pareto V3.

## Fontes e proveniência

1. Estudo físico original: [run 41681714081](https://github.com/lucianox777/Jornada/actions/runs/41681714081), `execute_30k` concluído em 03/11/2026 no commit **`3f21032e1b308df2d60fbf31dc92454efa3ab0a1`**, corpus sintético 30.000, seed 42, plano `1e3dc98d980a34ad233787e2ded8c29873a8a0cfc43b03a6f9fe92225c9941bf`.
2. Leitura agregada pré-existente do arquivo original `art/study-tf/study.json`: [run 41700736265](https://github.com/lucianox777/Jornada/actions/runs/41700736265), job **`summary` 126143029692**, branch temporária **não integrada** `tmp/v9-study-summary-41681714081`. O log imprime `## LIST SourceMeasurements 135` e os 135 objetos JSON agregados, além de `## LIST FrozenConfigurations 135`, contadores de pares e `EndToEndMeasurements=[]`.
3. O novo arquivo [CSV com 135 linhas](../evidence/V9_TF_001_135_Validation_Aggregates_From_ReadOnly_Log_20261104.csv) foi extraído **dessas linhas do log**, validando: exatamente 45 `OptionId` × 3 margens (0,5; 2; 4), um ruleset, 5.556 identidades-base, 7.841 fontes verdadeiras elegíveis, contagens reconciliadas com 10.296 observações VALIDATION por linha, e `FDR = FP / (TP+FP)`.
4. **Limitação da cadeia de evidência:** o CSV deriva dos logs de um leitor intermediário, não é uma assinatura do `study.json` completo nem substitui seu hash e o artefato original. O arquivo ZIP de ~528 MB foi localizado pelo conector, mas **não foi descompactado/validado diretamente neste ambiente**. O SHA/run originais acima foram verificados no histórico do GitHub; o próprio conteúdo do log é a origem da tabela.

## Números, margem 0,5 (VALIDATION; observados, não certificados)

| Opção | Recall | TP | FP | FDR observado | Conflitos | Pendentes |
|---|---:|---:|---:|---:|---:|---:|
| TF0 (controle) | 95,7148% | 7.505 | 0 | 0% | 176 | 2.615 |
| TF prenome da mãe, peso 1, simétrico | 96,0719% | 7.533 | 0 | 0% | 143 | 2.620 |
| TF prenomes pessoa+mãe, peso 0,5, simétrico | 96,3780% | 7.557 | 1 | 0,01323% | 153 | 2.585 |
| TF prenomes pessoa+mãe, peso 1, simétrico | **96,4928%** | **7.566** | **1** | **0,01322%** | **143** | **2.586** |

O maior recall observado recuperou **61 TP a mais** que TF0, com **1 FP adicional**, melhorando o recall em **0,7780 ponto percentual**. A alternativa mãe/prenome/peso 1 ganhou **28 TP** sem FP observado, justificando sua presença como alternativa conservadora **exploratória**, não certificada.

## As 3 margens, com contagem exata do log

| Margem | Melhor TP observado | Melhor recall | Selecionáveis com recall ≥95% | Finalistas empíricos recall × FDR |
|---|---:|---:|---:|---:|
| 0,5 | 7.566 | 96,4928% | 41/41 | 4 (incluindo 2 empates da opção mãe/peso 1) |
| 2 | 7.490 | 95,5235% | 8/41 | 3 (incluindo 1 empate) |
| 4 | 7.319 | 93,3427% | 0/41 | 3 (incluindo 1 empate) |

A contagem de fronteira é **derivada apenas das métricas observadas de recall/FDR**, sobre os 41 braços selecionáveis, sem depender do indicador de custo e **não é o parecer operacional da V9**. O arquivo do estudo original usa Pareto `V9_BOOTSTRAP_PARETO_2D_IDEAL_DISTANCE_V2`; a PR #1261 introduz V3 no *código futuro*. Não trocar etiqueta nem reetiquetar a campanha.

## Custo: o que os dados efetivamente dizem

- **0/135** opções+margens têm `EndToEndMeasurements` no estudo histórico, logo **0 valores de P95 fim a fim** e **0 custos computacionais/financeiros por braço** atestados. O CSV escreve `NOT_MEASURED`, nunca 0 ms ou custo zero.
- O frame de candidatos SQL foi o mesmo para todos os braços do único ruleset; logo, esta campanha **não fornece diferença de custo de materialização SQL atribuível a TF**. Igualdade de *frame* **não significa** igualdade de CPU/tempo do scorer TF.
- `SharedSurnameCandidatePairsByOption` registra **zero** para todos os braços nessa medição; isso significa ausência desse evento específico no frame, **não custo zero do sobrenome** ou comparação de esforço por braço.
- Existem diagnósticos de grau U, fanout, tempos SQL e benchmark CPU em outros componentes, mas não são medições emparelhadas de custo físico fim a fim dos **135** modelos. Não misturar sem prover a mesma unidade, infraestrutura e coorte.
- A duração do workflow completo não deve ser dividida por 135 para inferir custo de cada opção. Comparação de preço de SQL Fabric/SQL local também não existe nesses registros.

## Estado de risco e governança

A medição original tem **135/135 `ClusterIidIndependentlyValidated=false`**, todas as 135 opções sem `OperationallyCertified`, `EndToEndMeasurements=[]`, e status **`NO_ELIGIBLE_CONFIGURATION`**. Melhor braço da margem 0,5: FDR observado ~0,01322%; limite CP condicional ~0,8587%, **não certificado** porque a hipótese estatística de dependência entre identidades-base não foi validada. O resultado TEST permaneceu lacrado; não há autorização DT-15 nem uso operacional.

### Consequência prática

A evidência pode agora ser **comparada numericamente nas 135 configurações**, sem esperar download/descompactação do ZIP; entretanto **não podemos escolher a configuração mais barata** com esses dados, e não devemos inventar métricas novas nem V4. A instrumentação de P95 físico que já existe deve ser usada na próxima campanha *somente* após decisão de execução do operador, na mesma referência e para finalistas predeclarados. Comparar uma vez o custo de calibração separadamente do custo operacional por lote.

Arquivo de teste sintético/histórico: `Solution/tests/fixtures/test_v9_tf_001_historical_log_135.py` valida as 135 linhas, os denominadores, a imutabilidade de `NOT_MEASURED` e os resultados dos controles; não valida diretamente o ZIP original.
