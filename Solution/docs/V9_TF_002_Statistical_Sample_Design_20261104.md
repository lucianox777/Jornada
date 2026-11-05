# V9-TF-002 — dimensionamento prospectivo da estabilidade

**Status: desenho condicional, não certificação.** Implementação de cálculo no
módulo C# preexistente V9StatisticalSamplePlan, preservando a família
histórica de 3.861 comparações e a meta de FDR de 1%.

## Hipóteses explícitas

Para o estudo futuro de **transferência congelada** seed42 → seed43:
- um único contraste pareado ONE_MF contra TF0 por identidade-base;
- dois modelos finalistas congelados na VALIDATION; um só modelo
  em um **novo holdout confirmatório independente**;
- confiança familiar de 95% e poder do contraste de 80%;
- orçamento alfa dividido igualmente: 0,025 para a família de FDR
  (Bonferroni dos dois modelos na VALIDATION) e 0,025 para
  o contraste pareado predeclarado;
- ganho mínimo relevante de recall +0,36 ponto percentual;
- até quatro observações-fonte aceitas por identidade-base;
- médias hipotéticas: 1,35 aceites, 1,30 fontes elegíveis por identidade;
- partições prospectivas: 20% VALIDATION e 20% holdout independente,
  retenção de 90% das identidades sorteadas.

O número histórico de 3.861 comparações **não desaparece** da
análise exploratória da campanha TF-001. O orçamento de dois finalistas
aplica-se **somente** a um experimento confirmatório novo, previamente
congelado, sem inspeção/adaptação nos resultados do novo VALIDATION.

## Método

O método CalculateFrozenTransfer reutiliza CalculateFinalistFdrOnly
para o cenário hipotético **zero falso aceite**, com cap por identidade,
média prevista de aceites e multiplicidade. Não altera nem substitui
V9IdentityClusterFdr ou V9RecordedClusterFdrValidation.

Para o contraste pareado, defina X_i = TP_ONE_MF,i - TP_TF0,i
por identidade-base. X_i pertence ao intervalo [-cap, cap].
Em vez de tratar os pares de fontes como ensaios Bernoulli independentes,
usa-se um teste unilateral conservador de Hoeffding para E[X] > 0.
O poder é baseado na hipótese E[X] = ganho × fontes elegíveis esperadas
por identidade-base. Isso requer unidades-base independentes entre si
e observações com o cap efetivamente imposto; dependência entre
identidades via competição de candidatos **invalida** esta leitura.

Para validar a contribuição da incerteza, usar um estudo externo sobre
grafo de competição, desenho de amostragem, representatividade,
métricas por unidade e testes repetidos, seguindo
V9_Revisao_Independencia_Multiplicidade_Amostra_20261019.md.

## Cenário quantitativo, NÃO requisito definitivo

Com os números hipotéticos acima, os cálculos indicam aproximadamente:

| Componente | Identidades-base independentes |
| --- | ---: |
| Contraste pareado de recall, poder 80% | 14.860.865 na VALIDATION |
| FDR ≤ 1%, cenário com zero FP e 2 finalistas | 1.696 na VALIDATION |
| FDR ≤ 1%, cenário com zero FP e 1 modelo | 1.465 no novo holdout |
| População de origem com frações/retensão declaradas | 82.560.362 |

O contraste pareado domina esse cenário conservador. **Não** quer
dizer que 82 milhões de pessoas reais sejam exigidas: os tamanhos
dependem do modelo de concentração, da variância entre identidades,
da diferença de interesse, do cap e dos pressupostos. Um ensaio piloto
independente ou método pareado validado pode permitir um desenho
mais eficiente, **antes** de observar o novo resultado confirmatório,
com orçamento de testes e justificativa transparente.

FDR de 1% não é relaxado; o cenário zero FP não fornece poder ou
probabilidade de alcançar zero erros. O TEST da seed43 já foi
inspecionado em estudos anteriores e **não é holdout confirmatório novo**.
Novo seed tampouco prova IID ou generalização para dados reais.

## Gates

- Sem verificação independente de unidades e competição, recusar
  certificação mesmo com limite numérico inferior a 1%.
- Sem P95 físico, recusar alegação de desempenho operacional.
- Sem novo holdout íntegro, recusar confirmação externa.
- Nenhuma PR ou cálculo habilita DT-15; seleção pelo calibrador,
  autorização operacional pela autoridade em etapa separada.

Testes de regressão: V9FrozenTransferSamplePlanTests, executados
na suíte NUnit existente. Evidência física e certificação: pendentes.
