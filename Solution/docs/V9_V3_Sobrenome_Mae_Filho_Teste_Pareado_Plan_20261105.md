# V9 — dependência de sobrenomes definida no `plan.json` (05/11/2026)

**Contrato:** a decisão de comparar sobrenomes da pessoa e da mãe é parte dos dados do plano, **não de um parâmetro de linha de comando**. O Calibrador C# V3 lê `Calibration.ModelOptions` de `plan.json`. Não existe mais `--compare-surname-dependence`.

## Declaração das hipóteses no plano

O arquivo entregue ao `--v9-calibrate` contém duas opções de modelo com os mesmos pesos, `SmoothingAlpha`, `DecisionThreshold`, `UnitTfOptions`, mesmo M/U e mesma referência IBGE. Só mudam o ID, `SharedSurnameMode` e a elegibilidade:

```json
{
  "Calibration": {
    "ModelOptions": [
      {
        "OptionId": "TF_TWO_PL_ML_W1_M0_A0.5_T0.95",
        "SmoothingAlpha": 0.5,
        "DecisionThreshold": 0.95,
        "TfWeight": 0,
        "SharedSurnameMode": 0,
        "EligibleForSelection": true,
        "UnpublishedTermPolicy": 1,
        "UnitTfOptions": [
          { "Side": 0, "Unit": 2, "Weight": 0, "Mode": 0 },
          { "Side": 0, "Unit": 3, "Weight": 1, "Mode": 0 },
          { "Side": 1, "Unit": 2, "Weight": 0, "Mode": 0 },
          { "Side": 1, "Unit": 3, "Weight": 1, "Mode": 0 }
        ]
      },
      {
        "OptionId": "TF_SHARED_PL_ML_W1_M0_A0.5_T0.95",
        "SmoothingAlpha": 0.5,
        "DecisionThreshold": 0.95,
        "TfWeight": 0,
        "SharedSurnameMode": 1,
        "EligibleForSelection": false,
        "UnpublishedTermPolicy": 1,
        "UnitTfOptions": [
          { "Side": 0, "Unit": 2, "Weight": 0, "Mode": 0 },
          { "Side": 0, "Unit": 3, "Weight": 1, "Mode": 0 },
          { "Side": 1, "Unit": 2, "Weight": 0, "Mode": 0 },
          { "Side": 1, "Unit": 3, "Weight": 1, "Mode": 0 }
        ]
      }
    ]
  },
  "MuEstimationMethods": [
    {
      "Method": "CLASSWISE_SUPPORT_BACKOFF",
      "Parameters": {
        "ClassMassCoverage": [0.99],
        "MinimumObservedMPerCell": [5],
        "MinimumObservedUPerCell": [5],
        "RetainSupportedTail": [true]
      }
    }
  ]
}
```

**Fragmento:** copiar somente estas propriedades para um plano TRAIN **completo** criado pelo produtor vigente, preservando todos os outros campos obrigatórios, os pins IBGE/CSV e seus hashes. Não substituir um plano completo apenas pelo exemplo acima. O builder V3 verifica que o par foi declarado no próprio JSON e falha fechado em caso de divergência dos pesos, opção ausente, configuração compartilhada elegível ou tentativa de reduzir uma comparação a um único braço com `--only-option-id`.

- `SharedSurnameMode=0`: hipótese de dois termos TF bilaterais, calculados separadamente; **não prova independência probabilística verdadeira**.
- `SharedSurnameMode=1`: hipótese conservadora de um termo quando o sobrenome é compartilhado; **somente diagnóstico**, fora do Pareto e dos finalistas até decisão posterior de governança.

## Duas avaliações por combinação física

Cada variante da grade M/U, ruleset de blocking e margem avalia **ambas** as opções do JSON no mesmo TRAIN e no mesmo frame de candidatos SQL. Isso produz duas medições (não dois treinamentos independentes ou duas seeds). Por exemplo, **1 variante M/U × 2 modos TF × 1 margem × 2 rulesets = 4 avaliações**. Com 8 variantes M/U e duas margens, 2 rulesets, são 64 avaliações iniciais.

O relatório `mu-statistical-analysis.json` lista todas as opções — inclusive a diagnóstica — e traz `surnameDependenceComparisons` com `candidatePairsSha256`, pares de sobrenome compartilhado, recall, FDR, falsos vínculos aceitos, pendências e diferenças pareadas (dependente − bilateral). A comparação exige igualdade de SHA do frame, blocking, M/U, margem e lote. Zero pares afetados é sinalizado como **não informativo**, não como evidência de equivalência. `sharedSurnameAssumptionChosenForOperation=false` permanece explícito.

## Produção e execução

1. Gerar o plano TRAIN com o produtor existente (incluindo o corpus e referência IBGE pinada).
2. No JSON de entrada, manter somente as duas opções explicitadas acima em `Calibration.ModelOptions`. O workflow `v9-v3-surname-dependence-plan.yml` mostra a transformação de autoria para preparar o JSON de entrada a partir do catálogo TF gerado. **Nenhuma flag de sobrenome é passada ao builder**.
3. Converter o plano-base para V3 com sua grade M/U explícita, preservando as duas opções JSON, e executar o Calibrador C# único:

```bash
cd Solution
python3 scripts/v9-v3-direct-plan.py \
  .local/v9/source-plan.json .local/v9/v3-grid.json .local/v9/plan.json \
  --initial-only --maximum-configurations 64
dotnet run --project src/Jornada.Linkage.Parameters.Worker -- \
  --v9-calibrate .local/v9/plan.json \
  .local/v9/observacoes.csv .local/v9/v3-surname-pair-results
```

O parâmetro `--initial-only` limita apenas a pesquisa inicial, não seleciona o modo de sobrenome. A escolha de quais hipóteses são medidas permanece **exclusivamente no JSON**. Não há baseline V2 obrigatório, alteração das 81 células, TEST, Gold, REF, publicação ou ativação automática. Resultados TRAIN não certificam FDR nem autorizam DT-15.
