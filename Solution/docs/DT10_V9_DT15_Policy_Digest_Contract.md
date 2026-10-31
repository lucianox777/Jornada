# DT-10 V9 — vínculo imutável de autorização DT-15 (em implementação)

**Estado em 26/10/2026:** o guard SQL foi adicionado à branch da PR #1199 no commit `12dadabff6d05ca81fc2cd1098f799defbcf0f71`. **Teste físico negativo e replay legado aprovados em LocalDB descartável no HEAD 5a9e68e; a prova positiva governada permanece pendente; não integrado à master**. A política continua desabilitada por padrão, sem registros de política ou autorização gerados pela migração. A aprovação de um modelo DT-15 isoladamente **não** autoriza a política inferior.

## Contrato de autorização

A tabela `auditoria.v9_dt10_lower_policy_autorizacao` vincula `aprovacao_id`, `politica_versao` e `politica_sha256` em registro separado, protegido contra UPDATE e DELETE por trigger. Somente a governança DT-15 pode criar a autorização depois de verificar a política proposta; nenhuma autorização é semeada automaticamente. A publicação exige simultaneamente a aprovação DT-15 ACTIVATE existente e o digest exato correspondente à linha da política.

**Serialização canônica V1:** `HASHBYTES('SHA2_256', CONCAT(...))`, com texto SQL Server Unicode e campos nesta ordem: prefixo literal `JORNADA_DT10_V9_POLICY_V1`, `politica_versao`, `modelo_id` (UUID), `modelo_versao`, `modelo_fingerprint` (hex), `fit_fingerprint` (hex), `snapshot_sha256` (hex), `limiar_inferior`, `limiar_superior`, separados por `|`. Os decimais são convertidos a NVARCHAR(40) a partir de DECIMAL(18,8), preservando oito casas. A verificação é realizada na procedure de publicação antes de escrever o ledger. Não aceitar digest enviado pelo produtor como substituto da recomputação SQL.

A guarda de publicação canônica exige também `origem_evidencia` do dossiê DT-15 em `REPRESENTATIVA_HML` ou `REPRESENTATIVA_PROD`. `SINTETICA_DEV` é rejeitada mesmo com estado `COMPLETO`, digest correto e aprovação ACTIVATE. A origem declarada como representativa é **condição necessária, não prova de representatividade estatística, aprovação institucional ou liberação de TEST**. O ensaio positivo em LocalDB deve permanecer claramente técnico e não pode ser usado para declarar certificação.

O snapshot é verificado por hash do conteúdo, schema, fit e threshold superior, além da aprovação do modelo e dossiê vigentes. Alterações na versão, no modelo, no fit, no snapshot ou nos limiares invalidam o digest. A trigger impede reescrever ou apagar a autorização existente; controles de permissão devem restringir INSERT a operadores DT-15 autorizados.

## Evidências ainda obrigatórias antes do merge

1. Teste SQL positivo em harness descartável: aprovação DT-15 válida, autorização do digest, candidato e posterior persistidos, publicação de nova identidade, ledger único e replay idempotente.
2. Testes negativos de mutação individual dos campos vinculados, digest ausente ou divergente, aprovação incorreta e tentativa de UPDATE/DELETE da autorização.
3. Validação do schema, testes de regressão e CI aplicável no HEAD final; atualização de matriz de gates e instruções operacionais antes da ativação.

**Não ativar em produção nem inferir aprovação de CI a partir deste documento.**

## Critério de aceite verificável da onda positiva (bloqueante para retirar draft)

A prova deve ocorrer em **um único LocalDB descartável JornadaHarness**, usando o fluxo real de aprovação DT-15 (aprovação ACTIVATE e dossiê vigente), nunca um INSERT artificial de autorização tomado como prova de governança. O digest deve ser calculado e conferido pelo SQL a partir de modelo, versão, fit, snapshot imutável e ambos os limiares. O valor **0,05 é configuração da política aprovada na fixture**, não constante de modelo ou do procedimento.

Executar **na mesma onda**, sob a mesma política aprovada, e registrar PASS/FAIL por caso na PR #1199:

| Caso | Preparação | Asserções obrigatórias |
| --- | --- | --- |
| LOWER | Candidato persistido, posterior estritamente abaixo de 0,05 | Exatamente uma publicação `LIMIAR_INFERIOR_V9_GOVERNADO`, uma nova identidade e um evento no ledger, com vínculo à política e ao snapshot aprovados |
| INTERMEDIÁRIO | Candidato persistido, posterior >= 0,05 e abaixo do limiar superior aprovado | Continua pendente; zero identidades criadas, zero eventos de criação |
| SEM CANDIDATO | Busca completa sem candidato | Caminho existente `SEM_CANDIDATO_NO_RULESET_BLOCKING`, sem depender da autorização lower |
| REPLAY | Reexecutar a mesma onda sem alterar inputs | Zero publicações adicionais; mesmos UUIDs, estados e contagens de eventos |

O teste deve comparar **estado final de cada origem e contagem de eventos por origem/run** antes e depois do replay, não apenas ausência de exceções. Não marcar como aprovado quando a fixture contiver somente registros artificiais sem passar pela aprovação DT-15 real.

### Mutações negativas e atomicidade

Repetir com autorização ausente, `aprovacao_id` incorreto, digest divergente e alteração individual de versão, modelo, fit, snapshot, limiar inferior e superior. Em cada rejeição, exigir **zero eventos parciais, zero promoção de identidade e estado do run preservado**; UPDATE e DELETE de autorização devem falhar. Registrar o erro SQL esperado e observado para cada caso.

### Publicação de evidências e ordem de merge

Publicar na PR #1199 tabela por caso (PASS/FAIL), SHA do HEAD, identificador da execução física, digest/snapshot em formato não sensível e contagens antes/depois. **Não retirar de draft nem mesclar** enquanto qualquer caso não passar e todos os gates aplicáveis não estiverem verdes no mesmo HEAD. A PR #1202 do helper NUnit é independente; substituir os usos ambíguos existentes e validar build Release com warnings-as-errors antes de seu merge. Não ativar V9/DT-15 em ambiente persistente.


## Registro de conciliação — 27/10/2026

A decisão vigente exige a execução **positiva** da procedure canônica em `JornadaHarness` LocalDB descartável **antes** do merge da PR #1199, usando as procedures DT-15 normais e dossiê de fixture segregado; não adiar o primeiro sucesso para HML/PROD. A etiqueta representativa em fixture não é certificação estatística ou institucional. A matriz LOWER/INTERMEDIÁRIO/SEM CANDIDATO/REPLAY, adulteração de cada campo do digest, origem `SINTETICA_DEV`, replay e atomicidade permanece **NÃO IMPLEMENTADA/NÃO TESTADA** no HEAD `c4c748b6ea54717f6ec0dd74e2057b54d32ca281`. O preflight físico atual testa apenas OFF, recusa do motivo experimental, append-only e replay legado. Os seis workflows SUCCESS nesse HEAD, inclusive [TRAIN 30k #40509840420](https://github.com/lucianox777/Jornada/actions/runs/40509840420), não autorizam ativação nem merge. O loader REF #1089 e TEST final independente permanecem gates separados. Esta conciliação documental não altera nem substitui o digest SQL e exige revalidação no HEAD posterior.


### Fechamento do predicado de motivo — 28/10/2026 (PR #1199, pendente de CI)

A validação geral de `NOVA_IDENTIDADE` e a guarda contra candidatos persistidos devem usar **exatamente o mesmo prefixo literal** `SEM_CANDIDATO_`: em T-SQL, `LIKE N'SEM[_]CANDIDATO[_]%'` (os sublinhados não podem ser curingas). Motivos semelhantes, como `SEMXCANDIDATO_...` e `SEM_CANDIDATOX...`, devem receber `THROW 51810` mesmo que haja candidato físico, com zero eventos, estado provisório e recusa idêntica no replay. O prefixo literal com candidato real continua sujeito a `THROW 51835`; ausência legítima continua idempotente. Este adendo registra o contrato e a regressão proposta na branch, **não certifica execução física nem altera a exigência da matriz positiva DT-15 ou a revisão REF #1089**.

## Adendo de rastreabilidade — 28/10/2026, correção de preflight e vínculo estrutural

Reconciliação do guard `SEM_CANDIDATO` no harness TRAIN: a variante literal `SEM[_]CANDIDATO[_]%` é agora a única reconhecida pelo patch de LocalDB descartável, sem alterar a política SQL default OFF ou autorizações DT-15. Regressões unitárias rejeitam guarda antiga, guarda adulterada, declaração divergente, duplicação e ausência de `THROW 51810`. Schema triplo medido em [40659908716](https://github.com/lucianox777/Jornada/actions/runs/40659908716): SHA-256 `8fe1db4da72aa420f3741c625270e5a6cd9cee2217ec9f60effbee8466a08a21`, vinculado ao candidato; preflight físico e CI do novo HEAD pendentes. Digest governado, autorização legítima e matriz positiva continuam sem prova de aceite.


## Emissor DT-15 vinculado ao motivo imutável — implementação em avaliação

A migration inclui agora auditoria.sp_autorizar_v9_dt10_lower_policy. O emissor não aprova modelos, não habilita políticas e não concede permissões. A aprovação ACTIVATE deve ser registrada pelo procedimento DT-15 normal, com o motivo humano contendo literalmente DT10_POLICY_SHA256= seguido do SHA-256 canônico em 64 caracteres hexadecimais maiúsculos. A política é registrada depois da aprovação, com habilitada=0, e só então pode receber uma autorização append-only pelo emissor. O digest precisa ter sido calculado a partir do payload planejado antes da aprovação, sem confiar em um hash fornecido pelo produtor.

O emissor exige modelo V9 ATIVO, aprovação ACTIVATE mais recente, dossiê completo e vigente de origem representativa declarada, fingerprint atual, snapshot/fit e threshold superior coerentes. A própria publicação confere o vínculo entre digest selado e motivo imutável: INSERT isolado na tabela de autorização não basta. Nenhum EXECUTE ou INSERT é concedido por esta migration; revisar e restringir privilégios DT-15 antes de qualquer uso. A etiqueta de origem representativa não certifica a evidência.

Esta é uma implementação técnica não certificada: ainda faltam matriz SQL positiva/negativa e atomicidade no LocalDB descartável, revisão REF #1089, busca física completa, revisão de segurança e fingerprint de schema recalculado. Não retirar o DRAFT nem ativar a política por causa desta alteração.


## Checkpoint DT-10 de integração — 30/10/2026 (PR #1199, sem ativação)

**Ordem e rollback.** A migration `20261029_Linkage_NoCandidate_Literal_Guards.sql` (#1210) precede `20261026_DT10_V9_Lower_Governed_Off.sql` (#1199) no manifesto e no instalador. A procedure de lote governada deve preservar o prefixo literal `SEM[_]CANDIDATO[_]%` e o erro 51835. O rollback controlado restaura a procedure **endurecida após a #1210**, nunca a definição histórica anterior. Qualquer formulação antiga de “procedure anterior” refere-se à versão endurecida imediatamente anterior à redefinição governada.

**Decisão multiobservação.** Para a mesma origem no mesmo run, a líder não pode publicar `SEM_CANDIDATO_*` se qualquer observação irmã materializada tiver `melhor_candidato_uuid`, `segundo_candidato_uuid` ou `pessoa_uuid_resolvido`. A guarda SQL percorre `#dt10_rows` por `pessoa_origem_id` e deve abortar o lote antes de qualquer evento, inclusive em lote misto; retry não pode produzir ledger. Isso não prova que o produtor executou todos os passes físicos de blocking.

**Evidência no SHA auditado `28125456ab349a865a52dc66f6d1e15234dbfa21`.** [Preflight LocalDB #41098709724](https://github.com/lucianox777/Jornada/actions/runs/41098709724): 1 teste NUnit PASS, 0 FAIL, 0 SKIP, TRX validado; cobre recusas OFF, candidato forjado na líder e em observação irmã, atomicidade mista e replay legado. [CI #41098709798](https://github.com/lucianox777/Jornada/actions/runs/41098709798), [schema #41098709771](https://github.com/lucianox777/Jornada/actions/runs/41098709771), [instalador #41098709760](https://github.com/lucianox777/Jornada/actions/runs/41098709760) e [validação independente #41098709936](https://github.com/lucianox777/Jornada/actions/runs/41098709936) concluíram SUCCESS. O candidato declara fingerprint estrutural `8b9adae24654e94bc668385ca9aa5c6c906744c0d8d88bd5b457f7ae9caa1f68` e SHA de manifest `c1d7d37f505563baea5d71d310e996945f5ffbf59d34fa6e24defbed3252e4b8`. [Holdout #41098709711](https://github.com/lucianox777/Jornada/actions/runs/41098709711): workflow SUCCESS, corridas físicas e agregador **SKIPPED**; não prova TEST selado. [Probe #41098709730](https://github.com/lucianox777/Jornada/actions/runs/41098709730): SUCCESS, job TRAIN 30k **SKIPPED**; não é novo ensaio de calibração.

**Bloqueios para merge.** Falta prova SQL **positiva** de autorização DT-15 ACTIVATE/digest legítimos (LOWER/intermediário/SEM_CANDIDATO/replay), negativos de adulteração individual de digest/fit/snapshot/limiares, atestado verificável de completude da busca física, contrato `upper` produzido pelo calibrador V9 e `lower` otimizado condicionado ao `upper`, revisão REF/loader #1089, VALIDATION/TEST selados e revisão humana. A PR segue DRAFT, política default OFF, sem aprovação, merge ou ativação. Revalidar todos os checks no SHA final após reconciliação com master. Os resultados históricos acima não certificam commits posteriores.

O emissor `auditoria.sp_autorizar_v9_dt10_lower_policy` exige ACTIVATE com motivo contendo digest canônico imutável. O teste atual recusa política inexistente e UPDATE/DELETE da autorização; não exercita a emissão positiva com dossiê legítimo. Não conceder privilégios amplos para simular aprovação.
