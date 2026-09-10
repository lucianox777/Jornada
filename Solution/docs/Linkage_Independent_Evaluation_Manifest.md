# Linkage — manifesto de avaliação independente

Estado: contrato preparatório da issue #31. Não constitui homologação estatística nem autorização de produção.

## Objetivo

Impedir que resultados de avaliação sejam apresentados sem demonstrar qual método/modelo foi avaliado, qual corpus foi usado para calibração, qual corpus independente foi usado para avaliação e qual referência de verdade supervisionou a medição.

`IndependentEvaluationManifestCatalog` exige versões explícitas de método e modelo e três fingerprints SHA-256: corpus de calibração, corpus de avaliação e verdade de referência. O corpus de avaliação não pode ter o mesmo fingerprint do corpus de calibração.

O manifesto também fixa os denominadores mínimos (`CandidatePairs` e `ReferenceLinks`) e um instante UTC. Seu próprio fingerprint SHA-256 é determinístico, permitindo rastrear exatamente a combinação de método, modelo, corpus e referência avaliada.

## O que este contrato não prova

A existência de um manifesto válido não prova representatividade, independência institucional, qualidade da rotulagem ou ausência de viés de seleção. Esses pontos precisam de evidência real e atestação conforme a issue #31.

Também não calcula nem aprova automaticamente recall, precisão, calibração probabilística, falsos vínculos, `T_LINKAGE` ou margem. Uma fatia posterior pode produzir essas métricas sobre um manifesto válido, mas sua aceitação continua condicionada ao corpus representativo e à aprovação institucional.

## Limites operacionais

Esta fatia não altera scorer, m/u, prior, thresholds, blocking ou precedência determinística do CPF. Não cria ou funde UUID, não altera Gold/Serving e não ativa Linkage probabilístico.
