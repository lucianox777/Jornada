# V9 — teste pareado da hipótese de dependência entre sobrenomes da pessoa e da mãe

**V3, M/U=CLASSWISE_SUPPORT_BACKOFF, sem comparar com V2, sem alterar as 81 células.**

Duas hipóteses de **TF de último sobrenome** são declaradas simultaneamente em `plan.json` e avaliadas com os **mesmos candidatos SQL, parâmetros M/U, referência IBGE, threshold, margem e pesos**:

| `OptionId` no `Calibration.ModelOptions` | `SharedSurnameMode` | Significado e elegibilidade |
| --- | --- | --- |
| `TF_TWO_PL_ML_W1_M0_A0.5_T0.95` | `0` (`Bilateral`) | Dois ajustes TF, um para o último sobrenome da pessoa e outro para o último sobrenome da mãe. **Elegível** para seleção normal do Calibrador. |
| `TF_SHARED_PL_ML_W1_M0_A0.5_T0.95` | `1` (`PersonSideOnlyWhenShared`) | Na coincidência do mesmo sobrenome significativo em pessoa/mãe, usar **um ajuste conservador assinado** já implementado pelo scorer, não somar os dois. **Diagnóstico somente**, inelegível para Pareto, refinamento/finalistas e publicação. |

O nome `Bilateral` representa a hipótese operacional de **contribuições separadas**, não demonstra independência real entre sobrenomes. O modo `1` representa a **hipótese conservadora de dependência/compartilhamento**, não um modelo probabilístico conjunto novo nem uma prova de vínculo familiar.

Ambas usam apenas os slots `Person.LastContentSurname` e `Mother.LastContentSurname` com `Weight=1`, `Mode=0` (simétrico); prenomes têm peso zero. A única diferença entre as opções é `SharedSurnameMode`, o identificador e a elegibilidade diagnóstica. Sem prior fixo: o C# recalcula prior e M/U do TRAIN e congela no fitted de cada hipótese. A opção compartilhada permanece diagnóstica por política vigente; testar **não** autoriza seleção operacional.

## Como escrever o `plan.json` com as DUAS opções

Não reutilizar o `plan.json` histórico V2. Gerar o plano de TRAIN já pinado a corpus e IBGE realmente existentes:

```bash
cd Solution
python3 scripts/v9-nested-calibration-study.py \
  .local/v9/observacoes.csv \
  .local/v9/source-tf-plan.json \
  --tf-search --pinned-ibge .local/v9/pinned-ibge.json

# O arquivo grid.json contém somente M/U V3, conforme o projeto #1291:
# ClassMassCoverage, MinimumObservedMPerCell, MinimumObservedUPerCell,
# RetainSupportedTail.
python3 scripts/v9-v3-direct-plan.py \
  .local/v9/source-tf-plan.json \
  .local/v9/v3-grid.json \
  .local/v9/plan.json \
  --compare-surname-dependence \
  --initial-only --maximum-configurations 64

dotnet run --project src/Jornada.Linkage.Parameters.Worker -- \
  --v9-calibrate .local/v9/plan.json \
  .local/v9/observacoes.csv .local/v9/v3-surname-pair-results
```

**Orçamento de exemplo:** para a grade V3 da PR #1291 de **8 variantes M/U**, duas hipóteses de sobrenome × duas margens × dois rulesets iniciais = **64 avaliações iniciais**. Ajustar `--maximum-configurations` quando a grade declarada mudar; o script falha se o orçamento não cobrir os dois braços. O `--initial-only` garante que o estudo físico limite-se à comparação pré-declarada (sem alegar busca adaptativa ótima). O argumento `--only-option-id` é incompatível com `--compare-surname-dependence`, pois eliminaria uma das hipóteses.

O plano final contém `Calibration.ModelOptions` com **exatamente dois objetos**; um tem `"SharedSurnameMode": 0, "EligibleForSelection": true`, e o outro `"SharedSurnameMode": 1, "EligibleForSelection": false`. Todos os demais parâmetros relevantes são idênticos. Ambas usam `UnpublishedTermPolicy=1` (abstenção quando TF sem suporte publicado), e o plano mantém todos os pins IBGE/CSV e fingerprint novo.

## Resultado estatístico esperado

O mesmo `mu-statistical-analysis.json` do Calibrador C# inclui **todas as avaliações**, também aquelas inelegíveis, e o campo adicional `surnameDependenceComparisons`: SHA do frame de pares, número de pares com sobrenome mãe/pessoa compartilhado, parâmetros M/U e margem, recall, FDR, falsos aceitos, pendências e respectivos deltas `compartilhado − bilateral`. Somente há comparação quantitativa pareada se os dois braços foram efetivamente medidos no **mesmo SQL candidate frame** (SHA idêntico), mesmo blocking, mesmo M/U, margem e lote. Pares ausentes ou inconsistentes não produzem uma melhora fictícia.

O resultado também informa `sharedSurnameAssumptionChosenForOperation=false`. Nenhum modelo novo é publicado. O bundle de evidências da PR #1293 continua usando os mesmos arquivos da V2 e poderá anexar o relatório gerado após a calibração real.

### Cuidados científicos
- Se `SharedSurnameCandidatePairs=0`, **não há poder de distinguir as hipóteses** nesse frame: deltas observados iguais a zero não demonstram independência.
- O ajuste conservador existente usa um único termo assinado quando o sobrenome normalizado é compartilhado; **não** equivale à estimação de uma distribuição conjunta mãe×filho a partir do IBGE, que não é publicada.
- `TF0` pode continuar como controle em estudos gerais, mas esta experiência isola apenas **o parâmetro único de dependência/compartilhamento**, mantendo os demais fixos.
- Protocolo TRAIN apenas, sem TEST e sem certificação DT-15 ou autorização de ativação.
