# V9 — influência de grupos independentes no diagnóstico de holdout nominal

**12/10/2026, etapa 10 (issue #946).** Continuação da PR #957, que construiu a avaliação isolada TRAIN × EVALUATION da distribuição conjunta de 81 estados do nome da pessoa e da mãe. Este documento é técnico; **não é decisão de ativação, nova norma nem certificação da população operacional**.

## Problema observado

A distância de variação total (TVD) entre as distribuições TRAIN e EVALUATION pode parecer baixa em uma amostra pequena mesmo quando os resultados dependem de **um único grupo independente**. Os pares de uma identidade/gestor podem ser correlacionados. O ESS de Kish existente considera pesos de **pares**, não a dependência entre grupos. Assim, relatar TVD sem investigar a influência dos grupos pode induzir falsa confiança em uma tabela de calibração V9.

## Incremento: perturbação leave-one-independent-group-out (LOO)

A PR adiciona `CandidateJointHoldoutGroupSensitivityV9` ao diagnóstico de cada classe independente M e U, sem alterar o estimador TRAIN, o score ou o corpus. As observações EVALUATION continuam separadas do treinamento por `IndependenceGroupId`.

Para cada classe com **três ou mais grupos independentes**, a auditoria:
1. Calcula a TVD observada da distribuição EVALUATION completa contra a distribuição TRAIN *empírica ponderada*, antes da suavização.
2. Exclui **temporariamente um grupo EVALUATION inteiro por vez**, mantendo intactos os parâmetros e suportes TRAIN. Recalcula as frequências de célula por `DesignWeight` e a TVD das 81 células.
3. Devolve **somente agregados**: menor e maior TVD LOO, maior variação absoluta da TVD relativamente à avaliação completa e maior fração ponderada de EVALUATION nas células **sem suporte observado em TRAIN** após uma exclusão. Conta grupos e exclusões efetivamente realizados, **sem jamais devolver identificadores de grupos ou pares**.
4. Se a classe possuir somente **dois grupos** (mínimo existente do holdout), não tenta interpretar a eliminação de um grupo como avaliação ainda independente: retorna `UNAVAILABLE_LT_3_INDEPENDENT_EVALUATION_GROUPS` e métricas nulas. A classe continua válida para o diagnóstico de TVD básica — **não** ganha uma falsa conclusão LOO.

A avaliação positiva utiliza `DIAGNOSTIC_ONLY_GROUP_INFLUENCE_NOT_CI`. Um teste com três grupos independentes e uma alteração nominal restrita a somente um dos grupos demonstra TVD total de 1/3, extremos LOO de 0 e 1/2, e variação máxima de 1/3; o TRAIN permanece bit a bit idêntico na serialização agregada.

## Interpretação e limites obrigatórios

- **LOO é teste descritivo de influência, não intervalo de confiança**. Extremos de deleção não fornecem cobertura de 95%, p-valor, teste de equivalência ou controle de erro familiar. Mais grupos e um método de inferência apropriado ao desenho por conglomerados são necessários antes de quantificar incerteza populacional.
- O agrupamento respeita os **IDs de independência já conferidos** por `CandidateLabeling.Validate`; a decomposição é apenas da classe em EVALUATION. Não reamostra rótulos, não altera partições e não escolhe limiares.
- O desenho `CandidateSamplingDesign` e o `BirthBlockingPlan` **não garantem equivalência** com o universo do `blocking_chave` SQL dinâmico/temporal `VERSIONED_ALIAS`. Nem um LOO estável autoriza aplicar m/u ou TF na V9 operacional.
- A matriz de 81 estados pode ser esparsa. Uma única classe/célula nominal sem suporte genuíno não passa a ter evidência empírica por causa da pseudocontagem.
- `EVALUATION` não substitui automaticamente o conjunto `TEST` governado; interpretar estes extremos para **selecionar** hiperparâmetros e depois relatá-los como validação independente introduz viés.
- A decisão canônica [DC-LK-TF](Decisoes_Canonicas_Identidade_Linkage_20260929.md) permanece vinculante. Marginal IBGE de sobrenome em qualquer posição só pode ser utilizada como **proxy inicial explicitamente identificado**, conforme [retificação #955](Justificativa_Retificacao_Proxy_IBGE_Ultimo_Sobrenome_V9_20261012.md), não como TF conjunta ou posicional empiricamente exata.

## Escopo e próximos gates

A entrega modifica apenas a biblioteca de auditoria, testes e documentação. Não toca `GENERATE_DRAFT`, `Rank/Resolve`, parâmetros, `ACTIVATE`, banco ou modelos. A V8 segue exclusivamente para replay histórico e a V9 ainda não foi habilitada.

Antes de eventual publicação: certificar o **mesmo frame SQL materializado para M e U** com inclusão/pesos e sem vazamento, validar versão/proveniência das evidências brutas, ampliar grupos/células e estimar incerteza por desenho, estimar TF no frame compatível, executar **conferência matemática independente do C#** (Splink externo **opcional**, não gate obrigatório) e avaliar em TEST FP/FDR/recall sob DT-15. **DT-10 permanece por último.**

**Ressalva para 30 mil:** LOO em poucos grupos é apenas sensibilidade descritiva; nenhuma quantidade de linhas sintéticas prova cobertura dos 81 estados, independência real ou FDR. A [avaliação de suficiência](Parecer_Suficiencia_Bootstrap_30mil_V9_20261012.md) exige denominadores/grupos, suporte observado e incerteza antes de alegar completude da calibração.
