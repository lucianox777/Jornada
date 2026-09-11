# Linkage — manifesto de avaliação independente

Estado: contrato preparatório da issue #31. Não constitui homologação estatística nem autorização de produção.

## Objetivo

Impedir que resultados de avaliação sejam apresentados sem demonstrar qual método/modelo foi avaliado, qual corpus foi usado para calibração, qual corpus independente foi usado para avaliação e qual referência de verdade supervisionou a medição.

`IndependentEvaluationManifestCatalog` exige versões explícitas de método e modelo e três fingerprints SHA-256: corpus de calibração, corpus de avaliação e verdade de referência. O corpus de avaliação não pode ter o mesmo fingerprint do corpus de calibração.

O manifesto também fixa os denominadores mínimos (`CandidatePairs` e `ReferenceLinks`) e um instante UTC. Seu próprio fingerprint SHA-256 é determinístico, permitindo rastrear exatamente a combinação de método, modelo, corpus e referência avaliada.

`IndependentRuleSetEvaluationManifest` vincula a execução de avaliação a uma única versão/fingerprint de ruleset. O avaliador não reconstrói, substitui nem mistura regras durante a medição.

## Métricas independentes do resolver

`IndependentResolutionEvaluator` consome somente uma representação de avaliação independente: fingerprint opaco da observação, fingerprint opaco da referência verdadeira quando houver, candidatos com score e códigos opcionais de subgrupo. Não exige UUID, CPF, nome ou outro identificador bruto.

O avaliador aplica `T_LINKAGE` e margem informados explicitamente usando a mesma regra de fronteira do runtime: score abaixo do threshold permanece não resolvido; quando há segundo candidato, margem estritamente menor que `ConflictMargin` produz conflito; igualdade com a margem não produz conflito.

Antes de calcular qualquer métrica, a avaliação falha fechado se:

- a soma dos candidatos não for exatamente `CandidatePairs` do manifesto;
- a quantidade de observações com referência verdadeira não for exatamente `ReferenceLinks`;
- fingerprints de observação ou candidato forem inválidos/duplicados no respectivo escopo;
- scores estiverem fora de `[0,1]`;
- threshold ou margem estiverem fora dos domínios permitidos.

O relatório calcula, sem promover decisão alguma:

- recall e precisão da resolução final;
- quantidade e taxa de falsos vínculos;
- falsos positivos sobre não-vínculos de referência;
- taxa de resolução, conflitos e não resolvidos;
- recuperação do candidato verdadeiro no universo candidato, inclusive quando o threshold não o publica;
- Brier score do melhor candidato;
- bins fixos de calibração do melhor candidato;
- as mesmas métricas por subgrupos codificados fornecidos pelo corpus de avaliação.

O relatório é determinístico: observações, candidatos e subgrupos são canonicalizados antes do cálculo, e o fingerprint final vincula manifesto de avaliação, ruleset, threshold, margem e métricas resultantes.

## O que este contrato não prova

A existência de um manifesto ou relatório válido não prova representatividade, independência institucional, qualidade da rotulagem, ausência de viés de seleção nem suficiência do tamanho amostral. Esses pontos precisam de evidência real e atestação conforme a issue #31.

O avaliador não define limites mínimos aceitáveis para recall, precisão, falso vínculo, calibração ou subgrupos e não aprova automaticamente `T_LINKAGE` ou margem. Critérios de aprovação estatística e institucional permanecem externos a este contrato e devem ser aplicados sobre corpus real representativo.

Dados sintéticos e regressões de CI comprovam somente a implementação matemática e os invariantes fail-closed; não constituem homologação estatística.

## Limites operacionais

Esta fatia não altera scorer, m/u, prior, thresholds, blocking ou precedência determinística do CPF. Não cria ou funde UUID, não altera Gold/Serving, não ativa Linkage probabilístico e não escreve estado operacional de modelo/ruleset.
