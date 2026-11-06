# Jornada V9 — estudo opt-in de regularização U nas células raras (06/11/2026)

**Estado:** implementação de três estimadores explícitos (V2, V3 histórico e V3 com mistura U) + testes unitários e de montagem do plano; **não** é execução física, escolha de parâmetros, certificação estatística, proposta de ativação nem substituição da V2. Frente independente das PRs #1298 e #1299, sem stacking.

## Hipótese pré-declarada

A V3 original estima U de células fora do suporte conjunto usando o produto das duas marginais condicionais do TRAIN. No experimento físico pareado da #1299, TF0/3 passes perdeu 469 resoluções corretas da V2 e recuperou 17 (saldo -452), com 450 mudanças do melhor candidato; todas as 469 perdas envolveram células com fallback U, mas 6.248 resoluções preservadas também envolveram fallback. A associação **não prova** efeito causal isolado de U.

Propomos um **único novo eixo experimental**: a fração `lambda = UBackoffJointMixtureWeight` da massa rara U que vem do estimador conjunto Dirichlet do mesmo TRAIN, em lugar do fallback por produto de marginais.

Para toda célula U com suporte conjunto preservado: `U_new(c) = U_V3(c)` (invariável).

Para as células U raras, fora do conjunto preservado:

```text
U_product(c) = R * P_person(c) * P_mother(c) / sum_rare(P_person * P_mother)
U_joint(c)   = (observed_U(c) + alpha) / (total_U + 81*alpha)
U_lambda(c)  = (1-lambda)*U_product(c) + lambda*U_joint(c)
```

Aqui `R = sum_rare(U_joint(c))`, calculado uma só vez a partir do mesmo TRAIN. Os dois termos somam **a mesma massa residual R**, de modo que a combinação convexa conserva U normalizado, M inteiro e probabilidade positiva em todas as 81 células, sem truncar probabilidades isoladas. A hipótese não pressupõe independência completa nas células sem suporte; `lambda=1` usa U conjunto Dirichlet nessas células, mas **não transforma toda a V3 na V2** porque M e as outras regras da V3 permanecem.

## Contracto no `plan.json`

Na especificação `MuEstimationMethods` do `plan.json`, declarar **três famílias** no mesmo array: `DEFAULT` (V2), `CLASSWISE_SUPPORT_BACKOFF` (V3 histórico sem peso) e `CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE` (V3 experimental com array explícito). A sintaxe anterior com pesos no método `CLASSWISE_SUPPORT_BACKOFF` isolado ainda é aceita para compatibilidade, mas os dois formatos misturados na mesma grade são recusados:

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
    },
    {
      "Method": "CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE",
      "Parameters": {
        "ClassMassCoverage": [0.99],
        "MinimumObservedMPerCell": [5],
        "MinimumObservedUPerCell": [5],
        "RetainSupportedTail": [true],
        "UBackoffJointMixtureWeights": [0, 0.25, 0.5, 1]
      }
    }
  ]
}
```

- Se **ausente**, cálculo, versão, `OptionId`, serialização e conteúdo histórico de V3 permanecem os originais (compatibilidade exata).
- Se presente, os valores explícitos são finitos, únicos e entre 0 e 1; eles multiplicam o orçamento declarado, sem truncar grade.
- `lambda=0` é a **testemunha de paridade numérica** da V3, mas tem `OptionId`/proveniência experimental exclusivos. Quando V3 padrão e `lambda=0` coexistirem, contar os **dois braços** no orçamento e na multiplicidade, sem interpretá-los como resultados independentes.
- Se presente, a proveniência do snapshot usa `V9_CLASSWISE_U_RARE_MIXED_JOINT_BACKOFF_V1`, o `FallbackAssumption` e `MethodU` específicos. O replay recalcula exatamente todas as probabilidades e recusa metadata adulterada.
- Este parâmetro é **U**, não margem de decisão e não TF. M, prior, regras de blocking, referências fixadas, seleção de FDR 1% e isolamento de TEST/Gold permanecem inalterados.

O plano congelado de comparação controlada contém **24 avaliações declaradas**: (1 V2 + 1 V3 padrão + 4 alternativas V3 U misto) × (TF0, ONE_MF) × (3, 19 passes), margem 0,5. As versões e os IDs da V2 e da V3 padrão permanecem originais; o novo método tem identificadores distintos. Os três são candidatos de avaliação TRAIN, **não** três modelos operacionais ativos.


## Otimização de custo físico para as três famílias

Na busca aninhada, os braços `DEFAULT`, `CLASSWISE_SUPPORT_BACKOFF` e
`CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE` compartilham **a mesma materialização
de candidatos SQL**, dentro do mesmo corpus TRAIN, conjunto de passes de
blocking e margem declarada. O coordenador forma grupos pela identidade do
frame (passes + margem), e não pelo estimador M/U. Cada opção continua sendo
ajustada, pontuada, auditada e identificada **independentemente**, usando seu
próprio `OptionId`, fingerprint e snapshot; nenhum score ou desfecho é
copiado da V2 ou da V3 para outro método. Se há mais de 64 opções, o lote
é dividido para respeitar o limite de modelos do executor.

Para a grade congelada de 24 avaliações (2 rule sets, 1 margem,
2 braços TF, 6 variantes de M/U), a organização dos lotes prevê
**uma materialização inicial por conjunto de passes**, em vez de seis
materializações iniciais por conjunto de passes, sem contar replays e
medidas posteriores executadas por opção. Este é um ganho potencial
em leituras/joins SQL, **não** uma afirmação de redução de tempo medida.
É obrigatório conferir SHA dos candidatos e métricas individuais no
experimento físico; sem evidência pareada, não reportar velocidade obtida
nem equivalência inferencial de `lambda=0`.

A grade, orçamento de FDR e multiplicidade estatística permanecem com as
24 avaliações; compartilhar o SQL não reduz o número de hipóteses.


### Paralelismo por percentual de vCPU (opt-in)

O contrato de `V9AdaptiveCalibrationPlan` agora aceita
`ParallelCpuPercent` **opcional** dentro de `Calibration`.
O `MaximumParallelRuleSets` existente permanece **teto rígido**,
inclusive quando o percentual permite mais workers. Exemplo
(somente fragmento do `plan.json`):

```json
{
  "Calibration": {
    "MaximumParallelRuleSets": 8,
    "ParallelCpuPercent": 80
  }
}
```

O cálculo usa `Environment.ProcessorCount` da instância **na execução**
(vCPUs lógicas visíveis ao processo, não os núcleos físicos do servidor).
O limite efetivo é `min(MaximumParallelRuleSets, max(1, floor(CPUs * percentual / 100)))`.
Assim, numa instância com 1/2/4/8/16 vCPUs visíveis e teto 8,
o limite será 1/1/3/6/8, respectivamente. O número de grupos
independentes também limita a concorrência. Percentual ausente mantém
**exatamente** o comportamento fixo anterior e não altera hashes/JSON
dos planos históricos; 0 e valores >100 falham antes de abrir SQL.

O limite é de *concorrência*, **não de uso efetivo da CPU**: o SQL, a RAM,
as operações de rede e o custo de cada algoritmo podem dominar.
O percentual não garante segurança contra OOM; executar com teto
conservador e só elevar após verificar uso de memória e tempos reais.
Cada worker usa conexão SQL independente; a composição de evidências,
a quantidade de modelos testados e o limite FDR são invariáveis.
A busca aninhada, a busca adaptativa e a validação de finalistas
compartilham este mesmo resolvedor, sem exigir GPU ou shards.

## Gate físico ainda exigido

**Não executar automaticamente a campanha científica confirmatória.** Para testar esta hipótese, congelar antes um `plan.json` pequeno, no mesmo TRAIN, corpus, seed42/43/57 e snapshots de referência, comparar os quatro pesos em TF0 e ONE_MF com 3 passes e 19 passes; margem operacionalmente elegível `0.5` fixa. Usar o próprio Calibrador SQL descartável e guardar hashes do plano, pares, fit, 81 células e resultados. Comparar o efeito no melhor e no segundo candidato, alterações de ranking/agrupamento e conflitos; não selecionar `lambda` pelos dados de TEST/Gold nem mascarar a regressão com margem zero.

Condições de aceitação: testes C# e Python completos em HEAD exato, reexecução física fechando TP/FP/identidades base, métricas independentes de fusão/fragmentação, fanout/custo e controle de FDR dependente por identidade-base. Sem melhora inequívoca e validação independente, manter V2 como referência e V3 desativada. A autorização do usuário para *merge de código validado* não autoriza DT-15 nem ativação operacional.
