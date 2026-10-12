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
2. Não há pesos de inclusão ou representação de indivíduos em universo populacional demonstrados, nem intervalos de incerteza. Cada uma das 81 células exigirá suporte suficiente ou tratamento rigoroso de esparsidade; estimar 81 probabilidades com uma amostra pequena pode produzir evidência espúria.
3. Não há estimativa independente de TF para `FullName`/`FirstLastJoint` na população compatível. O produto das marginais IBGE continua proibido.
4. Não há calibração condicional ao blocking e critérios auditáveis de `TRAIN/VALIDATION/TEST`, comparação matemática com Splink, orçamento de FP/FDR/recall ou DT-15 concluídos.
5. A ausência da mãe pode ser informativa sobre mecanismos de coleta, mas **não** é uma evidência negativa automática; o tratamento decisório depende de calibração aprovada de ausências.

**Status:** `DIAGNOSTIC_ONLY_JOINT_MU_NOT_CERTIFIED`. Esta PR integra apenas fundação estatística **não decisória**. V8, ativos, parâmetros persistidos, bancos, APIs e ledger seguem intocados.

## Ponte matemática TRAIN → scorer FS V9 (preview somente, 12/10/2026)

O adaptador `CandidateWeightedJointPreviewAdapterV9.FromTrainingDiagnostic`
recebe o diagnóstico ponderado existente, **somente quando os estados foram
recalculados de evidência bruta** pelo comparador V9 e o registro preserva
a versão do desenho, partições, referências e suavização Kish ESS. Confere
81 células exclusivas, probabilidades normalizadas, pesos/contagens com suas
respectivas classes e grupos independentes. Copia as células para o DTO público
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

Com menos de **3 grupos independentes** no conjunto EVALUATION a
sensibilidade é marcada `UNAVAILABLE_LT_3_INDEPENDENT_EVALUATION_GROUPS`;
com pelo menos 3, cada exclusão deixa dois grupos. A participação falsa
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
