# Linkage V9 — diagnóstico conjunto de nome da pessoa e da mãe

**12/10/2026 — etapa 6, issue #946.** A decisão arquitetural aprovada mantém V9 como única direção de evolução, mas preserva V8 histórica para replay. Esta PR **não ativa** V9, não altera decisões ou scores, nem certifica `m/u` ou TF.

## Motivação

As etapas #947–#951 criaram níveis nominais mutuamente exclusivos, `m/u` separados de pessoa/mãe, condicionamento lógico dos pares M ao blocking e amostra U derivada das chaves materiais, além da guarda contra unidade TF inadequada. **Somar `ln(m_p/u_p)` de pessoa com `ln(m_m/u_m)` de mãe sem medir sua dependência pressupõe independência condicional injustificada.** A V9 precisa medir também a distribuição **conjunta**.

## Implementação nesta etapa

`CompositeNominalJointCalibrationEstimatorV9.Estimate` calcula no TRAIN a tabela completa de estados `(nível_nome_pessoa, nível_nome_mãe)`, 9 × 9 = **81 células mutuamente exclusivas**. Cada par rotulado independentemente contribui para **uma célula somente** em M ou U. `Missing` é preservado como parte da célula: a falta da mãe não é imputada como `Other`/discordância. Contagens observadas são independentes das pseudocontagens de suavização.

Os 81 níveis têm frequências `M` e `U` *diagnósticas*, normalizadas separadamente, com `smoothingAlpha` explícito. Os contadores `BothClassesObservedCells` e `ZeroClassSupportCells` revelam esparsidade; `RequireObservedInBothClasses` impede que uma célula com suporte exclusivamente de pseudocontagem seja interpretada como evidência observada. **Mesmo células com suporte nas duas classes não estão liberadas para scoring**: esse método só atesta contagens no frame que recebeu, não calibração populacional, incerteza ou validade amostral.

`CompositeNominalBlockingAuditBuilderV9` agora anexa o diagnóstico conjunto à auditoria atual do `GENERATE_DRAFT` quando o frame nominal por campo dispõe de suporte. O worker registra somente `matchedPairs`, `unmatchedPairs`, `exclusiveJointCells`, células apoiadas em ambas as classes e células sem suporte em uma classe. Nenhuma string nominal, identificador civil, token ou hash de nome é registrado. As 81 células expostas no retorno diagnóstico representam apenas enumerações, contagens e probabilidades derivadas, **não nomes ou frequências de valores**.

## Por que ainda NÃO publicar parâmetros V9

1. Pares M são recuperados pela projeção lógica atual, mas não têm pertencimento SQL histórico materializado `VERSIONED_ALIAS` certificado; pares U vêm de uma amostra do pool candidato SQL.
2. Não há pesos de inclusão ou representação de indivíduos em universo populacional demonstrados, nem intervalos de incerteza. Cada célula que influencie decisões exige suporte suficiente ou tratamento rigoroso e **versionado** de esparsidade, incluindo colapso calibrado de níveis, restrição/abstenção ou neutralidade explicitamente prevista. **Não exigir suporte independente em todas as 81 células como bloqueio global da calibração inicial**, mas tampouco promover uma célula só porque a suavização lhe deu probabilidade positiva. Estimar 81 probabilidades com amostra pequena pode produzir evidência espúria.
3. Não há estimativa independente de TF para `FullName`/`FirstLastJoint` na população compatível. O produto das marginais IBGE continua proibido.
4. Não há calibração condicional ao blocking e critérios auditáveis de `TRAIN/VALIDATION/TEST`, conferência matemática independente conforme DT-01/09/14, orçamento de FP/FDR/recall ou DT-15 concluídos. A paridade externa **C# × Splink permanece opcional/diagnóstica**, conforme decisão de avaliação sintética primária de 29/09; não constitui gate obrigatório do Ensaio.
5. A ausência da mãe pode ser informativa sobre mecanismos de coleta, mas **não** é uma evidência negativa automática; o tratamento decisório depende de calibração aprovada de ausências.

**Status:** `DIAGNOSTIC_ONLY_JOINT_MU_NOT_CERTIFIED`. Esta PR integra apenas fundação estatística **não decisória**. V8, ativos, parâmetros persistidos, bancos, APIs e ledger seguem intocados.

## Ponte matemática TRAIN → scorer FS V9 (preview somente, 12/10/2026)

O adaptador `CandidateWeightedJointPreviewAdapterV9.FromTrainingDiagnostic`
recebe o diagnóstico ponderado existente, **somente quando os estados foram
recalculados de evidência bruta** pelo comparador V9 e o registro preserva
a versão do desenho, partições, referências e suavização Kish ESS. Confere
81 células exclusivas, probabilidades normalizadas, pesos/contagens com suas
respectivas classes e grupos independentes. **A ponte V9 _2 recalcula também
cada probabilidade M/U a partir do peso observado na célula, do total por
classe, ESS Kish, alpha e 81 estados, exigindo igualdade decimal exata com
o estimador TRAIN.** Alterar duas células em sentidos opostos para preservar
a soma 1, ou adulterar alpha/ESS sem refazer as probabilidades, passa a
falhar fechado. Essa checagem garante **consistência matemática interna**,
não autenticidade do frame/rotulação nem suficiência estatística.
Copia as células para o DTO público
`FellegiSunterJointNominalCellV9`, que o **mesmo scorer FS C#** aceita na
API de pré-visualização matemática. A ponte não possui acesso a ativação,
persistência de modelos, `Rank/Resolve` operacional nem decisões do cidadão.

O teste usa `CandidateWeightedJointNominalEstimatorV9.EstimateFromRawEvidence`
sobre corpus rotulado com partições TRAIN/EVALUATION, atravessa a ponte e
executa `FellegiSunterScoring.CalculateCompositeNominalJointV9`. Rejeita
sidecars de estados fornecidos pelo chamador, probabilidades alteradas,
linhas duplicadas e metadados inconsistentes. Não infere paridade numérica
com Splink nem melhora a representatividade do `BirthBlockingPlan`.

**Importante:** o atributo `EvidenceGeneration` e os fingerprints são
declarações do diagnóstico, **não assinatura da origem bruta ou certificação
independente**. O desenho amostral atual não corresponde necessariamente ao
blocking SQL temporal; a ponte não autoriza o uso operacional desses `m/u`
na V9. Continuam faltando suporte populacional por célula, segurança TF,
splits e testes adversariais cegos, FDR/FP/recall, vinculação de manifesto
a um modelo e decisão DT-15. O roteamento V9 permanece explicitamente
bloqueado. O histórico V8 segue inalterado.

## Diagnóstico ponderado de decisões FS no holdout independente — preview V9

`CandidateWeightedJointFsHoldoutPreviewV9.Analyze` integra o caminho
`ValidatedCandidateCorpus` + nomes brutos restritos + `EstimateFromRawEvidence`
(TRAIN) + `CandidateWeightedJointPreviewAdapterV9` + **o mesmo
`FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown` C#**
sobre a partição **EVALUATION** congelada. O limiar de decisão é um
**parâmetro explícito externo**, validado no intervalo (0,1); a rotina
**não otimiza o limiar** nem altera o modelo.

A matriz 2×2 registra contagens e somas de `DesignWeight` para VP,
FP, FN e VN. Apresenta `ObservedWeightedFalseDiscoveryShare =
W_FP/(W_FP+W_VP)` (valor **nulo** quando nenhuma previsão é positiva)
e `ObservedWeightedRecallShare = W_VP/(W_VP+W_FN)`.
Exige no mínimo dois grupos independentes por classe de EVALUATION e ESS
de Kish suficiente, reutilizando as verificações de holdout da V9. O
resultado só contém agregados, nunca UUIDs, nomes, rótulos ou
posteriores individuais. Os testes demonstram que um par negativo com
nome alterado para concordância pode aumentar o FP observado, **sem
alterar m/u TRAIN**. O uso de limiar .99 demonstra que ausência de
previsões positivas **não é FDR observado igual a zero**.

**Limite estatístico e de produto:** essas frações observadas são
**diagnósticas no frame `BirthBlockingPlan` amostrado**; não são
estimativas certificadas de FP/FDR/recall do universo do blocking SQL,
nem um conjunto `TEST` independente e intocado. Pesos de pares não
substituem inferência por conglomerados, a classe de calibração ainda
é sujeita a suporte e viés do frame, e escolher um limiar olhando
EVALUATION invalida tratá-la depois como TEST cego. O relatório não
publica/ativa V9, não altera TF, `Rank/Resolve`, DT-15 ou V8 replay.
Faltam dados históricos representativos de pares, Splink em mesmos
pares, modelo governado e testes cegos antes da promoção.

## Sensibilidade de decisões FS por exclusão de grupo — preview V9

`CandidateWeightedJointFsHoldoutPreviewV9.Analyze` também relata
`GroupSensitivity` para o **mesmo limiar fixo fornecido pelo chamador**.
Uma vez calculados os posteriores dos pares EVALUATION, soma VP/FP/FN
por `IndependenceGroupId` apenas em memória, remove cada grupo completo
(da classe M e U simultaneamente) e recalcula **participação ponderada
observada de falsas descobertas** e **recall ponderado observado** no
restante. Expõe só extremos agregados de exclusões, quantidade de remoções
e de casos sem previsão positiva: nenhuma chave de grupo, par, score ou
nome é exportada.

Com menos de **3 grupos independentes por classe** em EVALUATION a
sensibilidade é marcada `UNAVAILABLE_LT_3_INDEPENDENT_EVALUATION_GROUPS`;
com pelo menos 3 em cada classe, cada exclusão deixa dois grupos por classe. A participação falsa
permanece **nula/indefinida** se não há positivos previstos. Os extremos
não devem mascarar a contagem de exclusões sem positivos: o mínimo/máximo
ignora somente exclusões com denominador nulo e ambos ficam nulos se
**todas** elas estiverem indefinidas. Testes incluem perturbação
adversarial confinada a uma fonte, cuja exclusão apaga o FP, e cenário
sem previsões positivas.

**Interpretação estritamente diagnóstica:** ranges leave-one-group-out
não são intervalos de confiança, não certificam FDR nem corrigem pesos
por conglomerado, não autorizam seleção de limiar olhando EVALUATION e
não conferem representatividade SQL temporal ao frame `BirthBlockingPlan`.
Nenhum gate operacional V9 ou decisão DT-15 é alterado.

## Decisão sobre tamanho do bootstrap (12/10/2026)

Os **30 mil indivíduos sintéticos** são suficientes **para iniciar o treino/ensaio controlado** do modelo nominal V9; **não foi medida nem demonstrada a suficiência em cada célula M/U, estrato, passe ou grupo independente**. A contagem de indivíduos não é a contagem de pares independentes e a matriz 81×(M,U) terá estados rarefeitos. Não transformar a exigência de cautela em obrigação de aumentar a massa para 200 mil ou 1,5 milhão sem diagnóstico, nem exigir dados municipais reais para **iniciar** o bootstrap. Registrar por célula suporte bruto, peso, ESS, grupos, perdas por partição e `BothClassesObservedCells/ZeroClassSupportCells`, com hipóteses e fingerprints; [parecer de suficiência](Parecer_Suficiencia_Bootstrap_30mil_V9_20261012.md). O Splink externo continua opcional; o TEST e a matemática independente continuam necessários antes da promoção.

## Incremento de suporte observado por célula, TRAIN × EVALUATION (PR de continuidade)

`CandidateJointSupportAuditBuilderV9` materializa um relatório **não certificador** das 81 células a partir de `CandidateWeightedJointHoldoutAuditV9` e do snapshot TRAIN validado pelo adaptador do scorer. Calcula contagens M/U observadas em ambas as partições, células raras/zeradas e fração de **peso observado** sobre células sem suporte TRAIN de ambas as classes. O mínimo de pares por classe é explícito no relatório; não inferir independência dentro de células a partir de contagem de pares ou grupos globais, nem atribuir validade municipal. O mesmo relatório passa a integrar o `CandidateWeightedJointFsHoldoutOutcomeV9.ObservedJointSupport`. Não há captura de PII, inferência de limiares com EVALUATION ou ativação da V9.

O mesmo incremento exige **m/u completos para todos os estados de nascimento do contrato declarado na V9**, inclusive quando um par não traz nascimento. A ausência de valor na observação continua distinta de ausência de parâmetro estatístico; a regra não altera replay V1–V8, que permanece com a semântica histórica onde já estava definida. Conforme [decisões canônicas](Decisoes_Canonicas_Identidade_Linkage_20260929.md), campos obrigatórios de entrada são governados pelo schema de origem, enquanto parâmetros obrigatórios de evidências declaradas devem falhar fechado.

[Limites de suficiência do corpus inicial de 30 mil](Parecer_Suficiencia_Bootstrap_30mil_V9_20261012.md). Não declarar análise completa dos 30 mil executada até anexar manifesto e métricas coletadas dessa massa no mesmo frame.

## Auditoria suplementar de grupos independentes por célula

`CandidateJointGroupSupportAuditBuilderV9` verifica **grupo independente por célula e classe** a partir dos pares completos rotulados e evidências brutas recalculadas, além de contagem de pares e ESS agregado. Por exemplo, múltiplos pares negativos da mesma fonte/grupo que caiam na mesma célula contam como vários **pares**, mas apenas **um grupo independente**. O relatório distingue TRAIN e EVALUATION e tem mínimo de grupos **explícito e configurável**, sem escolher thresholds com o holdout nem substituir TEST. Uma troca silenciosa de valores nominais no corpus sem recalcular o diagnóstico é rejeitada por reconciliação por célula; as chaves e nomes não saem no resultado.

**Limites:** grupos distintos não asseguram independência probabilística, cobertura de confiança ou equivalência `BirthBlockingPlan` × SQL temporal. Não é execução comprovada das 30.000 pessoas sintéticas, e nenhum estado sem suporte ganha calibração por mera pseudocontagem. [Parecer de suficiência](Parecer_Suficiencia_Bootstrap_30mil_V9_20261012.md).

## Nascimento na V9 — medir o Hamming existente antes de aumentar estados

Não alterar a política congelada de nascimento V5 por presunção de que inversão de 1978↔1987 exige novo peso. O classificador já calcula a distância posicional `ddMMyyyy` (Hamming); a precedência semântica faz uma inversão de dois dígitos com dia+mês iguais cair em `PARTIAL_COMPONENT_AGREEMENT`, não em `TWO_DIGIT_ERROR`. O diagnóstico sintético **TRAIN-only** `SyntheticBirthDistanceAuditBuilderV9` registra ambos (estado efetivo e distância 0–8), além da inversão específica, ano adjacente, máscaras de componentes e ausências, exclusivamente no gabarito agregado.

A medida distingue **cobertura do comparador** de **recall de candidate generation**: detectar semelhança não prova que o blocking recuperou o par. A distribuição demográfica IBGE governa as pessoas-base; a taxa de erros na observação depende do perfil hipotético separado até haver evidência municipal real por fonte. Nenhum desses histogramas atribui diretamente `m/u`, LLR, limiar, peso fixo, ou autoriza expansão automática do blocking. Ver [Corpus sintético e diagnóstico TRAIN](Calibrador_Corpus_Sintetico_V2.md).

### Decisão explícita para nascimento: manter Hamming como diagnóstico, não como LLR novo

Fundamentação técnica, evidência publicada (CIDACS-RL e discrepâncias reais de datas), exemplos e limites estão em [Decisão técnica — Hamming e nascimento V9](Decisao_Tecnica_Hamming_Nascimento_V9_20261012.md). O diagnóstico sintético V9 agora cruza **estado semântico V5 × distância Hamming 0–8** (63 células, zeros inclusos), exclusivamente em TRAIN e na superfície `gabarito.json`, para verificar empiricamente se a taxonomia precisa ser ampliada. A precedência **EXACT → SWAP dia/mês → CENTURY → Hamming 1 → ≥2 componentes → Hamming 2 → 1 componente → OTHER** permanece intencional. Nenhum teste antigo deve impedir evolução de um DTO de **preview V9**, mas modelos publicados/históricos e sua interpretação precisam continuar versionados.

### ESS por grupo dentro de cada célula nominal conjunta — diagnóstico, não ativação

A amostra candidata V9 tem grupos `IndependenceGroupId` potencialmente repetidos entre pares. A auditoria `CandidateJointCellGroupWeightAuditBuilderV9` calcula `ESS=(Σ_g W_g)^2/Σ_g W_g^2` em **81 células × TRAIN/EVALUATION × M/U** após somar primeiro os pesos de desenho dos pares de cada grupo na célula. Peso de dois pares compartilhando grupo forma **uma** contribuição ao quadrado, nunca dois grupos artificiais; a ausência de observações retorna `null`, não suporte vindo da suavização. O resultado agregado `IndependentJointCellGroupWeightEss` do preview do scorer C# corresponde ao inventário de comparação nominal reextraído e reconciliado contra os totais ponderados do holdout. **Não é um novo fator de Bayes nem muda o scorer**, TF, ranking ou blocking. Não equivale a inferência por conglomerados com hipótese de independência validada, nem substitui TEST congelado, suporte M/U suficiente, paridade do frame SQL ou DT-15. A motivação de dimensionamento está no [parecer da massa IBGE de 30 mil](Parecer_Suficiencia_Bootstrap_30mil_V9_20261012.md).

### Exposição ponderada em células sem ESS M/U bilateral no TRAIN

A auditoria `CandidateJointCellGroupWeightAuditBuilderV9` versão `_2` devolve a métrica `UnsupportedDesignWeightExposure`, com seis razões de **massa de desenho por classe/partição**: TRAIN M/U em células sem ESS de grupo ≥2 em ambas as classes no TRAIN; EVALUATION M/U nessas mesmas células sem suporte **TRAIN**; e EVALUATION M/U em células sem ESS bilateral na própria EVALUATION. Essas razões revelam onde o **peso observado** está concentrado, em vez de concluir suficiência pela média de 81 células. A avaliação não é usada para treinar `m/u` nem selecionar decisões. Não muda scorer, FS, SQL, threshold nem ativação operacional. Ver [parecer IBGE e ESS](Parecer_Suficiencia_Bootstrap_30mil_V9_20261012.md).

### Decisões FS concentradas em células sem suporte TRAIN — avaliação descritiva

O preview FS V9 `_3` incorpora `UnsupportedTrainingCellDecisions` (auditoria `_2`): para **o mesmo limiar explícito e fixo** da matriz VP/FP/FN/VN, associa cada par de EVALUATION ao nível nominal composto recalculado e verifica **somente o suporte TRAIN** de ESS Kish agregado por grupo ≥2 **em M e U** na célula correspondente. Relata contagem e peso de desenho de previsões positivas, VP e FP em células sem esse suporte, fração do peso de **todas** as previsões positivas atribuída a essas células e participação observada de FP **dentre as previsões positivas sem suporte**. Quando não há previsão positiva, ou nenhuma previsão positiva sem suporte, o respectivo quociente é `null`, nunca zero artificial. O mesmo relatório agora mede as **falsas negativas** (FN) em células sem suporte bilateral TRAIN: contagem, peso, parcela do peso de **todas as FN** concentrada nelas, e **recall observado entre pares verdadeiramente M nessas células** (= peso VP sem suporte / (peso VP + FN sem suporte)). Sem FN global, a parcela de FN é `null`; sem pares M observados nas células sem suporte, o recall local é `null` (não zero). Isso distingue risco de falso vínculo de perda de vínculos legítimos, sem inferir causalidade.


O preview FS V9 `_4` / auditoria de decisões `_3` fecha a matriz de decisão das células **sem ESS bilateral TRAIN** com verdadeiros negativos (VN, contagem e peso), além de VP/FP/FN. Publica **taxa de falso positivo observada nessas células** `peso(FP_sem_suporte) / [peso(FP_sem_suporte)+peso(VN_sem_suporte)]`, condicionada a pares realmente U da EVALUATION; isso **não** é a participação de falsas descobertas (FDR observado), cujo denominador são previsões positivas. Se não há U observados nas células sem suporte, a FPR local é `null`; se existem U e zero FP, é **0**, mesmo quando não existem previsões positivas e a FDR é `null`. Também mostra a fração da massa de VN total concentrada nessas células (`null` se não há VN). Nenhuma dessas razões é estimativa de taxa populacional, teste cego ou aprovação do modelo. A condição de suporte continua apenas a partir do TRAIN e a decisão usa o limiar fixo fornecido pelo chamador.

É um diagnóstico de **exposição do erro de decisão observado**, complementar às seis participações de massa por classe/partição. Não utiliza rótulos de EVALUATION para ajustar M/U, suavização, suporte ou limiar; nenhum par, ID ou nome aparece no DTO. Testes cobrem massa 100% sem suporte, 0% com suporte bilateral, aumento adversarial de FP com TRAIN estável, todas as M como FN sob limiar fixo alto e denominadores nulos. Não representa inferência independente em TEST congelado, limites de confiança ou FDR operacional; `BirthBlockingPlan` permanece distinto do blocking SQL temporal `VERSIONED_ALIAS`. Nenhuma alteração na V8, modelo ATIVO ou decisões em produção.
