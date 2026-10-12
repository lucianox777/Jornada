# Linkage V9 — recomputação de níveis nominais a partir dos pares amostrais

**Data:** 12/10/2026. Continuação da issue #946, depois do merge #953.

## Lacuna corrigida nesta etapa

A estimativa ponderada V9 da #953 recebeu um *sidecar* de `CompositeNominalLevelV9` por par. Embora validasse inventário, enumerações, pesos e separação TRAIN/EVALUATION, **não podia comprovar que o nível declarado correspondia ao nome realmente observado**. Um emissor equivocado poderia atribuir `ExactFull` a um par nominal discordante, sem ser identificado.

Esta entrega adiciona a via **preferencial** `CandidateWeightedJointNominalEstimatorV9.EstimateFromRawEvidence`:

1. Recebe o `ValidatedCandidateCorpus` de amostragem em dois estágios (rótulos independentes), e um conjunto restrito de `CandidateNominalV9RawEvidence`, indexado por `SourceId, CandidateId`.
2. Confere o inventário completo da captura, inclusive **EVALUATION**: exatamente um par de evidência por par selecionado; faltas, duplicações e pares adicionais são rejeitados. Valida as versões congeladas de `IdentityComparison.NormalizationVersion`, `CandidateLabeling.FeatureVersion` e `CandidateSamplingDesign.Version`, os fingerprints de frame/seleção e a presença de manifesto aprovado.
3. Calcula diretamente os níveis com `CompositeNominalComparisonV9.Classify(leftName, rightName)` e `Classify(leftMother, rightMother)`, sem confiar em estados nominais definidos externamente. Nome integral, nomes intermediários e agnomes permanecem preservados pelo comparador. `Missing` de mãe continua nível específico.
4. Ordena a saída efêmera pela ordem congelada de pares no corpus; invoca o mesmo `CandidateWeightedJointNominalEstimatorV9.Estimate`, que reconfere pesos de desenho, grupos, ESS e partições de treino; nenhuma observação EVALUATION alimenta a tabela TRAIN.
5. Identifica `EvidenceGeneration=V9_NOMINAL_RAW_EVIDENCE_RECOMPUTATION_1` no diagnóstico agregado. A via legada de sidecar arbitrário permanece para análise histórica e explicita `CALLER_LEVEL_SIDECAR_UNATTESTED`.

## Limites fundamentais

Este procedimento demonstra que **as duas classificações nominais foram recomputadas pelo próprio comparador congelado** para todos os pares no *input*. Não prova de forma criptográfica nem por auditoria que os **nomes brutos entregues ao processo** são uma cópia fiel do snapshot original, nem valida a autenticidade de um repositório de evidências. Esses dados são pessoais: devem permanecer somente em ambiente controlado; não registrar seus valores, seus identificadores, os pares ou hashes não salteados de nomes.

A amostra continua associada a `BirthBlockingPlan`, não ao conjunto de passes SQL históricos/dinâmicos da operação, e a via atual ainda não está conectada a um leitor de evidências governado. Não usar seus `m/u` como parâmetros operacionais, não aplicar TF estatisticamente incompatível e não publicar modelo V9. Não houve alteração de modelo ATIVO, `Rank/Resolve`, `GENERATE_DRAFT`, `VALIDATE/ACTIVATE`, bancos ou replay V8.

## Próximos gates

Atestar procedência dos valores brutos e equivalência do universo selecionado ao blocking SQL materializado (incluindo `VERSIONED_ALIAS`); aplicar ponderação/ESS e intervalos de incerteza à população operacional; validar TF integral/conjunta sobre indivíduos; garantir segregação TRAIN/VALIDATION/TEST; executar a conferência externa independente com Splink sobre **os mesmos pares**; certificar FP/FDR/recall e DT-15.
