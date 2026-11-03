# V9-TF-001 — gerador mínimo de dossiê exploratório

Registros normativos e resultado da primeira campanha: [issue #1228](https://github.com/lucianox777/Jornada/issues/1228#issuecomment-6613863873). Esta ferramenta somente lê o `study.json` produzido pelo ensaio físico e cria **agregados** sem conteúdo nominal, dados TEST, publicação ou ativação.

## Uso

```bash
python Solution/scripts/v9-tf-exploration-dossier.py \
  --study /local/study-tf/study.json \
  --out-dir /local/dossier-v9-tf-001 \
  --sha 3f21032e1b308df2d60fbf31dc92454efa3ab0a1 \
  --run-id 41681714081
```

Saídas: `V9-TF-001.md` (resumo de líderes por recall e **fronteiras Pareto descritivas recall × FDR**, sem decisão operacional) e `V9-TF-001.manifest.json` (SHA-256 do arquivo fonte, hashes semânticos das 135 medições e snapshots, proveniência, estados de seleção). A origem de SHA e run-id é argumento do chamador; conferir ambos na interface GitHub Actions antes de atribuir validade probatória.

Para comparar uma **repetição manual autorizada** com a primeira campanha, gerar primeiro o manifesto de referência e executar o mesmo comando para o segundo `study.json`, trocando run-id e adicionando `--baseline-manifest /local/dossier-v9-tf-001/V9-TF-001.manifest.json`. Diferenças em SHA do código, plano, TRAIN, VALIDATION, contagem, métricas ou conteúdo SHA-256 dos snapshots causam **falha explícita**; ordem das linhas e metadados do ZIP são ignorados. O campo `replication_equal_to_baseline` só é gravado quando essas evidências coincidem.

**Limites:** hash idêntico de resultados não prova validade estatística, representatividade, independência entre clusters, P95 fim a fim, resultado Gold ou autorização operacional. O relatório não altera nenhum parâmetro do calibrador e não substitui a avaliação independente. As fronteiras completas e o histórico de explorações permanecem nos artefatos originais; o relatório contém apenas uma visão agregada curta.

O script não deve ser executado automaticamente durante o trabalho diário de CI com um corpus físico de 30k. O artefato integral (~528 MB ZIP; muito maior após extração) permanece fora do Git; guardar cópia durável e o hash manifestado.

## Auditoria de multiplicidade: 3.861 versus 3.849 (03/11/2026)

A fonte histórica do plano e do C# é `v9-adaptive-30k-study.py` + `V9AdaptiveCalibrationCoordinator`:
- 3.726 hipóteses/comparações precedentes foram transportadas para o plano TF, sem zerar o histórico.
- 45 braços TF × 3 margens = 135 avaliações físicas; o plano histórico contém **3.861** comparações predeclaradas.
- Os 4 braços `TF_SHARED_PL_ML_*` (12 avaliações) têm `EligibleForSelection=false` e são excluídos da seleção automática e do Pareto do seletor. Assim, a família de seleção implementada no SHA original registra **3.849** = 3.726 + 41 × 3.
- O seletor original usou essa família menor no campo `BonferroniComparedConfigurations`. Não corrigir nem reescrever o JSON da campanha #41681714081. A nova instrumentação do dossiê verifica ambas as contagens e rejeita lacunas ou duplicações.

**Inferências e limites:** 3.849 só pode fundamentar uma família exclusivamente seletiva se a exclusão dos 12 diagnósticos foi predeclarada e sua **não influência, direta e indireta**, na inferência analisada for demonstrada. Há bloqueio técnico contra escolha direta, mas os diagnósticos já foram examinados e discutidos para priorizar hipóteses futuras: portanto, não está atestado que sejam totalmente irrelevantes ao processo adaptativo. Para novas *alegações simultâneas* que possam ser influenciadas pelos diagnósticos, adotar **3.861** conservadoramente, com todos os braços medidos; se a família de ensaios crescer, ampliá-la sem duplicar nem esquecer hipóteses já examinadas. O novo `CountInferenceConfigurations` usa a família integral nos limites físicos futuros e mantém `CountSelectableConfigurations` apenas como tamanho do shortlist.

Bonferroni sobre 3.861 é conservador sob dependência *entre hipóteses* **se** cada limite individual tiver cobertura válida. Não corrige dependência *entre identidades-base*: os bounds condicionais de cluster, sua cobertura e o denominador FDR precisam de estudo independente; `ClusterIidIndependentlyValidated=false` impede certificação. Não confundir `0` falsos aceites observados com risco estatístico nulo.

### Limite de FDR e causa efetiva da ausência de elegibilidade (auditoria de 03/11/2026)

O [registro físico da campanha #41681714081 na issue #1228](https://github.com/lucianox777/Jornada/issues/1228#issuecomment-6613844943) traz **5.556 identidades-base** pré-blocking. No melhor candidato descritivo, o FDR pontual foi aproximadamente **0,0132%**, o limite **HoeffdingUpperBySourceBaseIdentity** aproximadamente **5,6677%**, e o limite **ConditionalClusterCpFdrUpper** aproximadamente **0,8587%** (cálculo histórico com família 3.849). Este último ficou numericamente abaixo do critério de 1% da análise, mas **não autoriza seleção ou certificação**. O Hoeffding acima de 1% **não é, sozinho, o gate efetivo**: o seletor demanda a comprovação independente dos pressupostos dos clusters para o limite condicional, além dos demais gates. No ensaio, **135/135** medições têm `ClusterIidIndependentlyValidated=false`; o P95 físico fim a fim também não foi medido (`EndToEndMeasurements=[]`, `EndToEndRepetitions=0`). Portanto, há bloqueios **independentes** de validade estatística e desempenho físico.

Para a diferença de multiplicidade, `(3861 - 3849) / 3849 = 0,312%` de aumento na contagem de hipóteses (e aproximadamente **0,311%** de redução no alfa por componente): não é correto dizer que esta diferença de contagem seja menor que 0,1%. O impacto **numérico no limite** tende a ser pequeno por entrar em funções logarítmicas e binomiais, mas deve ser recalculado com os dados congelados antes de ser quantificado; não retroalterar o resultado publicado sob 3.849.

**Decisão ainda reservada ao PO:** confirmar a meta de risco operacional, a unidade estatística e o protocolo de validação da dependência entre identidades-base antes de escolher/mudar qualquer estimador. Não substituir automaticamente CP por Hoeffding nem afrouxar limites para produzir um vencedor. Uma nova campanha ou recomputação integral só pode ser atribuída ao SHA e aos resultados que realmente a executarem. A exploração matemática permanece independente de certificação e autorização de uso.

**Regressões novas:** o dossiê exige 45 braços únicos, margens 0,5/2/4 por braço, 135 medições e snapshots pareados, contagens 3.861 e 3.849 e ausência de inserção de 12 diagnósticos no ranking/pareto. Testes C# verificam o orçamento explorado e inferencial; falham em caso de orçamento menor ou duplicação do ruleset na mesma campanha. A correção de multiplicidade não modifica nenhum gate histórico de TF-001.

## Exploração matemática versus elegibilidade operacional

1. O **Calibrador C#** mantém TRAIN/VALIDATION isolados de TEST, avalia cada candidato sem veto percentual arbitrário antecipado, registra resultados, Pareto recall × FDR e regra de decisão matemática predeclarada. O Pareto do dossiê é uma projeção **observada**, não substitui o Pareto oficial nem autoriza escolha operacional.
2. A **avaliação independente** calcula risco com dependência por identidade-base, multiplicidade auditada, estratos, falsos aceites, cobertura de blocking, P95 físico e incerteza; não altera o ranking exploratório a posteriori.
3. A **governança/autoridade** pode autorizar ou recusar um modelo operacional em ato separado. Se não houver elegível, conservar o melhor candidato **exploratório** e os motivos da ausência de elegibilidade, sem publicar ou ativar.

## Etapas da descoberta integral ainda em aberto

- **Upper:** explorar blocking multipasse, comparadores, m/u, prior, TF, margens e upper em espaço versionado. Usar Pareto recall × FDR sem gates percentuais de entrada; comparar também a alternativa TF0 e registrar custo.
- **Lower Gold:** somente depois da estabilização do upper, explorar falsas criações × pendências no TRAIN, com joelho/fronteira Pareto e três ordens de ingestão em LocalDBs novas; não tratar o lower 0,05 nem upper 0,8671875 do baseline como valores finais.
- **Independência:** preservar isolamento por identidade-base em TRAIN, VALIDATION e TEST selado; nenhuma escolha deve consultar o TEST. Repetir o ensaio no mesmo SHA/seed/pin/corpus/plano apenas mediante novo disparo manual autorizado.
- **Evidências:** hashes exatos dos artefatos, manifesto semântico, decisões de seleção, cenários negativos, B-cubed, P95, sensibilidade de falsas criações e análise dos limites por estrato; ainda pendentes de conclusão.

## Responsabilidade e gates

As alterações nesta PR só afetam futuras instrumentações e avaliações; **não constituem nova campanha 30k**, novo resultado independente nem correção retroativa de TF-001. A PR permanece **DRAFT sem merge automático** até CI no HEAD exato, revisão estatística e autorização explícita do operador. O gate físico da probe não pode ser contornado alterando workflow ou marcando falha como sucesso.

### Separação nativa no Pareto C# (contrato incremental desta PR)

O método `V9BootstrapPareto2D.Evaluate` passa a expor, **além dos campos já existentes**, três campos não operacionais:
- `ExploratoryFrontierOptionIds`: todas as opções não dominadas com métricas observadas coerentes de recall e FDR; empates equivalentes preservados; evidência oracle ou medidas inválidas não entram;
- `ExploratorySuggestedOptionId`: ponto da fronteira de distância euclidiana normalizada mínima ao ideal (recall máximo, FDR mínimo), com desempate determinístico por fingerprint e OptionId;
- `ExploratoryStatus`: `EXPLORATORY_ONLY_NOT_CERTIFIED` ou ausência de medições válidas. Em fragmentos (shards) parciais o vencedor exploratório global é **nulo**.

Esses campos são calculados **antes** dos filtros de P95, independência estatística, limites de risco e elegibilidade. A tabela e os campos históricos `InitialSuggestedOptionId`, `Eligible`, `OnFrontier`, `Status` e razões de exclusão **permanecem** representando somente a verificação operacional anterior. Portanto, `NO_ELIGIBLE_CONFIGURATION` pode coexistir corretamente com um `ExploratorySuggestedOptionId` informativo. O próprio Calibrador continua escolhendo matematicamente o candidato; o operador apenas solicita o ensaio, e governança mantém sua decisão separada.

A fronteira é descritiva: igualdade empírica não implica confiança estatística, generalização ao TEST, P95, certificação ou uso. A nova varredura de Pareto é ordenada, com custo `O(n log n)` para o catálogo de pontos observados; evita segunda comparação quadrática. Testes C# verificam fronteira e escolha invariantes à ordem mesmo quando todos os gates falham, exclusão de métricas inconsistentes/oracle e nulidade do vencedor global em shards parciais.

**Versionamento do contrato Pareto:** como o resultado C# ganhou campos `ExploratoryFrontierOptionIds`, `ExploratorySuggestedOptionId` e `ExploratoryStatus` e passou a computar seleção matemática antes dos gates, a versão nova é `V9_BOOTSTRAP_PARETO_2D_IDEAL_DISTANCE_V3`. Estudos e hashes emitidos sob `V2`, inclusive a campanha TF-001 original, não devem ser reetiquetados nem reinterpretados retroativamente como `V3`. O dossiê exploratório tem versão própria `V9_TF_EXPLORATORY_DOSSIER_V1`.
