# Diagnóstico de combinações de blocking

`BlockingCombinationDiagnostic` é uma camada analítica somente leitura para avaliar combinações OR de campos/passes candidatos a blocking sobre corpus rotulado independente.

Para cada combinação, registra:

- recall de vínculos verdadeiros preservados pela união;
- retenção de não-vínculos e razão de redução do espaço de pares;
- ganho incremental de recall sobre o melhor membro individual;
- ganho incremental de redução sobre o melhor membro individual;
- peso efetivamente observado.

O ranking prioriza recall de vínculos verdadeiros, depois redução do universo e ganho incremental. A intenção é medir complementaridade: uma combinação útil deve recuperar vínculos que os membros isolados perdem sem explodir desnecessariamente o universo de candidatos.

O diagnóstico não altera `BirthBlockingPlan`, não publica novos passes e não muda scorer, m/u, prior, thresholds, CPF, UUID, Gold ou Serving. Resultados em corpus sintético ou não representativo são apenas evidência técnica. Qualquer nova política depende de corpus representativo, avaliação independente da união/deduplicação, métricas de falso vínculo e aprovação explícita conforme a issue #31.
