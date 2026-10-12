# V9 — auditoria de distribuição conjunta em amostra independente EVALUATION

**Data:** 12/10/2026. Continuação da [issue #946](https://github.com/lucianox777/Jornada/issues/946) após #947–#956. **Status:** diagnóstico exclusivamente, sem alteração do modelo ATIVO, sem novo scorer e sem certificação estatística.

## Problema

As PRs #953 e #954 já calculam uma distribuição ponderada 9×9 de estados mutuamente exclusivos (nome civil completo e nome da mãe), com níveis recomputados pelo comparador V9 a partir de evidências brutas restritas, rótulos independentes por referência e pesos do desenho `CandidateSamplingDesign`. Entretanto, as 81 probabilidades eram publicadas como diagnóstico TRAIN sem uma **conferência empírica separada na partição EVALUATION**. Um modelo pode parecer calibrável em TRAIN e ter células dominantes ou ausentes no holdout independente.

## Incremento

O componente `CandidateWeightedJointHoldoutAuditBuilderV9.AnalyzeFromRawEvidence` toma o **mesmo** `ValidatedCandidateCorpus` com partições congeladas e os valores nominais restritos dos **mesmos pares selecionados**:

1. Recalcula níveis conjuntos pessoa/mãe usando `CandidateNominalV9EvidenceExtractor`, com inventário integral de TRAIN **e** EVALUATION (sem sidecar nominal arbitrário).
2. Estima m/u **apenas em TRAIN**, via `CandidateWeightedJointNominalEstimatorV9.Estimate`, que reconfere proveniência de rótulos, pesos/probabilidades do desenho, partições disjuntas por `IndependenceGroupId`, dois grupos por classe em TRAIN e ESS mínimo.
3. Examina **somente EVALUATION** em uma segunda passada, separadamente em M e U, formando as mesmas **81 células** do diagnóstico TRAIN sem inventar um estado novo. Para cada classe conta pares, grupos independentes, peso total e **ESS de Kish**; para cada célula conta pares e peso observado, com `Missing` preservado.
4. Mede a **distância de variação total** `TVD = 0,5 × Σ|p_EVALUATION(célula|classe) - p_TRAIN_observado(célula|classe)|` separadamente para M e U. Usa os pesos **observados sem pseudocontagem** em TRAIN (não os `m/u` suavizados), e registra o peso de EVALUATION que cai em **células sem suporte observado** em TRAIN.
5. Recusa avaliação por classe com menos de **dois grupos independentes** ou com ESS de Kish abaixo do mínimo solicitado. Não interpreta contagem zero suavizada como cobertura real. O DTO só contém agregados e enums: não expõe nomes, UUIDs de pessoa, hashes nominais, grupos ou dados por par.

**Conferência de isolamento:** modificar os nomes *exclusivamente* na partição EVALUATION altera o diagnóstico holdout, **sem alterar um único valor das tabelas TRAIN**. Testes também cobrem matriz 81 células, pesos, suporte, ausência materna, falhas de inventário e privacidade.

## Como interpretar (e como NÃO interpretar)

- TVD é uma **medida descritiva de desvio** entre duas distribuições condicionais empíricas dentro do frame estudado. Não é p-valor, intervalo de confiança, teste de equivalência, previsão de FDR, estimativa de recall nem critério calibrado de aprovação.
- ESS de Kish `(Σw)^2/Σw²` indica dispersão dos pesos **por par**. Não modela automaticamente clusters de identidade, estratos difíceis, erros de fonte, seleção informativa ou incerteza do universo.
- As probabilidades de inclusão e os pesos são da amostra `CandidateSamplingDesign` com **BirthBlockingPlan**, **não** do ruleset dinâmico de chaves históricas SQL (`VERSIONED_ALIAS`) que decidirá candidatos operacionais. A auditoria **não atesta** transporte estatístico para o cenário de produção.
- A recomputação nominal comprova consistência entre **input restrito** e comparador V9 congelado, não a fidelidade ou origem do input bruto nos snapshots históricos.
- A partição `EVALUATION` existente é um holdout independente por grupo, mas **não se deve afirmar** que satisfaz automaticamente a separação governada TRAIN/VALIDATION/TEST nem usar suas métricas para selecionar hiperparâmetros e depois reportá-las como TEST intocado.
- Não combinar a distribuição conjunta nominal com frequência TF integral ou proxy IBGE sem unidades e pesos apropriados; a correção de proxy da #955 continua subordinada à [DC-LK-TF](Decisoes_Canonicas_Identidade_Linkage_20260929.md).

O status explícito é `DIAGNOSTIC_ONLY_EVALUATION_BIRTH_BLOCKING_NOT_SQL_CERTIFIED`, conservando o status TRAIN `DIAGNOSTIC_ONLY_BIRTH_BLOCKING_NOT_RUNTIME_SQL_CERTIFIED`. Nem um resultado TVD zero autoriza `AlgorithmVersion=V9`, um score, threshold, ativação ou inferência de precisão populacional.

## Próximos gates antes da V9 operacional

1. Provar o mesmo universo de candidatos para M/U com passes SQL dinâmicos históricos `VERSIONED_ALIAS` e inclusão amostral por estrato.
2. Atestar cadeia de custódia/versão dos nomes extraídos dos snapshots e as probabilidades de seleção; auditar vazamento entre fontes e coortes.
3. Ampliar grupos e suporte/ESS por classe/célula, estimar incerteza e selecionar eventual agrupamento de estados raros sem olhar TEST.
4. Construir TF de nome integral/par conjunto no frame compatível, ou proxy de sobrenome IBGE explicitamente marcado sob DC-LK-TF; estimar pesos com TRAIN/VALIDATION.
5. Conferir paridade matemática de um único scorer C# com Splink sobre os **mesmos pares**, certificar orçamento FP/FDR/recall em TEST e obter a decisão governada DT-15.

**Escopo operacional zero:** esta PR cria biblioteca diagnóstica, testes e documentação; não integra a auditoria à execução de `GENERATE_DRAFT`, não publica ou ativa modelo, não altera V8 histórica, `Rank/Resolve`, SQL, locks, replays nem scores.
