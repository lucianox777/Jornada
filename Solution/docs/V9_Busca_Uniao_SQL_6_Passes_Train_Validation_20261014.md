# V9 — busca SQL física por união deduplicada de seis passes em TRAIN/VALIDATION

## Motivação P0 (#999)

As ablações SQL de #1001 mediram seis regras **individualmente contra o mesmo baseline ATIVO+C1**. Os ganhos por passe se sobrepõem, portanto **somá-los não estima o recall nem o custo de ativar dois ou mais passes**. A seleção operacional de regras requer avaliar a união física deduplicada, com contagens M/U, custo de candidatos e recall sobre **todos os M elegíveis antes do blocking**. Não é permitido usar TEST para selecionar configurações, e o gate de 95% do Calibrador refere-se à sua própria amostra/ruleset, não automaticamente ao SQL físico.

## Método implementado

`V9SqlTrainValidationUnionSearch` usa uma cópia efêmera de todos os pares SQL `#v9pairs` recuperados pelo baseline em TRAIN e VALIDATION e os pares adicionais das **mesmas seis ablações SQL pré-registradas**, lidas pela própria query de cada passe. A coleta incremental reaproveita as consultas existentes e não altera o baseline nem a tabela SQL de regras ATIVAS. Para toda extensão, o par possui ordinais canônicos `a<b`, a partição e o rótulo sintético M/U independente.

O algoritmo enumera deterministicamente **todas as 56 combinações de 1, 2, 3 ou 4 passes** (6 + 15 + 20 + 15, limite de 2.048 configurações), aplica união por pares `(a,b)` sem duplicatas e recalcula M e U de cada configuração por TRAIN e VALIDATION. A cada passo exige:
- Cada passe contém somente pares adicionais fora do baseline; duplicatas internas, discordâncias de rótulo/partição para mesmo par ou entrada TEST abortam a busca.
- `M_union<=M_elegível`, os denominadores M são obtidos antes do SQL, todos os pares obedecem ao cap físico fail-closed de **1 milhão**, nenhum `TOP N` ou corte silencioso.
- `M_union = M_baseline + M_extra_deduplicado` e idem para U; as somas marginais dos passes constituem apenas limites superiores, **nunca o ganho presumido** de sua união.
- O gate de 95% é avaliado por **inteiros exatos**, `20*M_union >= 19*M_elegível`, tanto em TRAIN quanto em VALIDATION. A quantidade mínima exigida é `ceil(19*M_elegível/20)`, sem arredondamento otimista.

Para fins **exclusivamente consultivos**, se houver combinação que cumpra ambos os pisos, a busca ordena candidatos por **menor quantidade de U em TRAIN, depois U em VALIDATION, maior recall de TRAIN, menos passes e assinatura determinística**. O relatório contém `AdvisoryBestSignatureNotPublished`, que é explicitamente **não publicável** e `null` se nenhuma configuração cumprir as duas metas. Não há ainda orçamento governado de FP/FDR, suporte condicionado, versões históricas `VERSIONED_ALIAS`, ajuste de prior/m/u/TF, intervalo por cluster ou prova de escala; portanto `MayPromoteBlockingPolicy=false` **sempre**, mesmo se a combinação atingir 95% no experimento sintético.

A CI verifica com Python independente as 56 combinações e suas assinaturas, M/U do baseline e de cada ablação individual, a identidade das somas, cotas superiores, cap, denominadores, percentuais e a escolha consultiva. Testes NUnit cobrem overlaps reais entre passes, falha em vazar TEST, M/U discordante entre passes e cap. Os resultados continuam vinculados à seed/fingerprint dos três corpora experimentais. Por padrão não há reaproveitamento de uma seleção da seed 43/44 para reajustar a seed 42 ou mesmo o TEST: cada seed de TRAIN/VALIDATION somente fornece **sensibilidade analítica não promotora**.

## Limites, risco e próxima rodada

**Esta não é uma seleção/ativação de política.** O mínimo do Calibrador em TRAIN usa outro universo e outro ruleset; alcançar ou falhar 95% no SQL atual não valida nem invalida automaticamente o gate histórico. A prova completa exige replicar no mesmo frame os campos, amostragem, gabaritos, regra candidata e a execução física, com fingerprints de ambos; depois comparar recall sem trocar denominador. A documentação canônica preserva a diferença.

O risco de U adicional e fan-out P95/P99 deve ser medido em cada união física antes de seleção; os resultados atuais fornecem contagem global de pares U, **não distribuição de fan-out por união**. O parâmetro TF deve ser estimado por pessoas-base TRAIN únicas no frame sintético IBGE-derivado e por marginais IBGE congeladas somente para unidades compatíveis. Uma célula sem M/U observado não ganha legitimidade com alpha arbitrário. A massa de 30 mil não está certificada suficiente; falta suporte inferencial robusto por clusters de identidade-base.

**Sem mudanças operacionais:** somente `JornadaHarness` isolado e `DOTNET_ENVIRONMENT=Development`. Não ativa V9 nem DT-15, não altera Rank/Resolve, V8 histórica, IBGE original, JornadaLocal, NODE/NAS, Gold ou HML/PROD. CI verde garante somente correção da instrumentação, não readiness estatística.
