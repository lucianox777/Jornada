# GOV-V9-J-COMPAT-20261108 — compatibilidade do plano único J com #1300

**Decisão vinculante do Product Owner:** 08/11/2026, frente J / PR [#1337](https://github.com/lucianox777/Jornada/pull/1337). **Estado:** DECIDIDO; implementação e teste físico são separados da aprovação. **Precedência:** complementa e prevalece sobre qualquer interpretação de “todos os parâmetros devem estar explícitos” que obrigue a reescrever ou recusar planos históricos de #1300. Não revoga a exigência do `plan.json` único, V2 WITNESS+ANCHOR em estudos J que os declarem, nem a validação fail-closed de chaves desconhecidas.

## Contrato de leitura e de compatibilidade

1. **Campo reconhecido e presente no `plan.json` → respeitar o valor declarado no arquivo.** Não sobrescrever por workflow, CLI, `.env` ou valores embutidos de outro estudo.
2. **Campo reconhecido, mas ausente → usar o default do código que processa aquele tipo.** Nunca recusar um plano **somente** porque omitiu parâmetro opcional/novo. Para campos existentes, **proibido alterar defaults** em qualquer versão com efeito sobre planos históricos; exceção exige nova decisão e migração explícita, não ocorre aqui.
3. **Campo desconhecido → recusar**, sem ignorá-lo silenciosamente. Plano malformado, combinações semanticamente incoerentes, valores fora de domínio e evidência ausente obrigatória continuam recusados por motivos próprios; isto não é recusa “por omissão de campo opcional”.
4. **Todo parâmetro novo possui default neutro**, que reproduz exatamente o comportamento de hoje de #1300 sem alterar fit, TP/FP/FN, SHA científico, fronteira ou seleção quando ausente. Nunca converter ausência em valor explícito no arquivo histórico nem alterar seu SHA de bytes.
5. **Relatório por opção:** para cada configuração efetivamente avaliada, identificar campos reconhecidos **`FROM_PLAN`** versus **`CODE_DEFAULT`** e respectivos valores efetivos, distinguindo os defaults novos de opções existentes. O inventário informativo não cria gate de comparação automática, não interfere no P95/LOCK nem altera resultados para encaixar medição.
6. **Regressão:** ler/executar plano histórico sem campos da J e provar o mesmo resultado atual (fits/métricas/digests e semântica de seleção), além de teste com campo desconhecido recusado e teste de campo reconhecido explícito respeitado.

## Defaults neutros autorizados

| Parâmetro novo | Default quando ausente | Interpretação |
|---|---|---|
| `RecallEquivalenceMargin` | `0` | Não introduz tolerância nova de equivalência de recall; preserva o seletor preexistente. |
| `TieBreakOrder` | `[recall, FDR upper, SHA]` | Ordem atual: recall decrescente, bound FDR crescente, SHA estável para desempate; não remove P95 da elegibilidade. |
| `ClusterUnit` | `IDENTITY` | Unidade de agrupamento por identidade-base, não pares IID. |
| `Estimator` | `LABELED` | Estimador com verdade rotulada conforme o experimento, sem segunda inferência oculta. |
| `ManualReviewSampleSize` | `0` | Nenhuma nova etapa de amostragem de revisão manual. |
| `RecalibrationTriggers` | `false` / desligados | Sem gatilhos automáticos de retreino/recalibração. |

**Outros parâmetros já existentes:** preservar rigorosamente seus defaults históricos, incluindo `MaximumParallelRuleSets=1` quando ausente e `0` somente por opt-in explícito (C# / #1311). `MuEstimationMethods` ausente não obriga declarar `Role`, `Anchor` ou `PredeclaredCandidateCpMultiplicity`; planos históricos continuam com a semântica anterior.

## Impactos e limites

A J amplia o motor C# sem motor paralelo/segundo manifesto, preserva V2 executada como testemunha física num plano J quando declarada, e contabiliza múltiplas hipóteses candidatas sem inventar independência entre pares por identidade-base. O formato de análise registra valor e origem, sem alterar o modelo apenas para acrescentar telemetria. Nenhuma campanha científica com seed nova, TEST/Gold, ativação, mudança de P95 ou flexibilização de LOCK é autorizada. **Passo 3/D2 continua cancelado** pela decisão de 08/11.

**Rastreabilidade:** [PR J/#1337](https://github.com/lucianox777/Jornada/pull/1337); [PR #1300](https://github.com/lucianox777/Jornada/pull/1300); [decisão V2 WITNESS/ANCHOR](Decisao_V9_Multiparametro_V2_Testemunha_Permanente_20261107.md); [decisão P95/LOCK/D2 de 08/11](Decisao_V9_Decisoes_Vigentes_P95_LOCK_Cancelamento_D2_20261108.md) (a última está na PR documental #1318 até integração); [índice vigente](Indice_Decisoes_Vigentes.md); GOV-DOC-01. A integração na master fica condicionada aos gates técnicos e à fila sequencial G→F→campanha→J.
