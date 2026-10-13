# V9 — suporte observacional m/u das 81 células nominais no SQL físico TRAIN

## Objetivo e contexto

O censo físico SQL do `JornadaHarness` recuperou 7.786/18.036 pares M verdadeiros elegíveis em TEST de três seeds sintéticas, sem prova de desempenho suficiente para produção. As PRs #1003 e #1004 decompuseram as perdas de M e verificaram paridade lógica/física dos passes ATIVOS mais C1 **no mesmo conjunto M SQL**, sem confundir o recall físico de ~43% com o piso do Calibrador de 95% medido em outra amostra e possivelmente outro ruleset.

O próximo passo de calibração exige medir **m e u conjuntamente** em todas as 81 combinações de níveis pessoa×mãe no **mesmo universo SQL de candidatos**, e não reutilizar os negativos de mesmo prenome do ensaio proxy. A presente entrega instrumenta **apenas suporte e concentração de dados em TRAIN**, antes de autorizar qualquer estimação de pesos, priors, TF ou thresholds.

## Contrato da instrumentação

`V9SqlConditionedNominalSupportAudit` consome **todos os pares SQL físicos do split TRAIN** extraídos de `#v9pairs` depois da união C1+ATIVO, entre gestores, sem CPF, com nascimento presente nas duas pontas (frame do censo). Cada par é rotulado M se os dois registros possuírem a **mesma identidade-base sintética** e U caso contrário. O classificador usa exatamente `CompositeNominalComparisonV9.Classify`, para a pessoa e para a mãe, com nove níveis por lado: `Missing`, `ExactFull`, `ExactFirstLast`, `ReversedFirstLast`, `HighFull`, `HighFirstLast`, `ExactLast`, `ExactFirst`, `Other`. A precedência é mutuamente exclusiva, produzindo **9×9=81** células, inclusive as vazias.

Para cada célula reporta-se `MCount`, `UCount`, `DistinctMBasePeople` (quantidade de indivíduos-base distintos que geraram M), `DistinctUBasePairs` (pares únicos e **não ordenados** de indivíduos-base distintos que geraram U) e `DistinctUBasePeople` (total de bases distintas que aparecem nos pares U daquela célula). Contam-se **pares de observações** para `MCount/UCount`; reportam-se grupos de base separadamente para não confundir múltiplas observações do mesmo indivíduo com grupos independentes.

A auditoria devolve apenas contagens e rótulos de estados. `base_person_id`, IDs de observação, nomes, datas e amostras individuais são **mantidos exclusivamente em memória durante o ensaio** e não exportados em JSON. Ela proíbe pares duplicados, dados de VALIDATION ou TEST, ausência de identidade-base e falta de suporte bilateral M e U no split TRAIN. A soma de todas as células deve igualar exatamente `SqlCandidateTruePairs` e `SqlCandidateFalsePairs` de TRAIN. A CI repete a validação em Python e produz a matriz para **cada seed**, sem ajuste de modelo nem seleção por TEST.

## Interpretação estatística e limitações

`DistinctMBasePeople` e `DistinctUBasePairs` **não são ESS estimados**; são contagens de grupos distintos relevantes para diagnóstico de concentração. Pares U podem compartilhar o mesmo indivíduo-base, então mesmo pares U distintos não são independentes. Ausência de suporte M e/ou U em uma célula não pode ser convertida em probabilidade calibrada com smoothing arbitrário. Percentuais derivados dessas frequências são, no máximo, **empíricos no frame sintético selecionado pelo SQL**; não são frequências da população brasileira, priors municipais nem probabilidades de vínculos verdadeiros sem correção do desenho de amostragem.

O TF precisa considerar **f(value) por uma pessoa-base única em TRAIN**, já instrumentado pela PR #1000, e não a distribuição de pontas de pares deste audit. As marginais nominais oficiais IBGE congeladas são aplicáveis somente às unidades semanticamente compatíveis; nome completo e primeiro+último conjunto não se inferem multiplicando marginais IBGE. As células nominais, nascimento, prior, TF, pesos, limiar e orçamento FDR ainda precisam de uma política **TRAIN/VALIDATION versionada, suportada e independente**, seguida de verificação em TEST congelado.

**Estado explícito:** `TRAIN_SQL_CANDIDATE_COUNTS_ONLY_NO_INDEPENDENT_MU_PRIOR_TF_OR_PRODUCTION`. Não produz coeficientes `m/u`, não recalibra FS antigo, não altera blocking, não ativa V9/DT-15, não muda `Rank/Resolve`, V8 histórica, `JornadaLocal`, IBGE original, NODE/NAS, Gold ou HML/PROD. Executado somente no banco efêmero `JornadaHarness` com `DOTNET_ENVIRONMENT=Development`, sujeito a cap fail-closed de 1 milhão de candidatos. O gate de 95% não é reclamado como atendido por esta PR.
