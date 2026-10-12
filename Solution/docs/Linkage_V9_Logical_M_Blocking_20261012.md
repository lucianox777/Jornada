# V9 — condicionamento lógico de pares M ao ruleset de blocking

**12/10/2026.** Continuação do plano #946 após PRs #947 (estados nominais), #948 (diagnóstico m/u), #949 (amostra U proveniente das chaves materiais de blocking).

## Escopo

No `GENERATE_DRAFT`, o diagnóstico V9 agora filtra os pares **M** verdadeiros inter-Gestores, com CPF independente do scorer, **pelos mesmos passes de blocking selecionados**: AND entre campos, OR entre passes, sem aceitar campo ausente como concordância.

A avaliação utiliza `BlockingFeatureObservationFactory.Create` e as transformações do `ResolutionProjectionExecutor` da projeção corrente; não implementa uma nova fonética ou uma variante nominal implícita. A amostra U segue sendo fornecida diretamente por `BlockingConditionedUnmatchedPairReader`, que usa as chaves de blocking materializadas no SQL.

Foram adicionados contadores agregados `mInput`, `mLogicallyRetained`, `uSample`, `candidatePool` e distribuições nominais do conjunto M logicamente retido, sem nomes, tokens, UUIDs ou fingerprints de valores pessoais. Se nenhum par M sobreviver, o relatório fica `UNAVAILABLE_NO_LOGICALLY_RETAINED_MATCHES`; não fabrica m/u nem quebra a geração histórica da V8. Features fora do catálogo canônico são rejeitadas.

## Limite estatístico obrigatório

**M condicionado logicamente não equivale a M provado como integrante da indexação SQL física**. A base materializada preserva `VERSIONED_ALIAS` temporal; a projeção atual em memória pode não reproduzir todos os aliases históricos e a estratégia de captura não fornece probabilidades de inclusão/correção para M e U.

Portanto o estado é `DIAGNOSTIC_ONLY_M_LOGICAL_RULESET_U_SQL_CONDITIONED`. Não inferir que há distribuição m/u calibrada representativa do universo candidato, não publicar parâmetros, não dar score V9 e não alegar equivalência matemática com Splink. A calibração final requer atestar pertencimento M nas chaves históricas, pesos de desenho e segregação independente TRAIN/VALIDATION/TEST. Dependências entre nome da pessoa e da mãe e TF conjunta ainda são pendências.

## Garantias

- Nenhuma modificação de `AlgorithmVersion` operacional, `Rank/Resolve`, TF histórica, `VALIDATE/ACTIVATE`, banco, modelo ATIVO, fingerprints/ledger persistidos.
- Sem degradação/flexibilização de gates CI.
- O dado estatístico é diagnóstico e não autoriza ativação DT-15.
