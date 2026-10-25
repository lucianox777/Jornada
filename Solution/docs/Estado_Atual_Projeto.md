> **Estado Gold V6 em 25/10/2026:** as PRs técnicas #1182 (SQL17/DT-10), #1183 (preflight LocalDB) e #1188 (CI seletiva) estão integradas; o [experimento V6 físico #40156139352](https://github.com/lucianox777/Jornada/actions/runs/40156139352) terminou com **12/12 corridas SUCCESS** e evidência agregada verificada. Ver [resultado e protocolo de validação externa ainda pendente](V9_Gold_V6_Resultado_Fisico_Candidato_Validacao_20261025.md). KNEE 0,05 é **candidato prioritário não ativado**, sem holdout novo, TEST, certificação ou migration canônica de política; issue #1089 ainda aberta. Esta nota supera **somente** o status Gold anterior, preservando snapshots datados.

> **ATUALIZAÇÃO DE ESTADO — corte de 24/10/2026 (master `8b53c845`):** consultar o [mapa de decisões arquiteturais V9/Gold](Mapa_Decisoes_Arquiteturais_V9_Gold_20261024.md). Esta atualização substitui **somente** a interpretação de estado de V9/Gold dos snapshots de 09/10 e 19/10 abaixo, não as decisões e evidências datadas. Decisões canônicas: Calibrador V9 C# único, bootstrap clean 30 mil, TF 0/1 e estados neutros explícitos, seleção interna TRAIN, Gold com Pareto 2D e teste de efeito cascata em três ordens; **nenhuma ativação V9/DT-15**. Os demais subsistemas não são reavaliados nesta atualização.

| Frente V9/Gold | Situação comprovada no corte | O que **não** se pode afirmar |
| --- | --- | --- |
| Pareto Gold V6 por faixas descritivas | [PR #1181](https://github.com/lucianox777/Jornada/pull/1181) **integrada** à `master`; regra joelho + até três representantes distintos de faixas. | Não é desempenho Gold físico V6 nem política operacional aprovada. |
| FDR, duplicatas e pendências na massa TRAIN | [Run V5 #40124876599](https://github.com/lucianox777/Jornada/actions/runs/40124876599) **failure**, com estudo Pareto estático e três controles CURRENT físicos concluídos; diagnóstico publicado na [issue #1180](https://github.com/lucianox777/Jornada/issues/1180). | Não há `gold-12-trial-matrix.json`, corridas físicas experimentais V6 ou certificação de risco. |
| Guard DT-10 para SQL Server 17 | [PR #1182](https://github.com/lucianox777/Jornada/pull/1182) **aberta** no corte; reconhece cabeçalho `CREATE PROCEDURE` normalizado e conserva guard fail-closed. | Não afirmar merge/conformidade da `master` antes dos gates. |
| Preflight DT-10 em LocalDB descartável | [PR #1183](https://github.com/lucianox777/Jornada/pull/1183) **aberta** no corte; preflight Windows real [#40141616684](https://github.com/lucianox777/Jornada/actions/runs/40141616684) **SUCCESS**, com `ALTER PROCEDURE` transacional, rollback e verificação da definição canônica. | Preflight verde não substitui CI completa, merge ou matriz física de 12 corridas. |
| Seleção/ativação de modelo | Calibrador V9 é o destino arquitetural; parâmetros iniciais e snapshots exigem gates independentes, TEST novo e DT-15. | Não confundir `ref` experimental, ensaio sintético, CI verde ou decisão documentada com promoção operacional. |

> **Atenção à data:** todo status de PR/CI acima é uma fotografia e deve ser conferido novamente no GitHub antes de qualquer merge. A seção principal que começa com “Estado técnico atual — atualização de engenharia 09/10/2026” permanece preservada como linha de base **histórica daquele dia** e não deve ser usada como retrato de V9/Gold em 24/10.

> **Implementação local V9 — 19/10/2026:** [snapshot portátil com TF, seleção pós-Resolve, validação executada e lacunas operacionais](V9_Integracao_Local_20261019.md). O incremento não representa homologação ou ativação; consulte a PR e os checks da HEAD vigente. As fotografias históricas abaixo não substituem esse inventário de código.

> **Auditoria superveniente de 18/10/2026:** [auditoria V9 e últimos runners](V9_Reconciliacao_Decisoes_Evidencias_Runners_20261018.md). A decisão atual é **bootstrap inicial do calibrador V9 somente com o corpus sintético clean**, parâmetros **não definitivos**, e `correlated` preservado **exclusivamente para histórico**. Foram executados SQL Server físico sintético e Rank→Resolve por fonte na PR #1152; **não equivalem** a Gold operacional, FDR certificada, novo TEST ou HML real. A PR #1152 é trabalho de branch até merge comprovado; CI verde não promove V9.

> **Decisão vigente (14/10/2026):** [Calibrador V9 C# único e definitivo; parâmetros iniciais do bootstrap congelados em `ref`, não permanentes; calibrações futuras manuais](Decisao_Calibrador_Unico_Adaptativo_Bootstrap_20261014.md). Implementação ainda em andamento na issue #1089; não confundir decisão com entrega ou ativação V9.

# Estado técnico atual — atualização de engenharia 09/10/2026

**Base observada:** `master` em `b4f74cb34e4346191ed58010b3d6b5cbb2db7e65` após #859, candidata v5.00/Schema 3.70; última release selada e precedência normativa continuam definidos por `RELEASE_INFO.txt` e pela Especificação Técnica publicada. Este bloco prevalece, **somente quanto ao estado de implementação em DEV/CI**, sobre as fotografias datadas posteriores neste arquivo; não altera decisões normativas nem aprova HML/Produção. Consulte o [manual do sistema](Manual_Sistema_Consolidado_20261009.md), [Console atual](Console_DEV_Supervisao_Atual.md), [índice documental](Indice_Acervo_Documental.md) e Actions da HEAD exata.

| Frente | Estado técnico em 09/10 | Evidência/limite |
|---|---|---|
| API, SQL, Resultado independentes | **Integrado no sandbox GitHub descartável** | #845, #846; não equivale a deploy |
| Três workers independentes e auto-restart | **Integrado/aceito em CI** | #847; processo reiniciado não é lote recuperado |
| Ingestão real de ZIP sintético, idempotência, rollback SIGKILL, lease/fencing e reprocessamento | **Integrado/aceito em CI** | #848–#850; banco `JornadaE2E` exclusivo |
| Console DEV: GET estado, toggle global ON↔OFF, três RunOnce isolados, desconexão, parada individual e painel | **Código em `master`, com testes em CI** | #851–#856; perfil GitHub DEV/loopback; sem implantação institucional |
| Cancelamento de RunOnce ativo com confirmação separada/ID de execução | **PENDENTE** | [C3.3b3](C3_3b3_Confirmacao_Cancelamento_RunOnce.md); #854 apenas evita perda de supervisão ao desconectar |
| CI: dez gates preservados e DT-10 SQL reutilizável | **Primeira extração integrada; não é otimização integral** | #857–#859, [DT10](DT10_CI_Extracao_Reutilizavel.md) |
| **Trilha 4 / DT-22 — revisão governada de RESOLVIDOS** | **Trilha 4 contínua ENCERRADA COMO PROPOSTA; DT-22 ABERTA e POSTERGADA** | [DT-22](DT22_Reavaliacao_Governada_Resolvidos.md) e [Plano](Plano_Desenvolvimento.md). `INCREMENTAL` não cobre genericamente RESOLVIDOS afetados por novos candidatos de terceiros; `REPLAY` atual reproduz modelo histórico. Replay extraordinário futuro **não entregue**. |
| Modelos de linkage, Ensaio, HML e PROD | **Aprovações próprias ainda necessárias** | Não inferir implantação/validade estatística representativa de CI sintética |

**Restrições:** não operar, resetar, migrar ou testar os dados originais de `JornadaLocal`/IBGE, o NODE/Compose do usuário ou HML/PROD para conferir este estado. As provas acima são históricas de GitHub Actions e seus alvos efêmeros. **Decisão revisada de 09/10:** a antiga Trilha 4 de varredura temporal foi **encerrada como frente autônoma**; a reavaliação de RESOLVIDOS passa à DT-22 postergada. **Não** declarar que a Gold se autocorrige completamente por evento nem que o replay após mudança de modelo esteja implementado.

## O que falta resolver — quadro de ação após a decisão de 09/10

Abaixo, **pendência não é tarefa já autorizada a modificar dados
reais**. São classes distintas; a prioridade final e critérios de
promoção dependem dos responsáveis por cada frente:

| Situação | Pendência de fato | Verificação para encerramento |
|---|---|---|
| **Técnica / produto** | [DT-05](DT05_Snapshots_Parquet_NAS.md) ainda tem itens de fechamento global de replay histórico NAS, retenção/GC e medição real de custo/latência; não confundir com DT-22. | Runbook, invariantes de snapshot imutável, métricas e aceite explicitamente registrado na HEAD exata. |
| **Técnica / qualidade de identidade** | Consolidar decisões executáveis da **V8**, TF nominal calibrado, não-presunção de independência, separação sem CPF e regras de Gold por atributo; validar por corpus/snapshot conforme as [decisões canônicas](Decisoes_Canonicas_Identidade_Linkage_20260929.md) e o [Plano](Plano_Desenvolvimento.md). | Testes adversariais/estratificados e avaliação estatística independente; nenhuma simples regra escrita vale como implementação. |
| **Técnica / Console DEV** | Confirmar autorização/encerramento explícito de RunOnce ativo e **histórico externo durável** fora da sessão; os merges #854–#856 não provaram tudo isso. | E2E no ambiente privado GitHub `JornadaE2E`, sem efeitos em NODE real. |
| **Técnica / CI e tooling** | Reduzir builds .NET repetidos após a extração DT-10; conferir o desalinhamento de instalador Windows .NET 8 vs `net10.0` (issue #790) e a PR #809 Dependabot antes de qualquer merge. | Gates de segurança/integração preservados, provenance de artefatos por SHA e regressões do instalador/lock. |
| **Institucional / Ensaio** | Avaliar risco de falsos vínculos e de RESOLVIDOS desatualizados, validar os contratos de Gestor, massas representativas, IdP/RBAC/PRODAM e condições HML/PROD. | Aprovações, corpus, rastreabilidade e evidências próprios; CI DEV sintética não as substitui. |
| **Posterior / não bloqueante desta proposta** | [**DT-22**](DT22_Reavaliacao_Governada_Resolvidos.md): replay extraordinário e governado de RESOLVIDOS por nova evidência/candidato ou modelo. **Nenhuma execução contínua da antiga Trilha 4.** | PR nova de implementação, E2E de escopo/conservação/retomada, evidência de riscos e autorização; **não foi feita agora**. |

**Fechamento da proposta arquitetural ≠ fechamento das dívidas
funcionais ou homologação.** A Gold é revisável e pode conter
associação probabilística ainda não revista; não afirmar
autocorreção completa de RESOLVIDOS no modo `INCREMENTAL`.
A [DT-22](DT22_Reavaliacao_Governada_Resolvidos.md) documenta
explicitamente o risco aceito até sua execução posterior.

---

## Fotografia documental anterior (preservada para contexto)

# Estado atual — conferência técnica documental atualizada em 06/10/2026

**Escopo:** fotografia de implementação e governança documental sobre a candidata técnica `master`, revista em 06/10/2026. A PR #702 citada em revisões anteriores já está integrada. Este arquivo não substitui consulta ao HEAD, Actions, issues/PRs, gates institucionais nem ao [Plano de Desenvolvimento](Plano_Desenvolvimento.md); para precedência entre tipos de documento, usar o [índice vivo do acervo](Indice_Acervo_Documental.md).

## Publicação e candidato

`RELEASE_INFO.txt` continua sendo a autoridade para a **última release selada de engenharia v4.05 (SolutionSchema 3.69)**. `LEIA-ME.txt` declara **candidata técnica v5.00**, RC `v5.00-rc.1` cortada com efeito de release `NONE`, e **SolutionSchema técnico 3.70**. A Especificação Técnica v5.00 permanece **candidata**, não publicação normativa final; a versão materializada histórica permanece referenciada em `Documentos/README.md`.

## Estado técnico comprovado e limites

- **DT-04:** `Jornada.Access.Security` centraliza `X-Jornada-Access-Key`, `AuthenticationHandler`, scopes/policies nas duas APIs; [PR #510](https://github.com/lucianox777/Jornada/pull/510). Os gates de matriz e testes HTTP cobrem negações. **HML/Produção permanecem deny-by-default** até IdP corporativo e aceite da issue #378.
- **DT-03:** OpenAPI v1 contém **22 operações com contratos tipados** para sucesso/erro e regressão DTO/HTTP; [PR #517](https://github.com/lucianox777/Jornada/pull/517).
- **Busca síncrona:** `POST /api/v1/identidade/candidatos` está implementado em `Jornada.Api`, com scope dedicado, auditoria anterior à resposta, até cinco candidatos sem score, sem CPF/UUID visível e com `nenhumDestes`. **Restrito a Development e gate de modelo/feature elegível**; não equivale a endpoint FHIR `Patient/$match` publicado ou ativação institucional HML.
- **DT-05:** ledger semântico append-only, assinatura V1, guardas de transação/run `EXECUTANDO` e E2E de **três ondas e CPF tardio real em massa sintética** [PR #540](https://github.com/lucianox777/Jornada/pull/540) implementados. **O PR #700 integrou o vínculo SQL imutável create-once do manifesto NAS já publicado/verificado ao `linkage_run_id`. No PR #702, já integrado à `master`, o Runner passou a orquestrar antes do score `materializar universo → capturar pins → construir/verificar manifesto → registrar binding SQL em transação → score`, com rehash físico dos ZIPs, JSON canônico e `input_snapshot_id` determinístico. Mesmo com esse incremento, ainda faltam** replay histórico determinístico, congelamento completo de runtime/governança, ensaios de concorrência/retenção/recuperação e medição de custo/latência; a flag permanece desligada por padrão.
- **DT-10 — concluída tecnicamente em 01/10/2026:** Runner utiliza `identidade.sp_publicar_resolucao_progressiva_linkage_lote`; o cursor por origem foi retirado, com equivalência escalar/idempotência/precedência em SQL. O PR #664 e o `jornada-ci` #36858619309 executaram 4/4 testes `DT10Evidence` sem skips em `JornadaSyntheticDev`, cobrindo concorrência adversarial, conflito concorrente, rollback por falha injetada e medição relativa de plano/volumetria. Não é SLA de Produção.
- **Blocking D/C/D∪C:** [PR #567](https://github.com/lucianox777/Jornada/pull/567) integrou diagnóstico sintético no mesmo universo; [PR #568](https://github.com/lucianox777/Jornada/pull/568) acrescentou auditoria SQL opcional somente leitura com contagens compartilhadas e latências observadas. **Não** publica passes C no Runner nem homologa equivalência populacional.

- **Higiene de engenharia em 06/10:** o PR #789 corrigiu o teste Python de manifesto DT-05, passou a executar `Solution/tools/tests/test_dt05*.py` no CI principal e alinhou o README da Solution para .NET 10. O PR #791 saneou navegação documental sem reescrever artefatos históricos. O PR #792 integrou um cleanup DT-12 fail-closed para branches cujo HEAD remoto coincide exatamente com o head de PR realmente merged em `master`, preservando proteção, PRs abertos, tags e referências ativas; a efetiva exclusão é comprovada pelo workflow pós-merge, não pelo texto deste documento.
- **Drift operacional conhecido:** a Solution corrente está em `net10.0`/SDK 10.0.112, mas o instalador Windows de produção ainda verifica/instala .NET 8. A correção técnica está rastreada na issue #790 e exige PR próprio com regressão do instalador; documentação não deve mascarar essa divergência.

`codigoPessoaOrigem` permanece **opcional**. A aderência integral da Gold à hierarquia de quatro níveis por atributo, a reavaliação de referências candidatas, o dossiê decisório DT-15 e a validade estatística independente #31 continuam a exigir conferência/implementação específicas. A Gold é representação revisável, não certificação civil.

**Fases aprovadas:** DEV → Ensaio único → HML → Produção. O Ensaio técnico exige massa sintética, funcionalidades, contratos e controles já prontos; HML requer massa das Secretarias, condições institucionais e segurança homologadas. CI verde, massa sintética e página master somente leitura em DEV não autorizam HML/Produção.

## Próximos gates

Seguir [prioridades e dependências](Plano_Desenvolvimento.md), [inventário DT com critérios verificáveis](Dividas_Tecnicas.md), [contrato de Ensaio](Ensaio_Unico_Paridade_HML.md) e [governança de decisão do modelo](DT15_Governanca_Decisao_Modelo.md). DT-01/09 exigem evidência CONFORME por modelo para promoção; DT-14 esclareceu o runbook, **sem retirar o gate de código**. DT-05 replay NAS e DT-15 HML/Produção continuam abertos; a DT-10 foi concluída tecnicamente, **E2E-A/DT-17A foi executado** pelo PR #666, **DT-02 foi concluída** pelo PR #668 em .NET 10, **E2E-B/DT-17B foi executado** no PR #677, a **DT-12 foi concluída** com cleanup run #36945815354 e 0/170 candidatas remanescentes, e **E2E-C/DT-17C foi executado** no HEAD `e51876b175ad212344033fd08d7f32687b2ed323` (PR #690; `jornada-ci` #36950535828; DT-05 E2E #36950535827). A sequência A × B × C ficou concluída no escopo técnico DEV; o próximo trabalho volta às pendências ativas do plano, sem promover automaticamente HML/Produção. A [DT-07 teve auditoria documental concluída](DT07_Conferencia_Drift_20260928.md), sem encerrar a triagem histórica de segredos #405; a DT-08 já tinha aceite técnico no PR #508. A DT-16 [PR #580](https://github.com/lucianox777/Jornada/pull/580) foi revertida em 30/09/2026 antes de qualquer migração física: as cinco solutions auxiliares e o gate específico foram removidos, e `Jornada.sln` voltou a ser a única solution suportada. A matriz transversal foi renumerada **DT-17**.


## Atualização transversal — revisão do parecer FS/IBGE (09/10/2026)

A [matriz de reconciliação do parecer externo](Reconciliacao_Parecer_Externo_20261009.md) separa críticas procedentes, decisões já tomadas, código observado e evidências ainda pendentes. Para o Calibrador, a referência demográfica diária projetada e a calibração sintética inicial são **artefatos diferentes**, ambos versionados e congelados em `ref` após publicação. O corpus sintético não certifica FDR em dados reais; o limite unilateral de FDR permanece candidato até implementação e validação. A PR de bootstrap demográfico não equivale à carga operacional executada nem à promoção de modelo.

Em **DEV v1**, não se exige retrocompatibilidade do runtime antigo, mas permanecem obrigatórios integridade de versões publicadas, histórico de decisões, testes do contrato vigente e preservação de branches históricos relevantes. O único decisor probabilístico é FS C#; Splink é conferência externa, sem duplicação de motor.


### Bootstrap congelado na subida — contrato de operação

**Decisão:** o pacote do sistema deve incluir o snapshot demográfico já materializado em `data/reference/synthetic-birth-sp/`, com manifesto e SHA-256, e o registro histórico do marco zero de calibração inicial em `ref`, quando produzido por execução explícita. Na inicialização, verificar integridade e existência da **referência demográfica** publicada no SQL Server; quando ausente, importar **os bytes já congelados** e publicar a referência, sem executar projeção IBGE, gerar nova população de referência ou recalibrar FS. **Não** executar nem exigir a calibração FS na subida: ela é acionada exclusivamente por demanda. Se os hashes/proveniência divergirem, falhar de forma explícita. Subidas subsequentes reutilizam as versões publicadas e não sobrescrevem dados. Novas calibrações são processos explícitos, separados da subida.

**Estado de entrega:** `scripts/verify-frozen-birth-reference.py` confere manifesto, SHA-256, esquema e linhas do arquivo local, sem alterar dados; ainda falta conectar o importador SQL e a verificação da referência congelada à inicialização automática. Assim, o comportamento completo descrito acima é **requisito de aceitação**, não funcionalidade já demonstrada em runtime.


**Decisão de operação (09/10/2026):** a **calibração FS é por demanda**, não faz parte do bootstrap, health/readiness nem da inicialização automática. O sistema sobe apenas com referências congeladas verificadas/carregadas. Quando uma calibração for solicitada, o resultado pode ser versionado em `ref` como marco histórico; ausência desse resultado não deve provocar recalibração automática ou bloquear a subida da infraestrutura.
