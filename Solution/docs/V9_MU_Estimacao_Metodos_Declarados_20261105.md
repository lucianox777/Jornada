# V9 — busca M/U declarada no plan.json (05/11/2026)

Contrato proposto/executável no fluxo **`--v9-calibrate`**: `MuEstimationMethods` no objeto-raiz de `V9NestedCalibrationPlan`. Cada `Method` tem seus próprios `Parameters`, cada parâmetro uma **lista de valores explícitos**. A ausência de `MuEstimationMethods` preserva os planos históricos e o comportamento anterior; NÃO modifica os arquivos de evidência congelados.

## Configuração de regressão V2 × V3 histórica (sem retuning)

```json
{
  "MuEstimationMethods": [
    { "Method": "DEFAULT" },
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

A comparação exige exatamente **duas variantes M/U por configuração-base**. A segunda reproduz os valores usados no experimento V3 (fonte: `Solution/data/reference/v9-tf-frozen-joint-trial-20261105/historical-v3-support-policy.json`, SHA da política `3833d26a21691f2804fa9c52201a9c5e6f3603dc151d871453218debfc3dd319`). `true` mapeia para `RETAIN_CLASS_JOINT_WHEN_COUNT_AT_LEAST_K_V2`; `false`, quando explicitamente declarado em ensaio futuro, significa a variante histórica de fallback em toda a cauda. Não executar `false` na comparação histórica V3.

`DEFAULT` corresponde a `JointSupportPolicy=null` e estima diretamente todas as 81 células M/U com suavização original (schema do fit V2). `CLASSWISE_SUPPORT_BACKOFF` utiliza `V9JointSupportEstimation` (schema do fit V3), o núcleo separado por classe e as marginais normalizadas na cauda de baixo suporte. Os mesmos alfa, TF, passes de blocking, prior, threshold e margem das opções-base são preservados; não redefinir esses parâmetros pelo seletor M/U. Para reproduzir a comparação física congelada de 05/11, a configuração-base deve continuar sendo ONE_MF peso 1, alpha 0,5, threshold 0,95, margem 0,5 e 3 passes. Uma nova calibração produz novos SHAs e **não** substitui os snapshots imutáveis históricos.

Os campos legados `ClassMassCoverage`, `MinimumObservedMPerCell`, `MinimumObservedUPerCell` e `SupportedTailRule` na raiz são conservados por compatibilidade dos planos anteriores. Com `MuEstimationMethods` presente, os valores operacionais são **somente** os arrays aninhados; os escalares legados não participam da variação. Um futuro formato versionado poderá eliminá-los.

## Execução, governança e evidência

- O Calibrador enumera o produto cartesiano explícito sem criar valores intermediários, intervalos implícitos ou priorizar métodos; ausência de método conhecido ou duplicações são recusadas.
- Cada combinação M/U recebe `OptionId` e fingerprint próprios, integra a contagem de hipóteses e deve passar os gates existentes. Se o conjunto inicial exceder o orçamento, aborta; não trunca a enumeração declarada.
- A saída `mu-statistical-analysis.json` e `mu-statistical-analysis.md` relaciona método, subparâmetros, threshold/margem, snapshots, TP, FP, recall, FDR, conflitos, motivos de inelegibilidade e status. Inclui resultados de todos os lotes, não apenas finalistas.
- A seleção ocorre sobre TRAIN_SELECTION, sem antecipação de VALIDATION/TEST. Confirmação independente, FDR por identidade, integração Gold e autorização DT-15 continuam separadas. A geração do relatório não comprova melhoria de acurácia ou certificação.
- Um terceiro método poderá acrescentar seu próprio contrato versionado em `Parameters`; por segurança, nomes não implementados falham fechado. Intervalos `RANGE` **não** são reconhecidos nesta versão.
