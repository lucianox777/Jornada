# V9 — formação incremental de identidades candidatas sem gabarito (18/10/2026)

**Superveniência factual:** as PRs [#1151](https://github.com/lucianox777/Jornada/pull/1151), [#1153](https://github.com/lucianox777/Jornada/pull/1153) e [#1152](https://github.com/lucianox777/Jornada/pull/1152) foram integradas à master, respectivamente nos commits `2111ad4cddeec571fa5bdd43e182ca3fe1eec533`, `9e043fe00ae38686d8abaffa99bf4ea0280f106b` e `30c28ae63a2f0d8cafe32c7c79accc232c85381a`. O dossiê `V9_Reconciliacao_Decisoes_Evidencias_Runners_20261018.md` é uma fotografia **anterior** à integração da #1152.

## Incremento implementado nesta PR (fundação testável, não execução física)

`V9BlindIncrementalIdentityGrouping.Build` é um núcleo **diagnóstico puro** para formar grupos de candidatos sem ler `BasePersonId`, CPF ou TEST. Recebe observações VALIDATION e arestas SQL canônicas com **log-odds FS de ambas as direções** calculados externamente a partir do mesmo fit congelado em TRAIN. Esta API não atesta, sozinha, a proveniência SQL/FS; essa integração ao probe físico ainda precisa ocorrer antes de qualquer ensaio.

Requisitos de transição: posterior FS mínima explícita (default experimental 0,99, não política operacional), escore em ambas as direções acima do limiar, primeiro e último token normalizados do nome iguais, nome da mãe normalizado completo igual, nascimento exato presente, gestores diferentes e **aresta SQL forte para cada par entre dois grupos** (complete linkage). Não há fusão por encadeamento A–B–C se A–C não foi comprovada; ausência de campo não autoriza merge. Limite pré-declarado de tamanho do grupo impede expansão silenciosa. A ordem é determinística por escore mínimo e IDs; UUIDs de grupos são sintéticos e **nunca** devem ser publicados como Gold.

Os testes cobrem variação de nome intermediário, cadeia sem aresta de fechamento, escore assimétrico, guard de mãe, gestor, ausência de campo, cap, TEST, duplicações e **invariância à troca do gabarito**. Um homônimo completo pode passar todos os guards: não há promessa de ausência de FP. O mecanismo ainda **não** substitui o modo BLIND_EXACT, não participa da CLI/Actions e não modifica os resultados físicos históricos. A integração seguinte deverá medir exclusivamente **clean**, thresholds 0,90/0,95/0,99 em VALIDATION e TP/FP/CONFLITO por fonte após Rank→Resolve; **não** usar ORACLE para formar grupos ou escolher limiares.

## Fronteiras de validade

A massa sintética clean seed42 não demonstrou representatividade brasileira. O FDR pontual e o bound Hoeffding atual não certificam operação; independência iid entre identidades, cap, amostragem e suporte devem ser auditados antes de decisão. TF positiva fica bloqueada até provar mesma unidade e condicionamento do U SQL TRAIN e referência congelada. Não consumir TEST na seleção; não promover modelo, snapshot `ref`, V9/DT15, HML/PROD ou Gold. Perfis correlated/independent/field ficam como histórico manual, sem novo ensaio automático. A evidência do DT-05 Runner CPF tardio (run 39149089443, artifact 12145055171) não equivale à execução física V9/HML.


## Integração real ao probe SQL físico clean (sem gabarito de agrupamento)

O probe chama agora `EvaluateSourceUnitResolveValidation` com o braço
`BLIND_FS_COMPLETE_LINK_CANDIDATES` habilitado **apenas** na CLI física
(`includeFsBlindGrouping=true`), mantendo o comportamento anterior nas
chamadas existentes. Para cada par efetivamente materializado no SQL de
VALIDATION, o mesmo modelo FS já ajustado em TRAIN calcula os
`LogOdds` direcionais, via `RankCompositeNominalV9Preview` e o mesmo
conjunto de candidatos. `V9BlindIncrementalIdentityGrouping.Build` recebe
**somente** observações sem TEST, arestas SQL reais e scores TRAIN-frozen.
Rótulos sintéticos `BasePersonId` são consultados **após** a formação
dos grupos apenas para medir clusters impuros, TP e FP. `ResolveRankedCore`
calcula a margem entre o maior FS de **cada grupo inferido distinto**.
Esse modo é diagnóstico e pode rejeitar verdadeiros matches com erro
nominal: os guards exigem prenome, último nome, mãe e DOB exatos; portanto
não representa o agrupador Gold operacional nem garante melhoria de recall.

A CLI mede thresholds de decisão `0,90 / 0,95 / 0,99`, margens
`0 / 0,5 / 2`, e os quatro modos RAW, BLIND_EXACT, BLIND_FS_COMPLETE_LINK
e ORACLE (=36 condições físicas), sempre TF=0. O limiar *de fusão*
FS `0,99` é mantido **fixo e declarado** para tornar comparáveis as
três políticas de decisão (não é parâmetro escolhido para PROD).
O workflow publica `physical-source-resolve-3x3x4-TF0.csv` e JSON,
associados ao hash do corpus/fit; o braço ORACLE continua apenas teto
não operacional. Antes da execução, números do quarto braço são
**não medidos**; não inferir FP/recall sem artefato do Actions.


## Inferência de FDR por identidade: análise CP exata condicional (18/10/2026)

O experimento físico `clean` após a PR #1154 confirmou no threshold
**0,95** e margem **0,5**, para o braço BLIND_FS_COMPLETE_LINK,
**7.505 TP, 0 FP observados, 176 conflitos**, recall global por fonte
**95,715%** em 7.841 fontes elegíveis. BLIND_EXACT ficou em 7.502 TP:
o ganho do FS conservador foi somente +3 fontes. No threshold
**0,90** o FS obteve 7.535 TP, **1 FP**, recall 96,097%.
Evidência: [run SQL físico 39151390820](https://github.com/lucianox777/Jornada/actions/runs/39151390820),
[artefato #12145661038](https://github.com/lucianox777/Jornada/actions/runs/39151390820/artifacts/12145661038),
HEAD `c5f52c12fb532ffbfd56087ce163728bcccc8640`, merge da
[PR #1154](https://github.com/lucianox777/Jornada/pull/1154)
em `9c84527f8fe44496c35c142cde009135653e28ca`.

O novo diagnóstico `UpperBoundForPopulationMeanRatioByAnyErrorCp`
usa **Clopper–Pearson unilateral exato** para a **probabilidade
por identidade de pelo menos um FP**, NÃO diretamente FDR por vínculo.
Sob as hipóteses explícitas de identidades independentes e iid,
truth completa, e cap `C` imposto antes do experimento:
`E[FP/identidade] ≤ C × P(identidade com FP)`.
Uma LCB unilateral de Hoeffding para `E[aceites/identidade]`
permite então limitar `E[FP]/E[aceites]`. O risco total alpha
é repartido entre numerador/denominador e corrigido por
**Bonferroni sobre todas as configurações comparadas**.
Quando a LCB do denominador é zero, o resultado é **undefined**,
nunca risco zero. A classe preserva `OperationallyCertified=false`
e `IidClusterAssumptionIndependentlyValidated=false`.

Usando a coorte medida (5.556 grupos de identidade, 7.505 aceites,
zero FP, cap16) a fórmula dá UCB **~1,003% em comparação única**
ou **~2,223% para 36 opções pré-declaradas**, sempre **condicionais
às hipóteses não certificadas**, não promessa de 95% de cobertura
sobre o conjunto real. O Hoeffding original (~27,52%) permanece
visível como limite descritivo mais conservador. A competição
entre identidades no blocking e no Resolve cria dependência
estatística, então **não usar automaticamente o bound CP para
selecionar nem promover V9**. Essas duas colunas serão medidas
no probe físico sem usar TEST; TF positiva e correção de múltiplos
estudos permanecem pendentes.

## Seletor diagnóstico pós-Resolve por FONTE — complemento de 19/10/2026

O antigo `AdaptiveCalibrationModelSearch` gerava uma **sugestão por par**,
que NÃO mede as decisões finais por identidade e poderia ser confundida
com seleção válida. Quando o `V9PhysicalSqlAdaptiveCalibrationProbe`
executa `evaluateFinalResolve=true`, agora o relatório legado **remove
explicitamente** `SuggestedModelFamily/OptionId` e registra
`PAIRWISE_SUGGESTION_SUPPRESSED_USE_POST_RESOLVE_GATES`.

O novo `V9PostResolveSourceDiagnosticSelector.Evaluate` usa apenas
`BLIND_FS_COMPLETE_LINK_CANDIDATES` (não RAW nem ORACLE) e mede no
VALIDATION físico por observação-fonte: recall global com todas as fontes
elegíveis pré-blocking; FP/aceites FINAL após Resolve; bound condicional
CP por identidade; e política explícita de margem positiva (0,5 neste
estudo). Essas **nove células** representam três thresholds por
três margens. A multiplicidade de 36 células já é aplicada no CP físico
para contemplar todos os braços realmente comparados.

**Fail closed:** `ClusterIidIndependentlyValidated=false` nos artefatos,
mesmo com FP pontual zero. Logo `SuggestedOptionId=null` e
`NO_SUGGESTION_CLUSTER_INDEPENDENCE_UNPROVEN`, sem modelo criado nem
ativação. Este seletor é **um diagnóstico C# físico**, não a publicação
do calibrador operacional na `ref`, e não altera o contrato genérico de
busca de outras famílias. Suposições iid, estimativa de FDR com cobertura,
seletor adaptativo com o catálogo completo e validação operacional
ainda requerem sua própria etapa e prova.
