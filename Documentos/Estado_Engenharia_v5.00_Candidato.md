# Estado de Engenharia — candidato Solution Engenharia v5.00

**Data de consolidação:** 20/09/2026  
**Status:** CANDIDATO TÉCNICO — RELEASE/TAG NÃO CORTADA  
**SolutionSchema corrente:** `3.70`

## 1. Relação com a última release selada

A última release selada continua sendo a **Solution Engenharia v4.05**, com `schema_solution=v3.69` e tag `jornada-solution-v4.05`, conforme `RELEASE_INFO.txt`.

Este documento descreve somente a candidata v5.00. O commit imutável da RC, quando existir, será registrado pelos metadados de candidato/release; este arquivo não congela antecipadamente um SHA mutável de `master`.

## 2. Runtime relacional da candidata

**Microsoft SQL Server é o único runtime relacional suportado pela Jornada candidata v5.00.**

O DDL canônico, o Processor, o Linkage, a coordenação transacional, os testes de integração, o instalador Windows e os gates de promoção usam o contrato Microsoft SQL exercitado em SQL Server 2022 Developer/Testcontainers no desenvolvimento e CI.

O suporte operacional paralelo a PostgreSQL foi retirado desta candidata: não há provider selecionável, adapter Npgsql, persistência de identidade PostgreSQL, calibrador PostgreSQL, DDL PostgreSQL nem gates de paridade PostgreSQL no produto corrente. O histórico Git preserva a implementação anterior para eventual migração a projeto independente; ele não constitui suporte runtime desta Jornada.

## 3. Microsoft Fabric

SQL Database in Microsoft Fabric **não é alvo operacional da candidata v5.00 e não é gate para o corte da RC/release**. Evidências Fabric anteriores permanecem como histórico de compatibilidade técnica.

Lakehouse e SQL Analytics Endpoint permanecem no escopo analítico/compatibilidade e não substituem o banco relacional operacional SQL Server.

Nenhuma hospedagem Fabric autoriza DDL alternativo, branch funcional ou segunda fonte de verdade operacional nesta candidata.

## 4. Linkage e calibração

O caminho operacional SQL Server usa `LinkageParametersWorker` e o modelo de decisão versionado da Jornada. `T_LINKAGE` e a margem efetiva V6 deixaram de ser entradas numéricas do `appsettings`: `FS_DECISION_THRESHOLD_PARETO_V1` deriva uma grade das fronteiras observadas em `VALIDATION`, avalia a regra exata do Runner, preserva a fronteira não dominada em FP/FN e reaplica os candidatos congelados em `TEST`. O mesmo indivíduo-base é mantido em uma única partição por split determinístico.

A verdade de referência dessa etapa vem de observações ligadas deterministicamente por CPF, mas o CPF é ocultado da geração de candidatos e do score. Cada observação rotulada gera um cenário positivo e um cenário negativo `LEAVE_TRUTH_OUT`, no qual a identidade verdadeira é removida do ranking. A restrição pré-HML de zero falso vínculo pode bloquear a promoção de uma fronteira; ela é safety gate explícito, não peso inventado entre FP e FN. `TEST` pode reprovar o candidato congelado, nunca escolher outro threshold olhando o próprio teste.

A implementação operacional dessa etapa compartilha literalmente o scorer e a política de decisão com o Runner por `Jornada.Linkage.Core`, evitando uma segunda implementação da fronteira. O draft persiste proveniência, tamanhos das partições e FP/FN/inconclusivos de validação/teste, e `VALIDATE` falha fechado sem a marca da calibração ou com falso vínculo em `TEST`.

Isso fecha a lacuna de `T_LINKAGE`/margem fixos. O antigo estágio DF/Splink foi retirado por não participar do runtime nem resolver os limites de identificabilidade observados. A publicação ponta a ponta separa o score bruto da decisão operacional, preserva `initial_uuid` apenas como linhagem e encaminha conflitos probabilísticos publicados para a fila institucional existente. Cada divergência probabilística mantém FK para o `linkage_resultado` imutável; a fila não duplica modelo, candidatos, scores ou margem e não produz correção automática.

A pendência estatística corrente é transportabilidade de `m/u`, representatividade da coorte rotulada, convergência do `u` para o universo candidato real e avaliação de evidências adicionais. A validação estatística representativa permanece gate externo. Corpus sintético, Monte Carlo e validação adversarial DEV são evidência de engenharia, não homologação populacional.

## 4.1. Auditoria/intercâmbio do modelo de Linkage

A retirada do antigo estágio DF/Splink não elimina a auditabilidade externa. `Jornada.Linkage.Evaluation --export-calibration` exporta somente modelos `ATIVO` ou `VALIDADO` em um documento C# tipado, sem criar run, sem alterar status e sem gravar identidade/Gold.

O formato separa a semântica do **u empírico**, condicionado à união deduplicada dos candidatos de blocking, da fonte nominal de `u` para nome/nome da mãe. Esta última é derivada dos parâmetros persistidos e pode ser `BLOCKING_CONDITIONED` ou `IBGE_BOOTSTRAP` enquanto a convergência condicionada não for suficiente. O documento não declara equivalência por definição ao `u` de pares aleatórios de outra ferramenta. Estados semânticos de nascimento sem mapeamento 1:1 também permanecem explícitos.

Cada exportação executa obrigatoriamente `JORNADA_CALIBRATION_AUDIT_ROUNDTRIP_V1`: serializa, reimporta fail-closed e compara campo a campo antes de escrever o arquivo. Membros desconhecidos são recusados para impedir perda silenciosa no round-trip. Essa evidência demonstra fidelidade do **formato Jornada**, não compatibilidade Splink completa, não paridade de uma segunda implementação do scorer e não substitui a validação representativa #31.

## 4.2. Conferência independente de implementação

A candidata contém a engine `JORNADA_IMPLEMENTATION_CONFERENCE_STATE_VECTOR_V1` em `Jornada.Linkage.Evaluation`, projeto separado que não referencia o Runner nem o Core operacional. A engine recebe estados comparativos e o flag de risco demográfico já formados e recalcula de forma independente LLR, agregação, posterior, ranking, threshold, margem e aplicação dos guards até a decisão final. A formação dos estados e a derivação do flag de risco permanecem fora da afirmação de independência.

A afirmação é deliberadamente limitada: comparadores de nome/data continuam **fora do escopo**, porque seus estados já chegam prontos à conferência. O gate primário exige LLR por par dentro de tolerância previamente congelada e decisão final exatamente equivalente; top-1, Spearman e diferença de log-odds são diagnósticos.

Nenhuma tolerância governada foi inventada. `config/linkage/implementation-conference-tolerance.json` permanece `UNFROZEN_REQUIRED_BEFORE_FIRST_EXECUTION` com valor nulo. Nessa condição a engine retorna `NAO_EXECUTADA`, não `CONFORME`. Valores numéricos usados pelos testes são fixtures `TEST_ONLY_NOT_GOVERNANCE`.

A conferência de implementação continua distinta da validação estatística representativa #31. A candidata agora materializa `auditoria.linkage_conferencia_evidencia`, append-only, e as procedures de fingerprint/registro/assert do gate. A evidência é agregada por modelo/método/tolerância, sem PII ou score par-a-par. No registro, o SQL calcula um fingerprint SHA-256 do snapshot decisório do modelo; no assert, ele é recomputado. Assim, qualquer mutação posterior de parâmetros/estatísticas/ruleset invalida a evidência. O assert sempre considera a evidência mais recente e falha se ela não estiver `CONFORME`. `validacao_estatistica` permanece forçada a `NOT_ASSESSED_ISSUE_31`.

A candidata agora também contém o orquestrador `Jornada.Linkage.Conference`. Ele é projeto separado, referencia Core + Evaluation apenas para conferência e não é dependência de Runner/Parameters Worker. Em transação `SERIALIZABLE`, ele locka o fingerprint do modelo, executa um corpus determinístico sem PII de 7 cenários/208 candidatos, compara scorer/policy canônico × independente e registra somente a evidência agregada. Request/report hashes incorporam o fingerprint do snapshot; rerun idêntico é idempotente.

O wiring do assert em `VALIDATE/ACTIVATE` permanece deliberadamente **inativo** enquanto `implementation-conference-tolerance.json` estiver `UNFROZEN_REQUIRED_BEFORE_FIRST_EXECUTION`. A configuração explicita `toleranceVersion=UNFROZEN` e valor nulo. Portanto o comando está implementado, mas uma execução governada real e a promoção condicionada continuam bloqueadas até o congelamento/versionamento explícito da tolerância.

## 4.3. Autoria dos atos governados de identidade

`controle.api_evento` permanece telemetria/auditoria HTTP e não é usado como fonte canônica de autoria de correções de identidade. A candidata materializa `auditoria.decisao_identidade_evento`: ledger append-only que registra a credencial `GESTOR` autenticada, o Gestor, um `operacao_id` gerado pelo SQL Server, o objeto governado, ato/justificativa e `correlation_id` apenas como contexto.

A gravação ocorre dentro da mesma transação da correção, abertura/aplicação de caso ou desfecho de divergência. Falha do ledger provoca rollback da mutação; o teste de integração injeta essa falha explicitamente. O CPF opcional declarado pelo agente na borda HTTP não é tratado como autoria institucional.

## 5. Proveniência de schema

A fonte canônica permanece `Solution/database/Jornada_Fase1_v3.70.sql`, com migrações versionadas e fingerprint estrutural controlado em `CANDIDATE_INFO.json`.

Antes do corte da RC, a tupla de proveniência deve apontar para um **checkpoint estrutural imutável e ancestral** do HEAD da RC. O `source_commit` não precisa ser o próprio HEAD da RC: ele representa o último estado do schema comprovadamente equivalente ao candidato corrente.

O gate `jornada-schema-consolidation-370` deve provar, de forma executável, que:

- o fingerprint estrutural calculado no SQL Server coincide com `structural_fingerprint_sha256`;
- `source_commit` é ancestral do HEAD exercitado;
- o manifesto de migrações no checkpoint possui o hash declarado;
- o próprio workflow de consolidação corrente prova instalação limpa, upgrade, bundle achatado e equivalência estrutural contra o fingerprint declarado; não existe dependência circular de um run histórico por ID.

O **HEAD exato da RC** continua sendo identificado e testado separadamente pelo CI de engenharia e pelo `.NET SourceRevisionId`. O caminho `rc-evidence` recalcula o fingerprint estrutural em SQL Server na própria tag e publica essa prova como asset durável do pre-release. A evidência autoritativa é produzida no commit/tag exercitado, não herdada de um run histórico. Após o corte da RC, mudança estrutural exige novo checkpoint de RC.

## 6. Documentação e UML

Os documentos destinados à entrega permanecem em DOCX/PDF, com UML incorporada quando exigida. As fontes Markdown e scripts de geração são artefatos de engenharia e rastreabilidade.

Documentação histórica não deve ser usada para inferir arquitetura corrente quando divergir deste estado candidato, da Especificação/Requisitos correntes ou de `CANDIDATE_INFO.json`.

## 7. Condições para o corte técnico da v5.00-rc.1

O corte da RC é um **checkpoint técnico imutável**, não homologação populacional, não ativação probabilística e não release de Produção.

Os assets anexados ao pre-release não são imutáveis por si: a reexecução pode substituí-los com `--clobber`. O commit/tag identifica o checkpoint; para os bytes atestados, a referência de integridade é a attestation Sigstore e seus subject digests. As Actions que participam desse caminho ficam congeladas até o corte da RC, evitando trocar a cadeia de evidência antes de exercitá-la.

O conteúdo candidato usa `technical_rc.status=CHECKPOINT_CONTENT` e `schema_provenance.status=BOUND_FOR_TECHNICAL_RC`; esses estados descrevem o conteúdo do commit, não fingem que a tag já foi criada. Tags `v*-rc.*` seguem o caminho `rc-evidence`; somente tags `jornada-solution-v*` seguem `release-promotion` e `RELEASE_INFO.txt`.

Antes do corte técnico devem estar satisfeitas estas condições:

1. o conjunto canônico de build, unitários, integração SQL, DDL/upgrade, E2E, segurança e harness deve estar verde no **HEAD exato** escolhido para a RC;
2. o gate executável de proveniência do schema deve comprovar a tupla registrada em `CANDIDATE_INFO.json`;
3. não pode haver defeito funcional ou estrutural conhecido que torne o checkpoint tecnicamente inconsistente;
4. os gates externos devem permanecer explicitamente pendentes, sem fabricar aprovação;
5. o CI da própria tag deve concluir `bronze-restore-drill`, `scale-harness` e `rc-evidence`, que publica o GitHub pre-release com evidência estrutural, bundle de fonte e attestation.

A validação estatística representativa do Linkage (#31) e a volumetria/decisão institucional de HML (#93) **não bloqueiam o checkpoint técnico da RC**. Elas continuam bloqueando, conforme o caso, homologação estatística, ativação/publicação probabilística, decisão institucional e promoção efetiva para Produção.

A higiene física de branches (#79) também não altera o conteúdo técnico da RC.

Homologação Fabric não integra essas condições.

## 8. O que este estado não autoriza

Este documento não autoriza:

- criar tag/release v5.00 ou v5.00-rc.1 antes dos gates do HEAD exato;
- reescrever `RELEASE_INFO.txt` da última release selada antes do novo corte;
- ativar modelo probabilístico por evidência sintética isolada;
- tratar código histórico PostgreSQL como runtime suportado;
- tratar evidência histórica Fabric como requisito ou homologação da candidata atual.

O objetivo do fechamento é manter **uma arquitetura operacional, um contrato relacional e uma cadeia de evidência reproduzível**, sem segunda persistência concorrente.


O monitor operacional agora apresenta, para o modelo ATIVO, a última evidência agregada da conferência quando existente, mantendo round-trip e validação estatística como evidências distintas.


O bloco de governança do `/monitor` também apresenta o suporte condicionado por passe do ruleset fixado no modelo ATIVO, incluindo amostra u, suporte de nome da mãe presente e suficiência contra o mínimo persistido por passe. Esses valores permanecem diagnósticos de proveniência, não parâmetros de decisão.
