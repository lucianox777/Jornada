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

## Ponderação e incerteza amostral

O relatório descritivo acima não deve ser tratado automaticamente como inferência para a população quando o corpus de avaliação resulta de amostragem complexa. `IndependentResolutionSurveyEvaluator` é uma camada separada para esse caso.

Cada observação recebe um `DesignWeight` positivo e um fingerprint SHA-256 opaco de `IndependenceGroup`. A rotina primeiro executa integralmente `IndependentResolutionEvaluator`, reutilizando seus gates de denominadores, candidatos, scores, threshold e margem. Somente depois calcula as versões ponderadas de recall, precisão, falso vínculo, falso positivo, resolução, recuperação do candidato, Brier e calibração.

A incerteza usa `DELETE_ONE_CLUSTER_JACKKNIFE_NORMAL95_V1`: cada réplica remove um conglomerado inteiro, nunca uma observação isolada. São exigidos pelo menos três grupos independentes no relatório global. Para um subgrupo com menos de três conglomerados, as métricas ponderadas continuam disponíveis, mas intervalos jackknife não são fabricados.

O fingerprint do desenho amostral inclui, em ordem canônica, fingerprint da observação, peso e conglomerado. Alterar peso ou agrupamento altera o fingerprint mesmo quando o corpus lógico é o mesmo. O fingerprint final do relatório liga esse desenho ao relatório descritivo do #100, às estimativas ponderadas, às réplicas de incerteza e aos bins de calibração.

Os intervalos de 95% são uma ferramenta técnica reproduzível para propagação da estrutura em conglomerados; não estabelecem suficiência amostral, desenho institucional válido, ajuste de não resposta ou regra de aprovação. Se a metodologia institucional exigir estratificação, FPC, pesos de não resposta/calibração ou outro estimador de variância, isso deve ser declarado/versionado explicitamente em nova metodologia, sem reinterpretar este contrato.

## Governança e proveniência dos pesos

`IndependentResolutionGovernedSurveyEvaluator` adiciona uma camada de rastreabilidade sobre a avaliação ponderada sem estimar, recalcular ou alterar pesos. Para cada observação, o contrato registra `BaseDesignWeight`, `FinalWeight`, versão do método, referência de governança e instante de atestação; o peso final precisa ser exatamente o mesmo `DesignWeight` consumido por `IndependentResolutionSurveyEvaluator`.

Seleção, não resposta e calibração precisam ser declaradas explicitamente e exatamente uma vez. Cada uma assume um dos estados `Applied` ou `NotApplicable`. Quando `Applied`, são obrigatórios uma referência de evidência e um fingerprint SHA-256; quando `NotApplicable`, o contrato rejeita metadata de evidência escondida. Um mesmo relatório governado também não pode misturar versões do método de ponderação.

A rotina não deriva probabilidades de seleção ou resposta, não impõe multiplicadores e não supõe relação matemática entre peso-base e peso-final. A escolha e a justificativa do método permanecem externas e institucionais. A função dessa camada é impedir que um peso corrigido seja usado na avaliação sem declarar de forma auditável quais classes de ajuste foram ou não aplicadas.

A proveniência dos pesos recebe fingerprint determinístico próprio, e o relatório final vincula esse fingerprint ao relatório ponderado do #101. Alterar método, referência, peso-base, peso-final, atestação ou evidência de qualquer ajuste muda a identidade do artefato, mesmo que as métricas ponderadas permaneçam numericamente iguais.

Essa rastreabilidade não prova que um ajuste de seleção, não resposta ou calibração seja estatisticamente correto, nem que seja necessário ou suficiente. Essas decisões continuam condicionadas ao desenho institucional, ao mecanismo de coleta e à evidência real prevista na issue #31.

## Dependência entre múltiplas evidências

O scorer de Fellegi-Sunter combina contribuições de `NOME`, `NOME_MAE` e `NASCIMENTO_CONJUNTO`. Antes de interpretar essa soma como adequadamente calibrada, a hipótese de independência condicional entre evidências precisa ser empiricamente inspecionável no corpus independente.

`CandidateEvidenceDependencyDiagnostic` usa exclusivamente observações da partição `Evaluation` já validadas por `CandidateLabeling`. O diagnóstico nunca usa a partição `Training` para medir dependência e separa obrigatoriamente as classes `Match` e `NonMatch`; rótulos inconclusivos na avaliação são rejeitados.

Para cada classe, são avaliados os três pares de evidências (`NOME × NOME_MAE`, `NOME × NASCIMENTO_CONJUNTO` e `NOME_MAE × NASCIMENTO_CONJUNTO`) em dois escopos:

- `AllStates`: inclui `MISSING` como estado explícito e, portanto, também detecta associação de missingness;
- `ObservedOnly`: remove pares em que pelo menos uma das duas evidências está ausente, permitindo distinguir dependência dos valores observados de dependência causada pela ausência conjunta.

A associação é reportada por duas medidas ponderadas pelo desenho amostral:

- distância de variação total entre a distribuição conjunta observada e o produto das marginais, em `[0,1]`;
- informação mútua normalizada, em `[0,1]`, quando ambas as marginais têm entropia não degenerada.

Cada medida também carrega número de pares, quantidade de grupos independentes, peso observado e tamanho efetivo. Quando o suporte não atinge os mínimos técnicos solicitados de grupos independentes ou tamanho efetivo, a métrica é marcada como não estimável com motivo explícito; a rotina não substitui falta de suporte por dependência zero.

O relatório não contém threshold de aprovação, ranking ou regra de correção. Associação observada não modifica m/u, posterior, thresholds ou weights operacionais. Se os dados reais mostrarem dependência material, qualquer resposta metodológica — combinação de campos, interação, modelo alternativo, recalibração ou aceitação justificada da aproximação — exige decisão estatística versionada e nova validação independente.

O fingerprint determinístico do relatório vincula frame, seleção, referência de rotulagem, versão das features, denominadores e todas as métricas de dependência. Dados sintéticos validam somente a implementação do diagnóstico; não provam independência condicional no corpus institucional.

## O que este contrato não prova

A existência de um manifesto ou relatório válido não prova representatividade, independência institucional, qualidade da rotulagem, ausência de viés de seleção nem suficiência do tamanho amostral. Esses pontos precisam de evidência real e atestação conforme a issue #31.

O avaliador não define limites mínimos aceitáveis para recall, precisão, falso vínculo, calibração ou subgrupos e não aprova automaticamente `T_LINKAGE` ou margem. Critérios de aprovação estatística e institucional permanecem externos a este contrato e devem ser aplicados sobre corpus real representativo.

Dados sintéticos e regressões de CI comprovam somente a implementação matemática e os invariantes fail-closed; não constituem homologação estatística.

## Limites operacionais

Esta fatia não altera scorer, m/u, prior, thresholds, blocking ou precedência determinística do CPF. Não cria ou funde UUID, não altera Gold/Serving, não ativa Linkage probabilístico e não escreve estado operacional de modelo/ruleset.
