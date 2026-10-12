# Linkage V9 — m/u conjunto ponderado no corpus de candidatos governado

**12/10/2026 — etapa 7, issue #946.** A V8 segue congelada para reprodução histórica; a V9 continua como arquitetura-alvo dos novos modelos. Esta entrega **não modifica** score, `Rank/Resolve`, `GENERATE_DRAFT`, `VALIDATE/ACTIVATE`, ledger ou parâmetros ATIVOS.

## Por que outra amostra?

As PRs #947–#952 implementaram níveis nominais V9, diagnósticos de `m/u` por campo e conjuntos pessoa/mãe (81 células), U condicionado ao blocking físico e M condicionado aos passes lógicos, e guarda de unidade TF. Entretanto o relatório conjunto desses pares é **não ponderado** e não comprova inclusão amostral M/U. É metodologicamente incorreto reinterpretar suas 81 células como frequência populacional.

Já existe, em paralelo, um subsistema `CandidateSamplingDesign` de **amostragem SRS em dois estágios**, com `InclusionProbability` e `DesignWeight` derivados do tamanho do frame e quotas por estrato, e `CandidateLabeling.Validate` que exige proveniência independente dos rótulos, grupo de independência e separação TRAIN/EVALUATION.

## Incremento implementado (diagnóstico exclusivamente)

`CandidateWeightedJointNominalEstimatorV9.Estimate(validatedCorpus, nominalLevelSidecar, alpha, minESS)` **reusa esse contrato governado** e calcula a tabela V9 9×9 ponderada. Um *sidecar restrito de entrada* informa `SourceId/CandidateId` e os níveis da pessoa e da mãe, **não rótulos**, completando precisamente o inventário de pares rotulados. As chaves só entram na memória do processamento: **nenhuma aparece no DTO resultante**. A tipagem dos níveis como enum não comprova por si a sua recomputação a partir dos nomes: a proveniência dessa classificação ainda precisa de atestado e verificação por hash/versionamento em etapa futura.

Os gates obrigatórios são:
- inventário exato 1:1 com `ValidatedCandidateCorpus.Observations`, sem par extra, ausente, repetido ou nível desconhecido;
- `Capture` e manifesto de rótulos devem ter mesmo frame/seleção, policy/feature e desenhos reconhecidos; denominadores, probabilidade de inclusão e pesos são recalculados por `CandidateSamplingDesign.Inclusion`, jamais confiados ao sidecar;
- amostra TRAIN e EVALUATION disjuntas por `IndependenceGroupId`; rótulos `Inconclusive` bloqueiam; ambas as classes devem aparecer em EVALUATION; M e U exigem ao menos **dois grupos independentes** cada no TRAIN e ESS de Kish mínimo;
- somente TRAIN é usado para estimar células, independentemente dos nomes/níveis da EVALUATION; cada par contribui a **uma única célula conjunta**, inclusive mães ausentes (`Missing`);
- retornam por classe peso total e ESS, por célula contagem **observada**, peso observado e probabilidade suavizada; a pseudocontagem **não cria suporte real**. A suavização `alpha` é explícita e ainda carece de escolha estatística governada (com efeito dependente da escala do peso).

O resultado é `DIAGNOSTIC_ONLY_BIRTH_BLOCKING_NOT_RUNTIME_SQL_CERTIFIED`: a amostragem validada usa o blocking `BirthBlockingPlan` do contrato de pesquisa, **não** os passes dinâmicos/chaves SQL históricas `VERSIONED_ALIAS` da Jornada em operação. Logo, não combinar numeradores desse diagnóstico com os denominadores de outra população, não ativar `AlgorithmVersion=V9`, não calcular TF ou LLR operacional com essas tabelas, não assumir independência nome/mãe.

## Antes de eventual promoção

1. Capturar e certificar um frame amostral ponderado que use o **mesmo universo de blocking dinâmico materializado SQL** com tratamento temporal e garantia de inclusão para M e U, ou demonstrar matematicamente o transporte de desenho.
2. Atestar recomputação dos **níveis V9** dos pares selecionados com o comparador congelado, sem dados pessoais no repositório/logs.
3. Estimar suportes/ESS/incerteza por estrato, estados raros e taxas de ausência, definir redução de estados conjunta quando necessário; ampliar TRAIN/VALIDATION/TEST sem contaminação por grupos.
4. Estimar TF em universo de **indivíduos** com unidades idênticas ao campo (nome integral ou par primeiro+último observado), com versão/população/seleção, sem produto de marginais IBGE.
5. Conferir matemática C# × Splink externo em pares idênticos e certificar FP/FDR/recall no orçamento vigente; só então autorizar DT-15.

**Nenhuma afirmação de certificação estatística, representatividade para o runtime ou alteração de modelo ATIVO é feita neste merge.**
