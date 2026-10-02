> **Histórico de evidência de versões anteriores.** [Decisão canônica 29/09](Decisoes_Canonicas_Identidade_Linkage_20260929.md) retira o guard demográfico exato da V8 futura e torna V6/V7 históricos. Os cenários dirigidos que pressupõem `DemographicExactCollisionRisk` **não** conferem a política revisada; atualizar vetor/implementação independente e executar DT-14 após mudar scorer. Preservar resultados antigos sem utilizá-los para promover novo modelo.

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

## Caracterização de fronteira DT-01 — conjunto artesanal, 26/09/2026

**Resultado limitado: CONFORME** para os nove cenários dirigidos (onze pontuações de candidatos) e ensaio suplementar legado de cinco termos, conforme [PR #514](https://github.com/lucianox777/Jornada/pull/514) e [execução CI #36268242478](https://github.com/lucianox777/Jornada/actions/runs/36268242478). Esse resultado significa exclusivamente que o scorer operacional, alimentado por `decimal` e com cálculo intermediário em `double`, concordou com o avaliador C# float64 independente no LLR e na **decisão final integral** dos cenários escolhidos; não é prova para todos os valores possíveis.

O conjunto foi escolhido **manualmente por construção algébrica**, não extraído de corpus sintético, amostra aleatória nem `IbgeNominalUBootstrapEstimator.ReplayPairs`. Reutiliza do PR #507 a disciplina de replay por índice/mesmo par e o diagnóstico de discordâncias, enquanto a comparação real de scorer/política usa `ImplementationConferenceRequest` e `IndependentImplementationConference`. O contrato `SplinkIbgeReplayContract` descreve estados de nomes do bootstrap IBGE, **não** pesos m/u ou decisões; preenchê-lo artificialmente com pares adversariais declarados como IBGE falsearia sua proveniência. Nenhum `.py`/Splink foi adicionado à árvore.

| Índice | Condição provocada | Decisão operacional e independente |
|---|---|---|
| 0 | Prior 0,81818170; ligeiramente abaixo de `T_LINKAGE=0,90` | `NAO_RESOLVIDO` |
| 1 | Prior 0,8181818181818182; posterior arredondado exatamente a 0,90000000 | `RESOLVIDO` |
| 2 | Prior 0,81818195; ligeiramente acima do limiar | `RESOLVIDO` |
| 3 | Dois candidatos acima do limiar; dual-threshold ativo | `CONFLITO` |
| 4 | Segundo abaixo do limiar, mas margem em log-odds menor que 0,05 | `CONFLITO` |
| 5 | `U_NOME_EXACT=10^-9` cancelado por `M_NOME_MAE_LOW=10^-9`; prior 8/9 | `RESOLVIDO` |
| 6 | Prior extremo `10^-7` contra razão nominal `9×10^7` | `RESOLVIDO` |
| 7 | Guard demográfico com flag de colisão exata pré-computado | `CONFLITO` |
| 8 | Prior 0,9999999, três termos LLR maiores que 20 e posterior saturado | `RESOLVIDO` |

O ensaio legado separado acumula cinco evidências (`NOME`, `NOME_MAE`, `NASC_DIA`, `NASC_MES`, `NASC_ANO`), com prior `10^-7` e posterior junto ao limiar. O gate V1 governado só aceita três evidências da política decision-evidence; não se declara conferência governada V1 para o legado.

**Gates mantidos:** configuração `FROZEN`, `V1_2026-09-26`, 0,01 (sem aumento), diferença absoluta de LLR de **cada** candidato dentro do teto e equivalência exata de status, candidato resolvido, melhor/segundo e motivo. O teste compara ainda o valor arredondado em oito casas próximo ao limiar. A hipótese de erro <0,001 em **corpus abrangente** permanece não demonstrada: nove casos dirigidos não determinam um limite global de erro. Os comparadores, a formação dos guard-inputs, um modelo RASCUNHO real em DEV, Splink 4.0.17 externo (#506) e representatividade estatística (#31) continuam fora desta evidência; o status de validação estatística permanece `NOT_ASSESSED_ISSUE_31`.

## Quando reconferir a implementação (DT-14)

A evidência atual do [PR #514](https://github.com/lucianox777/Jornada/pull/514), com status **`CONFORME` nos casos artesanais de fronteira**, permanece válida **para esse conjunto e para esse escopo** enquanto **scorer, thresholds e runtime** não mudarem. Ela é uma caracterização dirigida, não uma prova universal de equivalência numérica, uma conferência independente dos comparadores, a evidência SQL governada de cada modelo ou validação estatística representativa (#31).

**Não é necessário reexecutar essa conferência a cada `GENERATE_DRAFT`.** O procedimento normal pretendido passa a ser `GENERATE_DRAFT` → operador revisa o resultado → `VALIDATE` → `ACTIVATE`. Reexecutar a ferramenta standalone `Jornada.Linkage.Conference`, que **continua disponível**, quando houver alteração de `FellegiSunterScoring.cs` ou dos comparadores, migração de versão do .NET, ou mudança de faixa significativa de thresholds entre modelos consecutivos. Como a implementação independente V1 usa estados pré-computados, uma mudança nos comparadores exige testes/evidência específica adicional: `CONFORME` em V1 não atesta a correção dos comparadores.

**Execução obrigatória pós-runtime cumprida:** a DT-02 migrou a solução para .NET 10 no PR #668, com a conferência independente exigida pela DT-14 exercitada nos gates aplicáveis; o E2E-B/DT-17B do PR #677 consolidou a regressão pós-migração. Para mudanças futuras de runtime, scorer/comparadores ou thresholds significativos, preservar o registro do runtime, da versão do scorer e dos thresholds e repetir a conferência conforme DT-14; a evidência histórica do PR #514 permanece referência, não estado corrente.

**Distinção entre procedimento e código atual:** DT-14 modifica apenas a documentação. O gate existente em `VALIDATE`/`ACTIVATE` ainda exige evidência governada `CONFORME` mais recente para o **mesmo modelo, método, versão de tolerância e fingerprint**. Assim, a validade da caracterização artesanal do PR #514 **não** autoriza promover um novo rascunho sem sua própria evidência persistida. Tornar o fluxo operacional independente de conferência por rascunho requer mudança de código em tarefa separada; até lá, respeitar o bloqueio fail-closed, sem inferir que a mudança documental o retirou.

## Estados de saída

- `CONFORME`: todo LLR por par está dentro da tolerância congelada e a decisão final é exatamente a mesma;
- `DIVERGENTE`: há divergência de LLR ou decisão;
- `NAO_EXECUTADA`: pré-condição de execução não foi satisfeita, por exemplo tolerância ainda não congelada.

Esses estados são independentes da validação estatística representativa. O relatório marca explicitamente `NOT_ASSESSED_ISSUE_31`.

## Dados e privacidade

O contrato da conferência recebe identificadores opacos de candidatos, estados comparativos, parâmetros do modelo e resultados canônicos. Nos cenários TF, recebe ainda frequências numéricas do snapshot persistido; os valores nominais usados para acionar o scorer ficam no orquestrador governado e não são persistidos na evidência agregada. CPF não participa da conferência.

A evidência persistente por modelo é materializada em `auditoria.linkage_conferencia_evidencia` e permanece agregada, sem `candidate_id`, score par-a-par ou PII. O registro inclui hashes SHA-256 do request e do relatório, método/escopo, tolerância, contagens e diagnósticos agregados.

## Persistência e gate de promoção

O schema contém:

- `auditoria.sp_calcular_fingerprint_modelo_linkage`, que produz fingerprint SHA-256 canônico do snapshot decisório persistido (metadados estáveis, parâmetros, estatísticas, ruleset/passes/campos e, quando presentes, as linhas de `identidade.frequencia_linkage`);
- `auditoria.sp_registrar_conferencia_linkage`, que aceita somente modelo `RASCUNHO`, calcula esse fingerprint no ato do registro e persiste evidência agregada append-only;
- `auditoria.sp_assert_conferencia_linkage_conforme`, que procura a evidência mais recente para o mesmo `modelo_id`, método e versão de tolerância, exige `CONFORME` e recomputa o fingerprint do snapshot; qualquer mutação posterior torna a evidência obsoleta e bloqueia o assert;
- `auditoria.v_linkage_conferencia_evidencia`, superfície read-only de auditoria.

Uma execução `DIVERGENTE` ou `NAO_EXECUTADA` posterior invalida, para efeito do assert, um `CONFORME` anterior até que nova conferência `CONFORME` seja registrada. O histórico não é atualizado nem apagado. A coluna `validacao_estatistica` é restrita a `NOT_ASSESSED_ISSUE_31`, impedindo que esta conferência seja usada para declarar a validação estatística representativa.

O **wiring em `VALIDATE` e `ACTIVATE` já está implementado** no `Jornada.Linkage.Parameters.Worker`: ambas carregam o mesmo contrato versionado de tolerância, abrem transação `SERIALIZABLE` e chamam `auditoria.sp_assert_conferencia_linkage_conforme` para a própria `modelo_id` antes de alterar o status. O assert SQL exige a evidência **mais recente** `CONFORME` para o método/versão de tolerância e um fingerprint que ainda corresponda ao snapshot decisório.

Com a configuração de engenharia congelada, `VALIDATE` e `ACTIVATE` superam apenas a pré-condição de **tolerância definida**. A promoção continua exigindo a execução governada prévia da conferência, evidência mais recente `CONFORME` no mesmo método/versão/fingerprint, e a validação dos budgets FP persistidos. A regra **implementada hoje** continua exigindo conferência governada por modelo antes da promoção, embora o **procedimento normal pretendido por DT-14** seja `GENERATE_DRAFT` → revisão do operador → `VALIDATE` → `ACTIVATE`, com reconferência independente apenas nos eventos descritos acima. Nenhuma alteração de gate ou promoção é demonstrada apenas por esta documentação. As suítes de CI verificam contrato e fixtures; enquanto o gate atual persistir, DEV deve produzir evidência independente por modelo. A validação estatística representativa (#31) continua separada.

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

O corpus base contém **7 cenários e 208 candidatos sintéticos**: matriz completa de estados de nome/nome da mãe/nascimento, casos forte/fraco, missing, empate, guard-input e forte-versus-fraco. Para modelo V8 com TF habilitado, a ferramenta acrescenta dois cenários dirigidos (`TF_COMMON_EXACT` e `TF_RARE_EXACT`) escolhidos do snapshot de frequências persistido do próprio modelo; o avaliador independente recebe apenas as frequências e recompõe o ajuste por fórmula própria, sem chamar `SplinkCompatibleTermFrequency`. Datas usadas para produzir estados semânticos são valores sintéticos fixos em memória; nenhum registro de cidadão é consultado para montar o corpus.

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

Conforme a [decisão normativa §2.1](Decisoes_Linkage_Calibracao_IBGE_20260926.md#21-conferência-externa-jornada--splink--decisão-consolidada-de-26092026), o estudo externo visa **conferir os mesmos pares e estados sorteados pelo Monte Carlo IBGE**, com C# e Splink real, e não repetir amostragem aleatória diferente nem promover u incondicional a u condicionado. O modo externo existente de nove pessoas testa só formato. A futura execução/replay verificará comparador, contagens e probabilidades e o monitor deve expor somente resultado validado separado de `RoundTripStatus` e da conferência governada. A ADR-007 anterior é apenas registro histórico. Runner Splink real e evidência externa continuam pendentes em #506; a caracterização independente **dirigida da fronteira** de DT-01 está encerrada com o PR #514, sem alterar o gate `VALIDATE/ACTIVATE` nem declarar representatividade #31.

**Implementação adicional do PR #507:** `IbgeNominalUBootstrapEstimator.ReplayPairs`, extensão `CalibrationAuditExporter.ExportIbgeSyntheticReplayAsync`, contratos versionados por pares e importação offline estão disponíveis como **infraestrutura C#**. O `/monitor` ganhou campo Splink específico, inicialmente `SEM_EVIDENCIA_EXTERNA`; em DEV, só lê arquivos brutos de replay/resposta que possam ser revalidados contra a referência pública ATIVA. Falta executar/validar o runner Splink real e capturar evidência independente na issue #506. A fixture de nove pessoas continua insuficiente para a conferência do Censo.


## Registro de tentativa externa Splink — 27/09/2026

**Estado: `NAO_EXECUTADA` (bloqueio de ambiente; não é resultado de comparação).** Foi tentada a criação de um ambiente Python descartável **fora da árvore da Jornada**, seguida de instalação de `splink==4.0.17`. A instalação falhou por indisponibilidade de resolução DNS/acesso ao índice de pacotes no ambiente de execução. Nenhuma chamada ao motor Splink foi concluída; portanto não existem LLRs, rankings, decisões nem evidência `CONFORME`/`DIVERGENTE` provenientes de Splink nesta tentativa. A conferência C# decimal × C# float64 do PR #514 permanece válida **somente para seu escopo original**.

**Protocolo para retomada:** em repositório/ambiente externo com acesso ao PyPI, fixar `splink==4.0.17`, registrar a versão efetivamente importada e o hash do runner; executar os nove cenários e onze candidatos de `LinkageHandcraftedBoundaryConferenceTests.cs` com os mesmos estados, m/u, prior e IDs. Preservar a distinção entre (a) LLR e ranking calculados pelo Splink real e (b) aplicação separada da política Jornada — arredondamento a oito casas, threshold, margem, dual-threshold e guard demográfico, que não devem ser atribuídos ao Splink. Persistir por cenário os parâmetros, contribuições, LLRs, ordem, decisões, diferenças e versões; comparar cada candidato e a decisão final, sem inferir tolerância nova dos resultados. O status `CONFORME` ou `DIVERGENTE` só pode ser atribuído **após** execução e conferência dos artefatos reais. Não adicionar runner Python, dependências ou ambiente à árvore/pipeline da Jornada.

Este protocolo artesanal é **suplementar**: a conferência normativa do bootstrap IBGE continua exigindo replay dos mesmos pares e estados de `IbgeNominalUBootstrapEstimator.ReplayPairs`, suporte e probabilidades, conforme §2.1 de `Decisoes_Linkage_Calibracao_IBGE_20260926.md`. Não importar este registro como evidência governada por modelo, não executar `VALIDATE`/`ACTIVATE` por sua causa e não declarar validação representativa (#31).


## Retomada do ambiente externo — 27/09/2026

**Estado atualizado: `SPLINK_INSTALADO_SMOKE_EXECUTADO`; replay IBGE `PENDENTE`.** Após o bloqueio de DNS registrado acima, foram recebidos offline o código/pacotes de `splink==4.0.17`, suas dependências binárias para Linux x86-64/Python 3.11 e um runtime Python 3.11.14. Em ambiente virtual descartável fora da árvore da Jornada, a instalação offline concluiu e a importação efetiva confirmou `splink 4.0.17`, `duckdb 1.2.2` e `sqlglot 30.19.0`. Um smoke real com `Linker`, `DuckDBAPI`, `SettingsCreator`, `block_on('pair_index')` e `JaroWinklerAtThresholds('nome', [0.92, 0.80])` processou dois pares **inteiramente fictícios**: `MARIA`/`MARIA` produziu `gamma_nome=3` e `JOAO`/`JOA0` produziu `gamma_nome=1`. O Splink alertou que m/u e prior não estavam treinados/fixados; portanto **não utilizar os scores probabilísticos desse smoke** como evidência de conformidade. A instalação e a API básica do comparador foram verificadas, não a equivalência C# × Splink.

**Próximo gate verificável da #506:** obter os dois arquivos `JORNADA_SPLINK_IBGE_U_REPLAY_V1` exportados pelo C# no banco isolado `JornadaSyntheticDev`, executar o runner externo duas vezes sobre os mesmos `pair_index` sem reamostragem, guardar hash/versões e importar ambos os resultados pelo `--check-splink-ibge-replay`. Não há JSONs de replay IBGE neste ambiente nesta data; `CONFORME`/`DIVERGENTE`, TVD, discordância por estado e representatividade #31 continuam **não aferidos**. A execução externa não integra o CI/build/deploy da Jornada e não modifica parâmetros ou gates de promoção.


## Alteração do scorer V8 — ausência neutra (28/09/2026)

A V8 altera a contribuição da ausência do nome materno para `MISSING_NEUTRAL`, LLR zero, tal como nome e nascimento. A versão V6 preserva `MISSING` materno calibrado. A implementação independente já ignora `MISSING_NEUTRAL` ao reconstruir log-odds e a matriz governada cobre ausência dos três campos. **Reexecutar a conferência para cada modelo V8 antes da promoção.** A evidência dirigida do PR #514 não valida a V8. Os gates de fingerprint e tolerância continuam obrigatórios. Ver `Linkage_Ausencia_Neutra_V8.md`.
