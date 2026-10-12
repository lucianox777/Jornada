# V9 — auditoria read-only da amostra U efetivamente condicionada ao blocking

**Data:** 12/10/2026. Continuação da migração governada [#946](https://github.com/lucianox777/Jornada/issues/946), após as PRs #947 (comparação composta) e #948 (distribuições m/u diagnósticas).

## Escopo desta entrega

O `GENERATE_DRAFT` da implementação operacional **V8 histórica**, após ler o universo de pares do ruleset vencedor via `BlockingConditionedUnmatchedPairReader.ReadAsync`, invoca uma **auditoria V9 independente, read-only**. Essa auditoria recebe:

- Os pares **U** realmente amostrados do pool formado pelas chaves materializadas de blocking (mesmo conjunto de passos selecionados), e sua cardinalidade agregada;
- Os pares **M** inter-Gestores rotulados por CPF independentes do próprio score;
- Os passos ordenados e normalizados, versão da normalização textual, versão/fingerprint da projeção física, sem os nomes no fingerprint;
- O `smoothingAlpha` de treinamento, para produzir estados nominais compostos de pessoa e mãe.

O componente `CompositeNominalBlockingAuditBuilderV9` produz apenas agregados `m/u` preliminares, suporte observado/ausente, cardinalidades e SHA da **configuração** de blocking. Nenhum nome, valor de token, UUID de pessoa ou par é exposto nos logs, no diagnóstico ou no hash. O uso da amostra é em memória e não altera `modelParameters`, TF, thresholds, scores, vínculo ou `ACTIVATE`.

## Restrição estatística importante

**Somente U já é condicionado ao blocking selecionado.** O conjunto M inter-Gestores ainda **não foi demonstrado como recuperado pelo mesmo ruleset**. Os m/u do relatório **não são** valores aprovados para scoring, e o status explicita: `DIAGNOSTIC_ONLY_M_NOT_BLOCK_CONDITIONED`.

O fingerprint declara a configuração de passes e projeções usadas, não prova o conjunto de pares nem independência, ausência de seleção enviesada ou estratificação. TRAIN e corpora independentes de VALIDATION/TEST continuam sujeitos aos gates próprios. Não atribuir `AlgorithmVersion=V9` nem ativar a V9 a partir deste incremento.

Se faltar suporte nominal em ambas as classes para pessoa ou mãe, retorna **`UNAVAILABLE_INSUFFICIENT_NOMINAL_SUPPORT`**, sem distribuir pseudocontagens artificiais no atributo ausente. O diagnóstico insuficiente **não bloqueia o draft histórico V8**, mas permanece inelegível para publicação V9.

## Dependências antes da próxima integração operacional

1. Projetar os pares M sobre o **mesmo ruleset** com avaliação de inclusão, grupos independentes e pesos de desenho.
2. Estimar m/u ponderados, suporte e incerteza por estrato, com segregação TRAIN/VALIDATION/TEST e amostra positiva suficiente.
3. Ajustar TF por unidade estatística equivalente (nome integral, par primeiro+último ou componente pertinente), sem pressupor produto de marginais IBGE nem somar evidências dependentes.
4. Conferir quantitativamente níveis/LLR com Splink independente; certificar FDR/FP e recall no orçamento aprovado.
5. Integrar `AlgorithmVersion=V9` no único scorer C# apenas sob contrato versionado, preservando replay V8; aprovação/ativação governadas pela DT-15.

**CI verde demonstra apenas correção funcional desse diagnóstico; não demonstra validade empírica de V9.**
