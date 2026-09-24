# Avaliação independente das decisões finais do Runner V6

## Por que a avaliação por posterior não basta

O Runtime `FELLEGI_SUNTER_DECISION_EVIDENCE_V6` usa margem entre
candidatos em **log-odds**, guarda de núcleo demográfico exato não único
e guarda de segundo candidato, além do threshold de posterior. A
reexecução apenas de `score_melhor - score_segundo` NÃO reproduz essas
decisões, mesmo quando o score numérico parece alto.

`IndependentResolutionEvaluator.Evaluate`, o simulador anterior de
margem em posterior, passa a recusar explicitamente manifestos V6.

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
