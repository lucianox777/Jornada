# V9 — identificar o resíduo físico por três âncoras SQL, sem escolher política

## Fato verificado: as 11 famílias existentes ainda falham o recall de 95%

O artefato **real** `v9-physical-sql-active-candidate-wrong-link-30k` da CI `38504715191` (PR #1018) contém os relatórios físicos dos corpora sintéticos de seeds **42, 43 e 44**, cada um com 30 mil pessoas-base únicas. O SQL experimental `ATIVO+C1 + seis passes antigos + cinco passes de ano/fonética`, com uniões deduplicadas, continua recuperando só **70,78%–72,11%** dos M elegíveis em TRAIN/VALIDATION. **Nenhuma** dessas seis partições alcança 95%. Os resultados usam apenas TRAIN/VALIDATION para a exploração das famílias e o denominador real de M antes do blocking, não o piso configurado no Calibrador amostral.

| Seed | Partição | M elegíveis | M recuperados 11 passes | M não recuperados | M adicionais necessários para 95% |
|---|---|---:|---:|---:|---:|
| 42 | TRAIN | 18.315 | 13.045 | 5.270 | 4.355 |
| 42 | VALIDATION | 6.070 | 4.340 | 1.730 | 1.427 |
| 43 | TRAIN | 18.326 | 12.971 | 5.355 | 4.439 |
| 43 | VALIDATION | 6.122 | 4.395 | 1.727 | 1.421 |
| 44 | TRAIN | 18.230 | 13.145 | 5.085 | 4.174 |
| 44 | VALIDATION | 6.039 | 4.305 | 1.734 | 1.433 |

Entre os **20.901 M ainda não recuperados** nesses seis splits, a tabulação antiga baseada em igualdade **exata** do nome/mãe/nascimento aponta 9.445 M com os três valores divergentes; 6.823 com nome divergente, mãe ausente e DOB diferente; e 2.525 com nome divergente, mãe ausente e DOB exato. A classificação é **descritiva, não causal**: não atribui a perda a um mecanismo do gerador, nem permite inferência para HML/PROD.

## Nova análise: âncoras realmente testadas no SQL, por par M elegível

As três âncoras extraídas dos **mesmos campos normalizados usados nos JOINs físicos** são:

1. `P`: **fonética do nome da pessoa** igual em ambas as pontas e não vazia;
2. `M`: **fonética do nome da mãe** igual em ambas as pontas e não vazia;
3. `Y`: **ano civil de nascimento** igual em ambas as pontas, nascimento presente como exige o recorte SQL.

A família já executa em SQL as três conjunções `P∧Y`, `M∧Y` e `P∧M`, com filtros por partição TRAIN/VALIDATION, gestores distintos, CPF ausente e DOB presente. Portanto, no mesmo frame físico, **todo par M elegível que tenha pelo menos duas dessas três âncoras iguais deve constar na união dos 11 passes**. O novo `V9SqlTwoSignalResidualAudit` testa essa implicação como **invariante falha-fechada**, usando as etiquetas do gabarito enumerado **antes** do SQL, os sinais das observações elegíveis e os pares M efetivamente presentes na união deduplicada. Não deduz candidatos de uma simulação da consulta; confere o resultado SQL executado.

O relatório `ThreeAnchorResidualAudit` dentro de `ElevenPassResidualTrainValidation` cria **8 máscaras booleanas P/M/Y por split** (**16 linhas em duas partições**), com M elegíveis, recuperados e residuais por máscara. Inclui totais residuais com **zero âncoras iguais**, **exatamente uma** e **duas ou três**; residuais com fonética materna ausente; residuais com ano diferente. Nunca se considera **NULL** ou cadeia vazia fonética uma concordância neutra; o valor desconhecido não vira fator de Bayes 1, nem peso TF 0/1, nem sinal de igualdade automática. A CI Python verifica independentemente todas as 16 máscaras por seed, reconciliando com M do relatório físico de 11 passes e assegurando que o número residual com duas ou mais âncoras iguais seja **zero**. Testes NUnit cobrem essa implicação, split TEST proibido, duplicatas, IDs não canônicos e zero/um sinal.

## Interpretação: limite de uma classe restrita, não do blocking em geral

Se a auditoria passa, ela demonstra apenas que **nenhum novo par M residual pode ser recuperado pelas três conjunções exatas dessas mesmas âncoras**: `P∧Y`, `M∧Y`, `P∧M` já foram cobertos. Ela **NÃO** prova que toda política de duas evidências possíveis é incapaz de chegar a 95%. Permanece espaço para novas formas de concordância aproximada, aliases/nomes históricos, janelas temporais, relações condicionais, evidências de origem com contrato válido, fonética mais robusta, revisão humana e recusas/abstenções. **Não autoriza um passe de atributo único** nem equipara DOB ausente a uma divergência. Tampouco propõe transformar todo `NOVO` em obrigatório: disponibilidade de atributo básico é definida pelo contrato da fonte.

Este incremento **não recalibra** `m/u`, prior, TF, limiar de aceitação, universo de U, nem modela FDR de vínculos aprovados. A análise de M depende de identidade-base e do gabarito sintético; a suficiência estatística de 30 mil pessoas-base continua não demonstrada por mera contagem de pares ou número de seeds. Bootstrap M por cluster e bootstrap U por rede continuam diagnósticos, não intervalos de independência de pares nem confiança de generalização operacional.

## Barreiras e entrega

Novo relatório: `V9_SQL_PHYSICAL_THREE_ANCHORS_RESIDUAL_IDENTIFIABILITY_V1` com `IsGlobalBlockingCeiling=false` e `MayPromoteBlockingPolicy=false`. O código nunca exporta valores fonéticos, nomes, mães, DOBs, IDs, arestas nem CPF; apenas 16 células e métricas agregadas.

Sem alteração em passes SQL executados, ATIVO+C1, DB `JornadaHarness` restrito a `DOTNET_ENVIRONMENT=Development`, V8, Rank/Resolve, V9 operacional, DT-15, marginais IBGE originais, JornadaLocal, NODE/NAS, Gold, HML/PROD. O teste de admissibilidade operacional e o confronto entre o piso amostral do Calibrador e o mesmo frame SQL permanecem pendentes.
