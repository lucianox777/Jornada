# LOCK científico V9 — manifestação PROPOSTA (09/11/2026)

**ESTADO: DRAFT — AGUARDA APROVAÇÃO DO PO. NÃO EXECUTAR, NÃO MESCLAR, NÃO CONSUMIR SEED.**

Esta PR não altera o Calibrador, o `plan.json` oficial, o corpus, o FDR vigente, a seleção do modelo nem a ativação. O arquivo `plan.json` nesta pasta é **rascunho não executável, com schema de proposta deliberadamente incompatível com o Calibrador**; não deve ser promovido nem passado ao CLI sem nova decisão, adaptação de schema, testes e LOCK assinado.

## Escopo pré-declarado, ainda sem aprovação

| Papel | Motor | Braço | Mistura U rara λ | Margem | Passes | Seleção |
|---|---|---|---:|---:|---:|---|
| Candidata A | V3 | TF0 | 0,25 | 0,5 | 3 | Elegível **somente após LOCK** |
| Candidata B | V3 | ONE_MF | 0,5 | 0,5 | 3 | Elegível **somente após LOCK** |
| Testemunha | V2 | WITNESS | histórica | histórica | histórico | Não elegível |
| Âncora | V2 | ANCHOR | histórica | histórica | histórico | Não elegível |
| Testemunha | V3 | ONE_MF λ=0 | 0 | 0,5 | 3 | Não elegível |

**Origem do desenho:** campanha TRAIN físico [run 42343493885](https://github.com/lucianox777/Jornada/actions/runs/42343493885), [relatório](../../V9_V3_U_Rare_Mixture_Experiment_20261106.md). Na margem 0,5 e três passes, TF0 teve TP V2 6758 / V3 original 6306 / λ=.25 6771 / λ=.5 6758. ONE_MF teve TP V2 6702 / V3 original 6771 / λ=.25 6715 / λ=.5 6707. Nenhum λ comum satisfez simultaneamente critérios primários; os braços acima são **hipóteses, não vencedores**. Só evidência TRAIN, não TEST nem certificação.

## Plano de comparação

1. **Seed nova**: reservada, identificador/fingerprint apenas quando o PO autorizar LOCK. Proibido consumir qualquer seed de TEST agora.
2. **Corpus independente**: mesma distribuição-alvo justificada e mesmo recorte V2/V3/TF0/ONE_MF, estratificado por identidade-base e prior compatível. Dados apenas sintéticos na preparação. Evitar sobreposição TRAIN/TEST.
3. **Unidade de pareamento**: identidade-base; pares da mesma identidade são **dependentes**. As duas candidatas observadas no mesmo corpus são **dependentes entre si**. Mesmo gabarito e mesma unidade por braço; não fazer inferência IID por pares.
4. **Sem refit adaptativo** após olhar o resultado confirmatório. Todas as exclusões, abstenções IBGE, thresholds e elegibilidade são pré-especificados no LOCK. TEST cego somente com autorização posterior e sem vazar resultados à fase de desenho.
5. **Critérios propostos (não aprovados):** FDR observado ≤ 1%; limite superior simultâneo a 95% ≤ 1% **somente por método com pressupostos verificados**; recall da candidata ≥ recall da V2 − margem de equivalência que o PO ainda definirá. Ajuste de multiplicidade das **duas candidatas**; witnesses não integram a família de hipóteses elegíveis.
6. **Desempate proposto:** primeiro elegibilidade FDR+recall+P95/contrato, depois melhor recall, menor limite superior FDR, menor custo/latência e SHA estável. Depende de aceite do PO e compatibilidade com a ordem histórica; não modificar default existente sem aprovação.
7. **Parar sem inferência conclusiva** se pressupostos para limite superior não puderem ser sustentados; informar FAIL/NÃO COBERTO por braço e pedir decisão PO. Proibir substituir o teto 1% por meta mais fácil silenciosamente.

## Quadro obrigatório de viabilidade estatística

| Questão | Situação / alternativa de desenho | Status |
|---|---|---|
| Unidade inferencial | Identidade-base ou bloco independente definido *a priori*; múltiplos pares por cluster e entre candidatas compartilham erros; **não IID entre pares** | Pressuposto a validar |
| Método de risco | Propor limite superior de FDR por reamostragem/cluster ou desenho amostral probabilístico válido, com cobertura simultânea; Bonferroni em duas candidatas como referência conservadora; relatar IC e hipóteses | **NÃO VALIDADO** |
| Clopper–Pearson | CP95 só é válido para Bernoulli independentes na unidade relevante; `ClusterIidIndependentlyValidated=false` nos relatórios, logo **não** autoriza certificado 1% | **INADEQUADO SEM PROVA** |
| Dimensão amostral | No cenário **idealizado IID Bernoulli, zero erros** e dois limites simultâneos 95% com Bonferroni (α/2=0,025), são necessárias ao menos **368 observações efetivamente independentes por candidata** apenas para que o bound binomial superior fique ≤1%; não é tamanho garantido para clusters ou FDR de pares correlacionados | **PISO TEÓRICO, NÃO AMOSTRAGEM APROVADA** |
| Resultados anteriores | Bound Hoeffding histórico próximo de **5,7%** excede alvo 1%. Não extrapolar para impossibilidade universal, mas também não afirmar viabilidade sem redesenho/amostra | **LACUNA ABERTA** |
| Revisão manual | Somente como hipótese de futura auditoria cega de registros selecionados probabilisticamente, com pesos, adjudicação dupla e correção do viés de verificação; não acessar dados reais agora | **PO DEVE DECIDIR** |
| Sem independência demonstrável | Não declarar certificação, ampliar amostra mediante protocolo novo ou aceitar status NÃO COBERTO sob decisão expressa do PO; **não aumentar teto** por conveniência | **AGUARDA PO** |

A fórmula idealizada do piso é `n >= ceil(log(0.025)/log(0.99))`, com duas candidatas, zero falsos e independência efetiva. Não deduzir desse cálculo potência suficiente para comparar recall, cobertura FDR sob dependência, taxa de aceitação ou erro de rotulagem.

## Parâmetros adicionais: alternativas não ativadas

| Campo | Vigor neutro histórico | Alternativa PROPOSTA, não aprovada | Justificativa e decisão pendente |
|---|---|---|---|
| `RecallEquivalenceMargin` | `0` | `0` conservador ou `δ>0` pré-fixado | Trade-off de recall, **δ exige PO** |
| `TieBreakOrder` | recall, FDR upper, SHA | elegibilidade FDR/recall/P95, recall, bound, latência, SHA | Desempate estável; compatibilizar antes de ativar |
| `ClusterUnit` | `IDENTITY` | `IDENTITY` ou bloco externo com independência validada | Não contar pares duplicados |
| `Estimator` | `LABELED` | estimador cluster-robusto/draw bootstrap sob desenho verificado | CP de pares não sustenta bound geral |
| `ManualReviewSampleSize` | `0` | tamanho definido por poder, cluster e precisão desejada; não preenchido | Não fixar n sem variância/desenho |
| `RecalibrationTriggers` | `false` | revisão por drift/queda de recall/FDR após governança | Nunca re-treinar automaticamente |

**Pré-requisitos de aprovação:** proposta científica assinada pelo PO, teste de viabilidade estatística realista, margem δ, seed independente, fingerprint/plan, número de clusters, plano de multiplicidade e política de abstenção. Sem isso, estado **AGUARDA DECISÃO DO PO**.

**Rastreabilidade:** GOV-DOC-01, issues #999, #1257 e #1349; nenhuma decisão científica atual substituída. **Este PR deve permanecer DRAFT.**
