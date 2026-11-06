# V9 — Calibração direta da V3 parametrizada (05/11/2026)

**Status:** implementação de preparação de plano; **não** é resultado físico, certificação ou ativação. A V3 CLASSWISE_SUPPORT_BACKOFF é a única família M/U nesta rota. A V2 não é controle obrigatório e não fornece parâmetros ao novo fit.

## Motivação e implementação

Diferenças entre campanhas históricas de M/U, TF, prior, blocking e partições tornam comparações de scores e TP entre snapshots distintos insuficientes para decidir a política corrente. Esta rota usa o **Calibrador C# único já existente**, preservando o mesmo cálculo de score, Rank/Resolve, limites, gates e persistência versionada. Não modifica o scorer FS, o estimador V3, o cálculo automático de prior, Gold, REF, CI ou TEST.

O produtor scripts/v9-v3-direct-plan.py recebe **um novo plano TRAIN completo** e **uma grade explícita V3**. Ele emite um **novo plano V3-only** que V9MuEstimationSearchSpace já suporta, com MuEstimationMethods contendo somente CLASSWISE_SUPPORT_BACKOFF e os quatro parâmetros:

- ClassMassCoverage: fração de massa das classes com núcleo conjunto.
- MinimumObservedMPerCell e MinimumObservedUPerCell: suporte efetivamente observado por célula e classe (não pseudocontagens).
- RetainSupportedTail: preserva contribuição conjunta das células da cauda com suporte suficiente, quando true.

Os **arrays são fornecidos pelo estudo**, sem valor fixo intrínseco à V3. O C# enumera o produto cartesiano declarado e exige orçamento computacional suficiente. O prior continua **calculado automaticamente** no TRAIN pelo Calibrador, não um parâmetro selecionável. As famílias de TF, alpha, threshold, margem e blocking permanecem do plano-base; a rota não as congela a valores de snapshots históricos.

Para opções com TF **efetivamente ativa**, o novo plano registra explicitamente UnpublishedTermPolicy=1 (AbstainCandidate), evitando que o campo omitido aceite silenciosamente o default RejectOperation=0. Se o plano-base declarar RejectOperation para uma opção TF ativa, a preparação falha; TF0 não é alterado. O plano-base permanece intocado.

## Exemplo de preparação e execução

Criar previamente novo corpus sintético/manifesto e plano do **produtor vigente**, com referência IBGE publicada pinada, em ambiente DEV. Exemplo assumindo insumos já obtidos e verificados:

~~~bash
cd Solution
python3 scripts/v9-nested-calibration-study.py \
  .local/v9/observacoes.csv .local/v9/v3-train-base.json \
  --tf-search --pinned-ibge .local/v9/pinned-ibge.json
~~~

Grade **ilustrativa** de valores a declarar antes de um estudo. Não são valores vencedores nem prescrições:

~~~json
{
  "ClassMassCoverage": [0.95, 0.99],
  "MinimumObservedMPerCell": [3, 5],
  "MinimumObservedUPerCell": [3, 5],
  "RetainSupportedTail": [true]
}
~~~

Salvá-la em .local/v9/v3-grid.json. Essa grade produz oito variantes M/U. Com 41 opções TF, duas margens e dois rulesets iniciais, o plano começa com **1.312 comparações**. O orçamento original de 512 seria insuficiente; ampliá-lo **explicitamente** sem truncamento:

~~~bash
python3 scripts/v9-v3-direct-plan.py \
  .local/v9/v3-train-base.json .local/v9/v3-grid.json \
  .local/v9/v3-train-plan.json --maximum-configurations 1400
~~~

A preparação produz fingerprint novo, verifica valores duplicados, números não finitos, TF e capacidade inicial, e recusa sobrescrever evidência.

**Execução física separada e explícita**, somente em SQL descartável JornadaHarness, corpus/preflight pinados e ambiente autorizado:

~~~bash
dotnet run --project src/Jornada.Linkage.Parameters.Worker -- \
  --v9-calibrate .local/v9/v3-train-plan.json \
  .local/v9/observacoes.csv .local/v9/v3-train-results-new
~~~

Registrar SHA do código, corpus, plano, política, snapshots e relatórios emitidos, métricas por fonte, incerteza, suportes das 81 células, elegibilidade e razões de rejeição. Avaliação independente **da configuração V3 selecionada**, Gold incremental, TEST novo quando aplicável e DT-15 continuam como gates, mas **não exigem comparação causal com a V2**.

## Limites

- Somente plano e testes offline nesta PR; **nenhum treinamento, SQL físico, nova seed, REF, Gold, TEST ou ativação** foi executado.
- Não converter a análise histórica #1289 em pré-requisito de calibração V3; ela pode permanecer como registro técnico independente.
- A auditoria #1290 do prior só deve bloquear uso quando demonstrar erro no frame corrente. A V3 não deve copiar o prior de um snapshot antigo.
- Não alterar modelos ativos nem integrar a PR sem CI e gates aplicáveis no SHA final.
