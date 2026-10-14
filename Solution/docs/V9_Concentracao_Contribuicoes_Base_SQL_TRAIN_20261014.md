# V9 — concentração de contribuição por identidade-base no SQL TRAIN (pré-requisito de suficiência)

## Pergunta

A issue P0 #999 exige demonstrar se a massa de 30 mil pessoas-base fornece suporte suficiente para estimar **81 probabilidades m/u conjuntas**, evidência de nascimento e, futuramente, pesos TF condicionados à união física do blocking SQL. Uma quantidade grande de pares não equivale a uma quantidade grande de unidades independentes, porque a mesma pessoa-base pode aparecer em muitos registros e em vários pares U. O diagnóstico já existente (#1005, #1007, #1008) mede contagens de pares e número de pessoas-base distintas; faltava a **concentração de contribuição de cada pessoa-base** por célula.

## Unidade e algoritmo

`V9SqlBaseClusterConcentrationAudit` consome, **somente em TRAIN**, as mesmas listas ordenadas de candidatos físicos `#v9pairs` que alimentam simultaneamente as 81 células nominais da PR #1005 e os sete estados semânticos de nascimento da PR #1007. Antes de qualquer cálculo, exige igualdade de partição, ordinais e identidades-base entre as duas leituras, além de ausência de pares duplicados. Não busca pares de TEST nem mistura suporte de partições.

Publica para cada **classe M e U** de cada uma das 81 células nominais e 7 estados de nascimento (**176 células**):
- `ObservationPairs`: número de pares de observações recuperados pelo SQL na célula;
- `DistinctBaseContributors`: identidades-base distintas que participam dos pares daquela célula;
- `BaseContributionIncidences`: cada par M contribui **uma ocorrência da sua própria base**; cada par U contribui **uma ocorrência para cada uma das duas bases**, inclusive se várias observações repetirem a mesma dupla;
- `SquaredBaseContributionIncidences`: soma dos quadrados da quantidade de contribuições por base;
- `MaxBaseContributionIncidences` e `LargestBaseContributionShare`: maior contribuição individual e fração sobre a incidência total;
- `KishEquivalentBaseContributorsNotIndependentEss = (Σ contribuições por base)² / Σ contribuições²`, apenas quando há alguma observação; caso contrário **null**, sem imputação.

Este último índice é um **resumo da concentração**, que varia de 1 até o número de contribuintes-base distintos. **NÃO é um tamanho efetivo de amostra independente (ESS), variância robusta por cluster, intervalo de confiança, limite de erro de enlace nem número de pessoas-base observadas aleatoriamente.** Os pares U unem duas bases, compartilham endpoints e podem criar dependências de rede que o Kish de incidências não modela. Para um ESS inferencial seria necessário explicitar o plano amostral, dependências e técnicas de reamostragem adequadas a esse grafo.

### Reconciliação e segurança

A auditoria falha fechada se `Σ pares M/U` não corresponder exatamente às contagens da matriz nominal e do nascimento no **mesmo SQL TRAIN**, se a contribuição de cada classe não somar `M` ou `2×U`, se algum número de bases distintas exceder a massa-base TRAIN, se as listas de pares divergirem ou se houver células duplicadas. As células vazias preservam **null** nos índices; ausência de dados não significa peso TF 0, fator Bayes 1 ou parâmetro disponível.

Uma etapa Python independente na CI reconcilia as **176 células de cada uma das seeds 42/43/44**, confere somas, maior incidência, quadrados e fórmula de concentração, referenciando os dois relatórios preexistentes. Saem apenas contagens e índices agregados: não exportar nomes, datas individuais, CPF, IDs de observação, `base_person_id`, arestas ou chaves TF.

## Decisão sobre 30 mil e sequência

**Não há fundamento para declarar 30 mil bases suficientes só porque a CI passa.** Os índices agora mostram quão concentradas são as evidências, mas ainda não fornecem erro máximo garantido ou suporte independente em cada estado. O campo `EstimatesIndependentEss=false` e `MayPromoteModelParameters=false` é obrigatório em toda saída. Status `INCONCLUSIVE_KISH_CONCENTRATION_NOT_INDEPENDENT_ESS_NO_PARAMETER_PROMOTION`.

O próximo estudo deve pré-registrar orçamento de FP/FDR, precisão por estado e estrato, cálculo de incerteza levando em conta clusters e rede U, dimensões necessárias da massa-base, comparação do gate de 95% do Calibrador (outro frame/ruleset) com 43,16% de recuperação do SQL ATIVO+C1, seleção de passes com custos P95/P99 apenas em TRAIN/VALIDATION e calibração condicionada de m/u, prior e TF. Para TF, `f(value)` conjunto deriva de uma pessoa-base única em TRAIN, nunca das pontas repetidas deste audit nem do produto de marginais IBGE. TEST congelado só serve como avaliação posterior.

**Nenhuma mudança de ruleset ATIVO, V9 decisória, DT-15, `Rank/Resolve`, V8 histórico, IBGE original, JornadaLocal, NODE/NAS, Gold ou HML/PROD.** O ensaio só roda com `DOTNET_ENVIRONMENT=Development` e SQL efêmero `JornadaHarness`, com cap fail-closed dos candidatos SQL. O merge de diagnóstico não certifica readiness estatística ou operacional.
