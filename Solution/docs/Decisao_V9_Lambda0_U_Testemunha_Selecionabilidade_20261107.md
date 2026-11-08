# V9-U0-WITNESS-20261107 — λ=0 U-misto como testemunha, não candidata

**Decisão do PO:** 07/11/2026, frente B #1310. **Estado:** DECIDIDO; implementação na PR #1310, aguardando integração e pós-merge/CI cumulativa. **Autoridade:** Product Owner. **Governança:** [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md). Não é certificação FDR.

## Contexto e fundamento

A calibração U-raro compara V2 histórica, V3 histórica e quatro pesos de mistura U, preservando a grade física de 24 avaliações por margem (TF0/ONE_MF × 3/19 passes). O braço U-misto de `lambda=0` reproduz numericamente a V3, mas possui `OptionId`, `RequestSha256` e proveniência de snapshot próprios. Mantê-lo como candidato independente em alguma política de multiplicidade duplicaria numericamente uma hipótese e permitiria seleção espúria; excluí-lo da execução física apagaria a testemunha de compatibilidade.

## Deliberação, precedência e impactos

1. `lambda=0` U-misto permanece **fisicamente executado e auditado**, com SHA próprio. A equivalência numérica com V3 histórica deve ser provada no quadro original; não se alteram evidências congeladas, SQL, TRAIN, M/U histórico, TF, prior nem TEST.
2. A testemunha é excluída **da seleção**: nenhuma fronteira de Pareto, refinamento adaptativo ou finalista, em `COUNT_ALL` e `COUNT_UNIQUE`, pode selecionar seu snapshot. O conjunto de candidatos elegíveis é idêntico nas duas políticas.
3. Contagens distintas são registradas: `PhysicalHypotheses` inclui braços medidos elegíveis no quadro; `EligibleHypotheses` exclui a testemunha; `DescriptiveMultiplicityHypotheses` explicita a convenção descritiva (`COUNT_ALL` inclui testemunha na contagem, `COUNT_UNIQUE` a exclui). **Nenhuma** constitui por si correção de dependência entre pares, garantia IID ou FDR independente. `IndependentlyCertified=false`.
4. O auditor deve reprovar se SHA da testemunha aparecer em fronteira/finalista ou se as contagens não conciliarem. A comparação histórica de estudos anteriores permanece analítica, sem reinterpretar TEST/Gold.
5. Esta decisão complementa a [especificação de U-raro](V9_V3_U_Rare_Mixture_Experiment_20261106.md) e é anterior à composição final do motor multiparâmetro V9; a opção **V2 testemunha permanente** é outra decisão do PO, não substituída por esta testemunha numérica `lambda=0`.

**Verificação:** CI rápida na PR #1310 após rebase/conciliação, `jornada-ci` e prova técnica 30k no SHA da master após merge. **Separar:** DECIDIDO, IMPLEMENTADO, MEDIDO, VALIDADO e ATIVADO.
