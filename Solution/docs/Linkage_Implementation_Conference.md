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

O arquivo `config/linkage/implementation-conference-tolerance.json` contém a tolerância **técnica de engenharia V1_2026-09-26**, congelada antes da execução governada: `status=FROZEN`, `maxAbsolutePairLlrDifference=0.01`, método `JORNADA_IMPLEMENTATION_CONFERENCE_STATE_VECTOR_V1`. Este valor foi proposto como teto de engenharia **ex ante** para a conferência independente de scorer/policy C# `decimal` × C# `float64` (não é medição de Python/Splink, que não é o motor de conferência atual). A hipótese de erro de arredondamento <0,001 ainda demanda caracterização numérica independente em corpus abrangente; o teto 0,01 **não** demonstra suficiência estatística nem equivalência de comparadores.

A decisão final deve permanecer **exatamente igual** independentemente da tolerância de LLR. Um desvio de LLR >0,01 ou qualquer mudança de decisão produz `DIVERGENTE`. O contrato não permite inferir nem relaxar a tolerância a partir de um primeiro resultado. Evidências `NAO_EXECUTADA` ainda podem decorrer de outras pré-condições (vetor inválido, modelo incompleto). Valores `TEST_ONLY_NOT_GOVERNANCE` em fixtures não substituem o arquivo versionado.

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

- `auditoria.sp_calcular_fingerprint_modelo_linkage`, que produz fingerprint SHA-256 canônico do snapshot decisório persistido (metadados estáveis, parâmetros, estatísticas e ruleset/passes/campos);
- `auditoria.sp_registrar_conferencia_linkage`, que aceita somente modelo `RASCUNHO`, calcula esse fingerprint no ato do registro e persiste evidência agregada append-only;
- `auditoria.sp_assert_conferencia_linkage_conforme`, que procura a evidência mais recente para o mesmo `modelo_id`, método e versão de tolerância, exige `CONFORME` e recomputa o fingerprint do snapshot; qualquer mutação posterior torna a evidência obsoleta e bloqueia o assert;
- `auditoria.v_linkage_conferencia_evidencia`, superfície read-only de auditoria.

Uma execução `DIVERGENTE` ou `NAO_EXECUTADA` posterior invalida, para efeito do assert, um `CONFORME` anterior até que nova conferência `CONFORME` seja registrada. O histórico não é atualizado nem apagado. A coluna `validacao_estatistica` é restrita a `NOT_ASSESSED_ISSUE_31`, impedindo que esta conferência seja usada para declarar a validação estatística representativa.

O **wiring em `VALIDATE` e `ACTIVATE` já está implementado** no `Jornada.Linkage.Parameters.Worker`: ambas carregam o mesmo contrato versionado de tolerância, abrem transação `SERIALIZABLE` e chamam `auditoria.sp_assert_conferencia_linkage_conforme` para a própria `modelo_id` antes de alterar o status. O assert SQL exige a evidência **mais recente** `CONFORME` para o método/versão de tolerância e um fingerprint que ainda corresponda ao snapshot decisório.

Com a configuração de engenharia congelada, `VALIDATE` e `ACTIVATE` superam apenas a pré-condição de **tolerância definida**. A promoção continua exigindo a execução governada prévia da conferência, evidência mais recente `CONFORME` no mesmo método/versão/fingerprint, e a validação dos budgets FP persistidos. A regra exata continua:

`GENERATE_DRAFT -> CONFERENCIA -> VALIDATE -> ACTIVATE`

Não há conferência governada executada nem promoção demonstrada apenas pela alteração deste arquivo. As suites de CI verificam o contrato e fixtures; DEV deve produzir evidência independente por modelo. A validação estatística representativa (#31) continua separada.

## Comando governado

`Jornada.Linkage.Conference` é o orquestrador separado que pode enxergar simultaneamente o Core operacional e a Evaluation independente. Runner e Parameters Worker não dependem dele.

O comando:

1. exige `--model-id` apontando para modelo `RASCUNHO`;
2. carrega `implementation-conference-tolerance.json` e **recusa abrir o banco** se a tolerância não estiver `FROZEN`;
3. abre transação `SERIALIZABLE`;
4. calcula/locka o fingerprint do snapshot decisório;
5. carrega parâmetros do modelo e monta corpus determinístico sem PII;
6. calcula o lado canônico com `Jornada.Linkage.Core`;
7. executa `IndependentImplementationConference` sobre os mesmos vetores;
8. agrega conservadoramente os resultados;
9. recalcula o fingerprint antes do registro;
10. persiste somente o resumo agregado e hashes SHA-256.

O corpus corrente contém **7 cenários e 208 candidatos sintéticos**: matriz completa de estados de nome/nome da mãe/nascimento, casos forte/fraco, missing, empate, guard-input e forte-versus-fraco. Datas usadas para produzir estados semânticos são valores sintéticos fixos em memória; nenhum registro de cidadão é consultado para montar o corpus.

O hash do request e do relatório inclui o fingerprint do snapshot do modelo. Rerun byte-a-byte idêntico é idempotente e retorna o mesmo `evidencia_id`; mesmo hash com request/snapshot incompatível é recusado fail-closed.

O arquivo corrente tem `toleranceVersion=V1_2026-09-26`, `status=FROZEN` e `maxAbsolutePairLlrDifference=0.01`. O comando pode executar uma conferência governada quando houver modelo `RASCUNHO` e SQL operacional disponível; seu resultado, `CONFORME` ou `DIVERGENTE`, não deve ser presumido antes da execução.


## Exposição no monitor operacional

O `/monitor` exibe a última evidência agregada persistida para o modelo ATIVO, sem expor o valor numérico da tolerância, threshold ou margem. O painel mantém separadas três noções:

- conferência de implementação: evidência persistida por modelo;
- round-trip C# do formato: obrigatório no export, porém não persistido por modelo;
- validação estatística representativa: `PENDENTE_ISSUE_31`.

A ausência de evidência da conferência para o modelo ATIVO é exibida como `SEM_EVIDENCIA_MODELO_ATIVO`, e não como sucesso implícito.

## Estudo externo Splink — ADR-007, suplementar e não governado

A [ADR-007](../../Documentos/ADR/ADR-007-conferencia-externa-splink-sem-python-operacional.md) aprova uma fronteira **offline e sintética** com Splink em repositório independente. O primeiro intercâmbio V1 usa fixture literal `NOME` (nove indivíduos, dezoito registros), não acessa SQL e não contém dados de cidadão. O C# calcula m suavizado por labels e u **incondicional** exato por pares distintos; compara TVD e LLR por nível com as estimativas m/u externas (u externo estimado por amostragem). Diferença entre estimadores não é, por si, falha de scorer; comparadores V2, nome da mãe, nascimento e u condicionado por blocking não são cobertos. Executar Splink externamente ainda é pendência, não evidência já produzida.

A tolerância congelada `0,01` e o gate `JORNADA_IMPLEMENTATION_CONFERENCE_STATE_VECTOR_V1` continuam exclusivos da conferência governada C# decimal × C# float64 existente. **Nunca** importar resultado Splink como `CONFORME` persistido, promover modelo ou substituir a validação representativa #31. Ver [runbook offline](Linkage_Splink_External_Runbook.md).

## Decisão consolidada sobre Splink real e bootstrap IBGE

Conforme a [decisão normativa §2.1](Decisoes_Linkage_Calibracao_IBGE_20260926.md#21-conferência-externa-jornada--splink--decisão-consolidada-de-26092026), o estudo externo visa **conferir os mesmos pares e estados sorteados pelo Monte Carlo IBGE**, com C# e Splink real, e não repetir amostragem aleatória diferente nem promover u incondicional a u condicionado. O modo externo existente de nove pessoas testa só formato. A futura execução/replay verificará comparador, contagens e probabilidades e o monitor deve expor somente resultado validado separado de `RoundTripStatus` e da conferência governada. A ADR-007 anterior é apenas registro histórico. Runner externo, evidência e caracterização independente seguem pendentes em #506/DT-01; não alterar o gate `VALIDATE/ACTIVATE`.

**Implementação adicional do PR #507:** `IbgeNominalUBootstrapEstimator.ReplayPairs`, extensão `CalibrationAuditExporter.ExportIbgeSyntheticReplayAsync`, contratos versionados por pares e importação offline estão disponíveis como **infraestrutura C#**. O `/monitor` ganhou campo Splink específico, inicialmente `SEM_EVIDENCIA_EXTERNA`; em DEV, só lê arquivos brutos de replay/resposta que possam ser revalidados contra a referência pública ATIVA. Falta executar/validar o runner Splink real e capturar evidência independente na issue #506. A fixture de nove pessoas continua insuficiente para a conferência do Censo.
