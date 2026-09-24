# Avaliação independente das decisões finais do Runner V6

## Por que a avaliação por posterior não basta

O Runtime `FELLEGI_SUNTER_DECISION_EVIDENCE_V6` usa margem entre
candidatos em **log-odds**, guarda de núcleo demográfico exato não único
e guarda de segundo candidato, além do threshold de posterior. A
reexecução apenas de `score_melhor - score_segundo` NÃO reproduz essas
decisões, mesmo quando o score numérico parece alto.

`IndependentResolutionEvaluator.Evaluate`, o simulador legado de margem em
posterior, permite explicitamente somente V5. V6, V7 e versões futuras não
reconhecidas são recusadas.

## Nova API técnica

`IndependentResolutionEvaluator.EvaluateRecorded` recebe:
- manifesto independente, com fingerprints e denominadores;
- candidatos completos por observação, sem CPF/CNS usados como rótulo;
- decisões finais congeladas do MESMO run do Runner, incluindo estado
  `RESOLVIDO`, `CONFLITO` ou `NAO_RESOLVIDO`, melhor candidato e
  motivo estável quando existir;
- ID do run, versão do modelo e fingerprint do ruleset declarado;
- threshold posterior e margem log-odds do run;
- atestação explícita de que a lista da união de candidatos está completa.

Falha antes de emitir métricas quando falta decisão, há duplicata,
modelo/ruleset divergente, candidato vencedor fora do topo ou
resolução associada a um caso `CONFLITO`/não resolvido. Ordena o
corpus canonicamente e produz fingerprint distinto da avaliação
contrafactual. Brier e candidate recall só são interpretáveis com o
conjunto completo e a ordenação real de melhor candidato.

Ela usa **o resultado do runtime** para classificar, não recalcula a
decisão a partir de posteriors. Nunca grava em
`identity_map`, `vinculo_fonte`, Gold ou Serving e nunca ativa modelo.


## Avaliação amostral ponderada das decisões registradas

A avaliação por desenho amostral agora possui
`IndependentResolutionSurveyEvaluator.EvaluateRecorded` e
`IndependentResolutionGovernedSurveyEvaluator.EvaluateRecorded`. Ambas
reutilizam o gate descritivo de decisões finais congeladas: exigem o mesmo
run/modelo/ruleset, decisões exaustivas, candidatos completos e os parâmetros
operacionais declarados. A versão governada exige também a proveniência dos
pesos de seleção, não resposta e calibração.

Os estados `RESOLVIDO`, `CONFLITO` e `NAO_RESOLVIDO` vêm do Runner, não da
diferença de posteriores. Os pesos institucionais são aplicados a acertos,
falsos vínculos, perdas, candidate recall e Brier; a incerteza continua
estimada por jackknife de conglomerados independentes. Se uma razão não for
identificável em qualquer réplica, não se emite intervalo artificial para ela.
O fingerprint distingue avaliação registrada de contrafactual e incorpora a
identidade do run, as decisões finais, o desenho e, na camada governada, a
proveniência dos pesos. Não são publicados CPF, UUID ou dados pessoais nesses
relatórios.

O replay contrafactual por posterior (`Evaluate`) opera por lista explícita
de permissão: apenas o algoritmo legado
`FELLEGI_SUNTER_SEMANTIC_BIRTH_V5`. V6, V7 experimental e versões futuras
não reconhecidas devem usar `EvaluateRecorded`, jamais receber métricas de
uma aproximação legada silenciosa. V7 permanece experimental e não é
considerado homologado por existir uma API de avaliação.

Esses métodos continuam sendo avaliadores somente leitura. Não coletam
automaticamente o export institucional de HML, não validam a independência da
verdade e não substituem corpus representativo, avaliação por estratos nem
aprovação estatística da issue #31.

## Limites e próximo passo institucional

Esta API NÃO exporta por si só as decisões congeladas de HML e NÃO
verifica a independência da verdade declarada no manifesto. O extrator
institucional deve congelar run/modelo/ruleset, exportar a união completa
de candidatos e fornecer rótulos positivos e negativos independentes
com desenho amostral governado. Se só houver melhor/segundo candidato,
não declarar `CandidateRecoveryRate` da união completa.

A estimativa populacional e os critérios de homologação continuam
dependendo do corpus representativo e da decisão #31. Testes com casos
sintéticos demonstram comportamento matemático e fail-closed,
não qualidade estatística no território.
