# V9 — suporte M/U de nascimento semântico e binário no SQL físico TRAIN

## Motivação

A issue #999 exige estimar a evidência de nascimento **no mesmo universo SQL** que fornece os 81 estados nominais de pessoa×mãe. O modelo sintético anterior foi ajustado em negativos proxy por inicial do nome, que não representam a distribuição U efetivamente recuperada pelo SQL ATIVO+C1. As PRs #1005/#1006 já fornecem, no mesmo `JornadaHarness`, respectivamente, suporte nominal de 81 células em TRAIN e fan-out físico real dos candidatos inclusive observações com grau zero.

Esta entrega reporta o suporte de nascimento com **os mesmos pares do cursor SQL TRAIN**, sem ainda converter as contagens em parâmetros ou pesos FS.

## Definições e consistência

`V9SqlConditionedBirthSupportAudit` recebe, somente internamente, os pares candidatos da união SQL física `#v9pairs` para TRAIN; o frame explícito do experimento exige **sem CPF nas duas pontas, nascimento presente, mesmo split e gestores distintos**. O código falha se nascimento faltar nesse recorte experimental; **não transforma nascimento em campo obrigatório de identidade operacional**.

A classificação de nascimento usa **o contrato existente** `BirthDateSemanticEvidence.Classify`, com exatamente sete estados mutuamente exclusivos:

1. `EXACT`;
2. `DAY_MONTH_SWAP`;
3. `CENTURY_SHIFT`;
4. `ONE_DIGIT_ERROR`;
5. `TWO_DIGIT_ERROR`;
6. `PARTIAL_COMPONENT_AGREEMENT`;
7. `OTHER_DISAGREEMENT`.

A ordem de precedência vem do classificador versionado. Cada célula reporta `MCount`, `UCount`, `DistinctMBasePeople`, `DistinctUBasePairs` e `DistinctUBasePeople`, distinguindo pares de observações da repetição de identidades-base. Não calcula ESS ou alegação de independência: pares M da mesma base e pares U que compartilham uma base podem continuar correlacionados.

Além dos sete estados semânticos, o relatório fornece os quatro contadores **binários** `BirthExactM/BirthExactU/BirthDifferentM/BirthDifferentU`, que são o suporte pertinente ao atual scorer V9 de nascimento exato versus não exato. Exige-se:

- `sum(M_semantico)=M_sql_train` e `sum(U_semantico)=U_sql_train`;
- `BirthExactM/U` são exatamente os contadores do estado semântico `EXACT`;
- `BirthExactM+BirthDifferentM=M_sql_train` e análogo para U;
- contadores e total de pares SQL são exatamente os mesmos do `TrainSqlNominalSupportAudit` (81 células) e dos contadores físicos `Splits[TRAIN]`;
- cobertura explícita de todos os sete estados, inclusive zerados; suporte bilateral agregado M e U requerido; duplicatas, pares fora de TRAIN e ausência de base são erros fail-closed.

O teste NUnit verifica os estados `EXACT` e `DAY_MONTH_SWAP`, segregação de grupos M/U, repetição do mesmo par de bases em diferentes observações, ausência de vazamento de identificadores, falha em dados fora do split/duplicados/sem M ou U. O gate Python CI recalcula independentemente todas as igualdades sobre as três sementes, sem refit nem ajuste por TEST.

## Limites e próxima decisão

Estes **números são descritivos e condicionados à recuperação SQL do corpus sintético**. Não são probabilidades m/u validadas, prior representativo de municípios nem suporte efetivo suficiente para suavização de células raras. O status de saída é `TRAIN_SQL_OBSERVED_BIRTH_COUNTS_NOT_CALIBRATED_MU_PRIOR_ESS_OR_OPERATIONAL`. A integração técnica não certifica recall 95%, baixo FDR ou capacidade de operar em 11 milhões de cidadãos.

Para calibrar, é preciso confrontar suporte M/U por pessoas-base e variação entre seeds, definir limiares de suficiência e incerteza **antes** de escolher política/limiar/peso em VALIDATION, calibrar prior e TF no mesmo frame (IBGE congelado apenas para marginais compatíveis; massa-base sintética de uma pessoa por indivíduo para conjuntas), congelar parâmetros e avaliar TEST independente. Ainda não foi reproduzido todo o histórico `VERSIONED_ALIAS`. O gate 95% do Calibrador e o recall SQL físico 43,16% continuam com denominadores diferentes e não devem ser confundidos.

**Barreira operacional:** somente banco efêmero `JornadaHarness`, `DOTNET_ENVIRONMENT=Development`, cap de candidatos já existente, não modifica rulesets ATIVOS operacionais, `Rank/Resolve`, V9 decisória, DT-15, V8 histórico, IBGE original, JornadaLocal, Gold, NODE/NAS ou HML/PROD.
