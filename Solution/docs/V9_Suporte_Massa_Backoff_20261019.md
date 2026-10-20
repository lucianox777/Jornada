# V9 — suporte por massa e fallback marginal

**Contrato corrente, 19/10/2026.** Complementa a [seleção interna no TRAIN](V9_Selecao_Interna_TRAIN_Finalistas_20261019.md). A exigência histórica de M e U em todas as 81 células foi substituída por suporte no núcleo de massa de cada classe. FDR, fanout, recall mínimo, TF0 e governança de promoção não foram relaxados.

## Decisão estatística

A proposta literal de 5 M **e** 5 U nas células de 99% da massa conjunta também impediria o bootstrap: na união de 19 passes, `(ExactFull, ExactFull)` contém 9.220 M e zero U em TRAIN_ADJUST. U tem 505.169 pares contra 14.314 M; escolher apenas pela soma também pode ocultar células importantes de M. Por isso a política é **por classe**, congelada antes da busca:

1. Contar as 81 células exclusivas na união SQL deduplicada de TRAIN_ADJUST, separadamente para M e U. Mesmo blocking, partição e classificação para ambos; não usar IBGE marginal nem pares pré-blocking como substitutos.
2. Em cada classe, ordenar as células pela contagem decrescente, desempatar pela ordem canônica `(pessoa, mãe)` e incluir até atingir **pelo menos 99%** da massa observada. A célula que cruza a fronteira é incluída inteira.
3. Exigir **5 M** em cada célula do núcleo M e **5 U** em cada célula do núcleo U. A presença no núcleo M não exige uma contagem U positiva, e vice-versa. Núcleo abaixo do piso continua reprovado e registrado; não recebe suporte artificial.
4. Células fora do núcleo de uma classe usam fallback nessa classe. Uma célula pode ter M conjunto e U marginal. A escolha não olha TRAIN_SELECTION, VALIDATION ou TEST.

O contrato nominal universal permite todas as 81 combinações: os nove estados podem ser construídos independentemente para pessoa e mãe, inclusive ausências válidas. O teste de testemunhas verifica isso no classificador real. Portanto **não há exclusões estruturais universais demonstradas**. Zero observado não é declarado impossível. Restrições específicas de um blocking ou de um gerador não viram restrições gerais para registros novos; a tabela portátil continua completa e finita.

## Estimador único e normalização

Para uma classe C, com total N e alpha > 0, o núcleo mantém `J_ij = (n_ij + alpha)/(N + 81 alpha)`. As marginais são as da **mesma tabela TRAIN suavizada**, `P_i = (n_i. + 9 alpha)/(N + 81 alpha)` e `Q_j = (n_.j + 9 alpha)/(N + 81 alpha)`.

Se R é o conjunto raro, seu orçamento é `r = soma_R J_ij`. Nele, usar `p_ij = r × P_i × Q_j / soma_R(P_i × Q_j)`. Preserva a massa do núcleo e da cauda e normaliza cada distribuição em um. Se não há cauda, toda a tabela permanece conjunta. Não concatenar probabilidades conjuntas e produtos sem normalizar.

O produto das marginais é **hipótese de modelagem** para células raras, não evidência de independência, contagem observada ou garantia de FDR. Blocking pode induzir dependência. Essa hipótese precisa enfrentar o holdout congelado. Não se força ordenação entre EXATO, HIGH ou outros estados. TF permanece zero no fluxo padrão.

`V9JointSupportEstimation` é usado dentro do adaptador do calibrador único e na verificação de carga; não existe outro scorer ou motor de seleção. A política integra o fingerprint da configuração e do fit.

## Snapshot e auditoria

Novos fits usam `V9_FITTED_SNAPSHOT_V3`. `JointSupport` registra versão, SHA de TRAIN, alpha, totais, política, cobertura por classe, escalas de normalização e, para cada célula, contagens brutas, pertencimento aos núcleos e método M/U. O loader recalcula todos os valores e rejeita divergências. O SHA do snapshot cobre parâmetros e proveniência.

Snapshots V2 históricos continuam carregando com seus bytes/SHA originais; sua auditoria ainda inverte a suavização original. V3 lê contagens brutas verificadas e **não** inverte probabilidades de fallback como se fossem contagens Laplace. Leitores antigos recusam V3 em vez de ignorar a nova hipótese.

## Busca, multiplicidade e amostra

A execução padrão permanece: 36 configurações grossas, vizinhanças em lotes, parada por bootstrap pareado e no máximo três finalistas. A enumeração dos 524.287 subconjuntos continua diagnóstico opcional; não é invocada por `--v9-calibrate`. `batch-progress.jsonl` registra cada lote durante a execução.

`fresh-holdout-sample-plan.json` separa as famílias: **VALIDATION nova = número efetivo de finalistas**, TEST posterior = um único vencedor congelado. As 14.159.502 comparações conhecidas pertencem apenas à VALIDATION histórica consumida. A busca no TRAIN não entra na correção do novo holdout.

O plano externo usa o mesmo bound da avaliação: Clopper–Pearson de qualquer FP por identidade, Bonferroni entre finalistas e componentes, cap 4 e limite inferior Hoeffding da média de aceites. Rendimento previsto é declarado a partir de TRAIN_SELECTION e continua uma hipótese de desenho; a avaliação usa o rendimento realmente observado. Uma semente de 30 mil não garante antecipadamente FDR ≤ 0,3%. O plano antigo com quatro famílias de desenho e suporte em todas as células é diagnóstico histórico; não dimensiona automaticamente esta avaliação só de finalistas.

As definições de m/u por classe estão na [documentação primária do Splink](https://moj-analytical-services.github.io/splink/demos/tutorials/04_Estimating_model_parameters.html); isso não estabelece a validade deste fallback particular. O limite binomial exato está descrito pelo [NIST](https://www.itl.nist.gov/div898/software/dataplot/refman2/auxillar/exacbino.htm). A razão limitada por cap exige ainda hipóteses sobre grupos independentes e denominador, além do limite binomial.

## Reprodução e limites

```powershell
python Solution/scripts/v9-nested-calibration-study.py <observacoes.csv> <plano-novo.json>
dotnet <Worker.dll> --v9-calibrate <plano-novo.json> <observacoes.csv> <saida-nova>
python Solution/scripts/v9-nested-calibration-verify.py search <saida-nova>
```

Conexão somente ao JornadaHarness descartável. A nova VALIDATION só pode ser aberta depois de congelados os finalistas, com seed inédita, reserva de exposição e sem refit. TEST continua lacrado. Avaliação BLIND não é medição de Gold/Runner incremental; resultados condicionais não autorizam ativação ou removem DT-15.
