# Conferência independente da implementação do Linkage

## Escopo

A primeira conferência independente da Jornada verifica **scorer e política operacional a partir de estados de comparação já formados**.

Método:

`JORNADA_IMPLEMENTATION_CONFERENCE_STATE_VECTOR_V1`

Escopo declarado:

`SCORER_POLICY_ONLY_STATES_AND_GUARD_INPUTS_PRECOMPUTED_COMPARATORS_OUT_OF_SCOPE`

Ela recalcula em implementação separada:

- transformação `estado + m/u -> LLR`;
- soma de LLR e prior em log-odds;
- posterior;
- ranking determinístico;
- threshold;
- margem em log-odds;
- guard de núcleo demográfico exato não único;
- dual-threshold e piso de conflito independente;
- decisão final `RESOLVIDO / NAO_RESOLVIDO / CONFLITO`.

A implementação reside em `Jornada.Linkage.Evaluation`, projeto que não referencia `Jornada.Linkage.Runner` nem `Jornada.Linkage.Core`. O código da conferência também não chama `FellegiSunterScoring`, `ProbabilisticLinkageDecisions`, `IdentityComparison` ou `BirthDateSemanticEvidence.Classify`.

## Limite da afirmação

Os estados de nome, nome da mãe e nascimento são entradas da conferência. O flag `DemographicExactCollisionRisk`, consumido pelo guard de núcleo demográfico exato não único, também chega pré-computado. Portanto esta primeira versão **não detecta defeitos na formação desses estados nem na derivação desse flag** e não deve ser descrita como uma segunda implementação independente dos comparadores/guard-input.

Uma futura conferência de comparadores deverá partir de entradas brutas e implementar normalização/classificação de maneira independente. Até lá, comparadores permanecem explicitamente fora do escopo.

## Contrato do vetor de evidência

Para algoritmos `decision-evidence`, cada candidato deve carregar exatamente:

- uma evidência `NOME`;
- uma evidência `NOME_MAE`;
- uma evidência de nascimento: `NASCIMENTO_SEMANTICO`, ou `NASCIMENTO/MISSING_NEUTRAL` quando a data não estiver disponível.

Vetores incompletos, duplicados ou com shape incompatível retornam `NAO_EXECUTADA / INVALID_EVIDENCE_VECTOR_SHAPE`. Isso evita que uma extração defeituosa pareça conforme apenas porque a evidência omitida teria contribuição LLR nula.

## Gate primário

A avaliação governada usa dois gates primários:

1. diferença absoluta do LLR por par dentro de uma tolerância versionada e previamente congelada;
2. decisão operacional final exatamente equivalente, incluindo status, candidato resolvido, melhor/segundo candidato e motivo.

`sameTop1`, correlação de Spearman e diferença máxima de log-odds são apenas diagnósticos. Eles nunca substituem os dois gates primários.

## Tolerância

O arquivo `config/linkage/implementation-conference-tolerance.json` está deliberadamente em:

`UNFROZEN_REQUIRED_BEFORE_FIRST_EXECUTION`

e mantém `maxAbsolutePairLlrDifference=null`.

Não existe default de produção. A engine retorna `NAO_EXECUTADA / TOLERANCE_NOT_FROZEN` quando o contrato não está congelado. O valor deverá ser definido e versionado **antes da primeira execução governada**, sem ser inferido a partir do primeiro resultado.

Os valores numéricos usados em testes automatizados são fixtures marcadas `TEST_ONLY_NOT_GOVERNANCE` e não constituem tolerância institucional/técnica do modelo.

## Estados de saída

- `CONFORME`: todo LLR por par está dentro da tolerância congelada e a decisão final é exatamente a mesma;
- `DIVERGENTE`: há divergência de LLR ou decisão;
- `NAO_EXECUTADA`: pré-condição de execução não foi satisfeita, por exemplo tolerância ainda não congelada.

Esses estados são independentes da validação estatística representativa. O relatório marca explicitamente `NOT_ASSESSED_ISSUE_31`.

## Dados e privacidade

O contrato da conferência recebe identificadores opacos de candidatos, estados comparativos, parâmetros do modelo e resultados canônicos. Não necessita nome, CPF, data de nascimento textual ou outros dados pessoais.

A evidência persistente por modelo é materializada em `auditoria.linkage_conferencia_evidencia` e permanece agregada, sem `candidate_id`, score par-a-par ou PII. O registro inclui hashes SHA-256 do request e do relatório, método/escopo, tolerância, contagens e diagnósticos agregados.

## Persistência e gate de promoção

O schema contém:

- `auditoria.sp_registrar_conferencia_linkage`, que aceita somente modelo `RASCUNHO` e persiste evidência agregada append-only;
- `auditoria.sp_assert_conferencia_linkage_conforme`, que procura a evidência mais recente para o mesmo `modelo_id`, método e versão de tolerância e falha fechado se ela não estiver `CONFORME`;
- `auditoria.v_linkage_conferencia_evidencia`, superfície read-only de auditoria.

Uma execução `DIVERGENTE` ou `NAO_EXECUTADA` posterior invalida, para efeito do assert, um `CONFORME` anterior até que nova conferência `CONFORME` seja registrada. O histórico não é atualizado nem apagado.

O **wiring em `VALIDATE/ACTIVATE` ainda não está ativo**. Enquanto `implementation-conference-tolerance.json` permanecer `UNFROZEN_REQUIRED_BEFORE_FIRST_EXECUTION`, o fluxo operacional existente não chama a procedure de assert e nenhuma aprovação é inferida. A ligação efetiva seguirá a sequência:

`GENERATE_DRAFT -> CONFERENCIA -> VALIDATE -> ACTIVATE`

somente depois de congelar/versionar a tolerância e implementar o comando governado que produz o request/relatório a partir do modelo real.
