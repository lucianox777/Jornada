# Jornada V9 — estudo opt-in de regularização U nas células raras (06/11/2026)

**Estado:** implementação matemática + testes unitários e de montagem do plano; **não** é execução física, escolha de parâmetros, certificação estatística, proposta de ativação nem substituição da V2. Frente independente das PRs #1298 e #1299, sem stacking.

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

Na especificação `MuEstimationMethods[].Parameters` de `CLASSWISE_SUPPORT_BACKOFF`, o novo eixo é **opcional**:

```json
{
  "Method": "CLASSWISE_SUPPORT_BACKOFF",
  "Parameters": {
    "ClassMassCoverage": [0.99],
    "MinimumObservedMPerCell": [5],
    "MinimumObservedUPerCell": [5],
    "RetainSupportedTail": [true],
    "UBackoffJointMixtureWeights": [0, 0.25, 0.5, 1]
  }
}
```

- Se **ausente**, cálculo, versão, `OptionId`, serialização e conteúdo histórico de V3 permanecem os originais (compatibilidade exata).
- Se presente, os valores explícitos são finitos, únicos e entre 0 e 1; eles multiplicam o orçamento declarado, sem truncar grade.
- `lambda=0` é a **testemunha de paridade numérica** da V3, mas tem `OptionId`/proveniência experimental exclusivos; não duplicar essa opção com uma V3 legado na mesma comparação sem contabilizar multiplicidade.
- Se presente, a proveniência do snapshot usa `V9_CLASSWISE_U_RARE_MIXED_JOINT_BACKOFF_V1`, o `FallbackAssumption` e `MethodU` específicos. O replay recalcula exatamente todas as probabilidades e recusa metadata adulterada.
- Este parâmetro é **U**, não margem de decisão e não TF. M, prior, regras de blocking, referências fixadas, seleção de FDR 1% e isolamento de TEST/Gold permanecem inalterados.

## Gate físico ainda exigido

**Não executar automaticamente a campanha científica confirmatória.** Para testar esta hipótese, congelar antes um `plan.json` pequeno, no mesmo TRAIN, corpus, seed42/43/57 e snapshots de referência, comparar os quatro pesos em TF0 e ONE_MF com 3 passes e 19 passes; margem operacionalmente elegível `0.5` fixa. Usar o próprio Calibrador SQL descartável e guardar hashes do plano, pares, fit, 81 células e resultados. Comparar o efeito no melhor e no segundo candidato, alterações de ranking/agrupamento e conflitos; não selecionar `lambda` pelos dados de TEST/Gold nem mascarar a regressão com margem zero.

Condições de aceitação: testes C# e Python completos em HEAD exato, reexecução física fechando TP/FP/identidades base, métricas independentes de fusão/fragmentação, fanout/custo e controle de FDR dependente por identidade-base. Sem melhora inequívoca e validação independente, manter V2 como referência e V3 desativada. A autorização do usuário para *merge de código validado* não autoriza DT-15 nem ativação operacional.
