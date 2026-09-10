# Diagnóstico estatístico de campos para blocking

## Objetivo

`BlockingFeatureDiagnostic` é uma ferramenta analítica, somente leitura, para avaliar campos/comparadores candidatos a blocking antes de qualquer alteração da política operacional.

Ele não escolhe nem publica automaticamente um novo `BirthBlockingPlan`.

## Métricas por campo

Para cada predicado de concordância elegível, o diagnóstico calcula:

- **TrueMatchRecall**: proporção ponderada de vínculos verdadeiros de referência preservados pelo campo. Ausência do campo conta como não preservação, porque um blocking que depende dele perderia aquele vínculo.
- **NonMatchRetention**: proporção ponderada de não-vínculos que ainda permaneceriam candidatos.
- **ReductionRatio**: `1 - NonMatchRetention`, aproxima a redução do espaço de comparação.
- **AgreementLogLikelihoodRatio**: poder discriminante da concordância, usando suavização explícita de 0,5 apenas para evitar probabilidades nulas no diagnóstico.
- **MissingRate**: proporção ponderada sem valor observável para o comparador.

O ranking padrão prioriza recall dos vínculos verdadeiros, depois redução de pares e, por último, poder discriminante. Isso evita escolher como blocking um campo muito raro/seletivo que elimine candidatos verdadeiros.

## Dependência entre campos

Para cada par de campos é calculada a correlação phi ponderada entre os indicadores de concordância quando ambos são observáveis. Correlação alta sinaliza redundância potencial: dois campos aparentemente fortes podem representar quase a mesma informação.

Esse valor é diagnóstico, não prova independência causal nem substitui análise multivariada no corpus real.

## Governança

O corpus deve conter vínculos e não-vínculos de referência e pesos positivos. O resultado só pode fundamentar recomendação de novos passes depois de:

1. corpus representativo e separado da calibração operacional;
2. verdade de referência governada;
3. avaliação de recall e custo por passe e por união de passes;
4. análise de dependência entre evidências;
5. aprovação explícita de uma nova versão do blocking.

Nenhuma saída deste componente altera scorer, m/u, prior, thresholds, CPF, UUID, Gold ou Serving.

## Limite importante

Uma coluna não deve ser escolhida apenas porque possui maior correlação ou maior razão de verossimilhança. Blocking é um problema de cobertura e custo: o objetivo é preservar praticamente todos os vínculos verdadeiros enquanto se reduz de forma útil o número de pares candidatos. Por isso a política final normalmente é composta por passes complementares, cuja união precisa ser avaliada e deduplicada.
