# Relatório final de auditoria — Jornada V9, mandato de fechamento (#1345)

> **CONCLUSÃO: NÃO CONCLUÍDO** no corte auditado, por evidências obrigatórias ausentes. Este documento registra o estado observado, **não** certifica projeto, TEST/Gold ou ativação. A PR documental alterará o SHA quando integrada; a validação deve então ser refeita na nova master.

## A. Identificação

| Campo | Valor |
| --- | --- |
| Repositório | [lucianox777/Jornada](https://github.com/lucianox777/Jornada) (público, master protegida) |
| Branch | master |
| SHA da master verificado antes de intervenção | **afbefd4c47aa217b83465364548499e400c8ed2a** |
| SHA final após integração documental | **PENDENTE** — não antecipar o novo SHA, exigir gates posteriores e aditamento versionado |
| Data | 08/11/2026 BRT; GitHub Actions de 09/11/2026 UTC |
| PRs integradas de referência | [#1343](https://github.com/lucianox777/Jornada/pull/1343), [#1346](https://github.com/lucianox777/Jornada/pull/1346), [#1347](https://github.com/lucianox777/Jornada/pull/1347), [#1350](https://github.com/lucianox777/Jornada/pull/1350) |
| PRs de evolução, conforme estado **superveniente** | **#1351 MERGED externamente** em `5c9a1598` às 01:54:12 UTC de 09/11, apesar da determinação anterior de congelamento; **#1352 DRAFT ABERTA**, autorizada posteriormente pelo PO. Não atribuir o merge ao mandato de fechamento. |
| Critério de teste | [#1345](https://github.com/lucianox777/Jornada/issues/1345) e [inventário original a–m](Teste_Geral_V9_a_m_Preflight_20261108.md) |

**Definições:** PASS=executado e aprovado no SHA indicado; FAIL=executado e reprovado; NOT RUN=não há execução do requisito no SHA; BLOCKED=evidência parcial, requisito composto não atestado ou aguarda integração/validação. Nem código existente nem checks em SHA diferente autorizam importar um PASS.

## B. Escopo, integrações e exclusões aprovadas

O mandato original inclui integração V9, testes da cadeia, migração, instalador, cluster, 30k, estudos V3/M/U/TF0/sobrenome, comparação histórica, testemunha V2, segurança e documentação. O PO determinou executar apenas o fechamento e fazer triagem das issues. A decisão vigente **restabelece a triagem**, antes dispensada em adenda histórica, e substitui a regra anterior de zero PRs por preservação explícita das duas PRs de evolução #1351/#1352. O exploratório de #1352 foi **expressamente autorizado posteriormente**, não é trabalho indevido. Não foi mudado plan.json aprovado, REF, CI/30k, política científica, modelo ativo ou infraestrutura.

As PRs #1347 e #1350 foram integradas antes deste corte. Sua prova física ocorreu **nos SHAs de estágio**, não neste SHA de master; a #1347 empregou motor operacional **V8**, não o V9 multiparâmetro em 100k. Não iniciar 100k V9 somente para ampliar a campanha; lacuna [#1349](https://github.com/lucianox777/Jornada/issues/1349) permanece. Esta Frente A não editou ou mesclou #1351/#1352; o merge externo da #1351 requer registro e reconciliação de governança.

## C. Matriz completa dos itens originais a–m — SHA, teste, resultado e defeitos

| Item | Requisito | Implementação | Teste necessário/executado | SHA do corte | Run ID | Resultado | Evidência concreta | Defeito ou correção ainda necessária | Situação |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **a** | CI completa | ci.yml e suítes unit, SQL, E2E, segurança | unit-runtime 2155 PASS/0 SKIP; integration-sql 162 PASS/0 SKIP; E2E, CodeQL, contratos e segurança SUCCESS | afbefd4c47aa | 42541800549 | **PASS** | artifacts 13642036212 e 13641703882; logs TRX; run SUCCESS | Falhas antigas de NUnit/reflection e sanity documental corrigidas na PR #1346 | Verificado no SHA base, revalidar se master avançar |
| **b** | Instalador Windows/bundle | windows-production-installer.yml, bundle, manifesto schema | Execução Windows e inventário de instaláveis no SHA final | afbefd4c47aa | Nenhum no SHA | **NOT RUN** | Nenhum workflow Windows nesse SHA; CI Linux não substitui build de instalador | Sem evidência de instalação física Windows neste commit | Pendente |
| **c** | Cluster e topologia 2 nós | cluster-topology.yml, local-cluster-readiness | NODE1/NODE2, healthbeats, NAS bidirecional, schema e conexões | afbefd4c47aa | Nenhum no SHA | **NOT RUN** | Nenhuma Action cluster-topology entre as runs deste SHA | SQL/E2E CI não prova cluster real de dois nós; não tocar banco original | Pendente |
| **d** | E2E HTTP/Console/SQL funcional | ci.yml, local-e2e scripts, API, Processor | E2E Console + private SQL + agregado | afbefd4c47aa | 42541800549 | **PASS** | artifacts 13641198233 e 13641583767, jobs e2e-console/e2e-private-sql/e2e SUCCESS | E2E volumétrico HTTP 1k passou no SHA distinto 655e3402, não emula rerun na master | PASS funcional, sem extrapolar volume |
| **e** | Ingestão, ledger, API e Policy | API+Processor, ledger, autorização, OpenAPI | Provas positivas e negativas individuais, 401/403/409, idempotência e estados | afbefd4c47aa | 42541800549 parcial | **BLOCKED** | E2E, contratos, OpenAPI 79/79 e segurança SUCCESS; artifacts 13641198233 e 13641847229 | Matriz de cobertura 1×1 por cenário, ledger e Policy não foi atestada integralmente no mesmo SHA | Cobertura parcial |
| **f** | Migração SQL e inventário | migrations, schema-manifest, ddl-upgrade, integration-sql | Upgrade/replay SQL, 162 testes, inventário exato no SHA | afbefd4c47aa | 42541800549 parcial | **BLOCKED** | ddl-upgrade e 162/162 SQL SUCCESS; artifacts 13641244448 e 13641703882 | Inventário consolidado final de schema e de payload/arquivos ainda sem atestado abrangente | PASS físico parcial, inventário pendente |
| **g** | Validação independente | linkage-independent-validation.yml | Validação adversarial independente no SHA e relatório | afbefd4c47aa | Nenhum no SHA | **NOT RUN** | Workflow específico path-filtered não disparado neste SHA | Harness/seed42 TRAIN não é prova de validação independente no SHA final | Pendente |
| **h** | Regressão 30k física completa | v9-physical-seed42-probe.yml | classify + full physical SQL TRAIN + SQL físico clean | afbefd4c47aa | 42541800562 | **PASS** | 3/3 jobs SUCCESS, artifact 13641303523 SHA-256 a9892e8a1528f2e763a85704f4a3e292feb07256f2719476805b536f51933647; 135 braços TF+12 diagnósticos | 30k executado fisicamente, não SKIPPED; não é TEST/Gold ou FDR científico | PASS técnico no SHA base |
| **i** | V3 direto, M/U, TF0 e sobrenome | cinco workflows de regressão V9 seed42 e V3 broad | V3, M/U, TF0 mesmo frame, sobrenome, treino amplo | afbefd4c47aa | 42541800602; 42541800530; 42541800495; 42541800543; 42541800469 | **PASS** | cinco Actions SUCCESS no SHA; artifacts 13642655017 e 13641901431; contratos TRAIN | Prova de engenharia, não certificação estatística ou ativação | PASS técnico no SHA base |
| **j** | Comparação f1bd50fd × master | v9-j-historical-analytic-comparison.py | Comparar dois pares reais plan.json + mu-statistical-analysis no mesmo recorte | afbefd4c47aa | Nenhum comparativo final | **NOT RUN** | Código comparador e testes offline existentes; stage J 42529456510 não compara f1bd50fd ao SHA final | Não declarar IDÊNTICO/DIFERENTE nem NÃO COMPARÁVEL sem inspecionar os dois pares | Pendente |
| **k** | V2 testemunha e compatibilidade | v9-multiparameter-official-plan.py; V9NestedCalibrationCli e Coordinator; auditor J | WITNESS+ANCHOR, plano SHA, 24/16/8, frame compartilhado e reproduzível | afbefd4c47aa | 42529456510 no SHA 8832850a; unit 42541800549 | **BLOCKED** | Stage J físico dois papéis SUCCESS; código integrado guarda plan.json/receipt/fitted/sha; unidades 2155 PASS | Nenhum teste J oficial físico 24/16/8 localizado no SHA master atual; stage não é SHA final | Contrato positivo, físico SHA final pendente |
| **l** | Segurança, copyright e 30k push-only | LICENSE; workflow v9-physical-seed42-probe; ci.yml | CodeQL, source-sanity, security-analysis; ausência dispatch 30k | afbefd4c47aa | 42541800549; 42541800562 | **PASS** | jobs segurança SUCCESS, LICENSE copyright exclusivo PO; YAML push master sem workflow_dispatch | #1253 expôs senha efêmera em log antigo, exigindo auditoria histórica; bootstrap novo relatou máscara | Controles PASS no SHA, risco histórico separado |
| **m** | PRODAM, documentação e segregação das PRs | especificação v5.00, revisão 31 ocorrências, governança GOV-DOC-01 | neutralização 23/31, justificativa 8, PDF editorial e preservação #1351/#1352 | afbefd4c47aa | Edição na branch ainda não integrada | **BLOCKED** | 31 ocorrências classificadas; branch com 23 neutralizações pontuais e 8 preservações; PDF #1344 com 51 ocorrências | Falta merge documental e validação no novo SHA; duas PRs DRAFT são exceção válida, não bloqueio por número | Pendente integração dos registros |

**Consolidação a–m: 5 PASS (a,d,h,i,l), 4 NOT RUN (b,c,g,j), 4 BLOCKED (e,f,k,m), 0 FAIL identificados em execução nova.** O resultado NÃO CONCLUÍDO decorre da falta de atestado, não da afirmação de que componentes não testados falharam.

## D. Qualidade — gates, testes físicos e E2E

### Provas no SHA exato afbefd4c47aa217b83465364548499e400c8ed2a

- [run 42541800549](https://github.com/lucianox777/Jornada/actions/runs/42541800549): **jornada-ci COMPLETED/SUCCESS**. Job unit-runtime: **2.155 aprovados, 0 falhas, 0 ignorados**; integration-sql: **162 aprovados, 0 falhas, 0 ignorados**. OpenAPI runtime: 79/79. E2E Console, E2E privado SQL, agregador E2E, deterministic-build, ddl-upgrade, security-analysis, CodeQL e source-sanity SUCCESS. Artifacts principais: 13642036212 (unit), 13641703882 (integration), 13641583767 (E2E SQL privado), 13641198233 (E2E), 13641244448 (ddl).
- [run 42541800562](https://github.com/lucianox777/Jornada/actions/runs/42541800562): **30k físico realmente executado** na master, três jobs SUCCESS: classificação de impacto, Full Physical 30k SQL TRAIN e SQL físico 30k clean. Artifact 13641303523 com digest SHA256 **a9892e8a1528f2e763a85704f4a3e292feb07256f2719476805b536f51933647**, log com 135 braços TF e 12 diagnósticos, sem ativação ou certificação.
- [run 42541800602](https://github.com/lucianox777/Jornada/actions/runs/42541800602) V3 direto, [run 42541800530](https://github.com/lucianox777/Jornada/actions/runs/42541800530) V3 broad, [run 42541800495](https://github.com/lucianox777/Jornada/actions/runs/42541800495) M/U seed42, [run 42541800543](https://github.com/lucianox777/Jornada/actions/runs/42541800543) TF0/V2×V3 same-frame, [run 42541800469](https://github.com/lucianox777/Jornada/actions/runs/42541800469) sobrenome: todos **SUCCESS** no SHA de referência. Provas técnicas TRAIN, não FDR de população real.
- E2E privado (run 42541800549) passou sql-bootstrap, readiness API, console ligado/desligado, worker SIGKILL, ingestão, recovery de transação, supervisor, três workers run-once, desconexão, parada e journal. **Isso não prova dois nós com NAS compartilhado** do cluster original.
- Jobs condicionalmente SKIPPED da jornada-ci: scale-harness, bronze-restore-drill, rc-evidence, release-promotion e rc-publish. Não chamar skip de teste executado.

### Estudos históricos distintos — não transferir o PASS à master final

| Estudo | SHA ensaiado | Run, artifact e evidência | Limite |
| --- | --- | --- | --- |
| SQL SCALE medium 100k | 8d90306cda1d930291318ca5a4f3e7bf5fc93e4a | [run 42534643000](https://github.com/lucianox777/Jornada/actions/runs/42534643000), artifact 13639566469: 100.000 pessoas/25.000 pendências, 21.927 corretas, 0 FP observados, 31 conflitos, 3.042 não resolvidas | **Motor V8**, não multiparâmetro V9 e nem E2E HTTP; baseline HML pendente, sem certificação populacional |
| E2E HTTP 1k | 655e340215e21d822288f995849061f664f5685a | [run 42541173969](https://github.com/lucianox777/Jornada/actions/runs/42541173969), artifact 13641457473: 1 Bronze, 1k Silver/Gold/Serving, 2k itens, replay idempotente, 24,316s | **Stage diferente da master**, p95 não medida com uma remessa; 10k e 100k HTTP não cobertos |
| J V2 WITNESS+ANCHOR | 8832850aa11a0d14bbffa9debbf1aa2d80ceb5ff | [run 42529456510](https://github.com/lucianox777/Jornada/actions/runs/42529456510): 2 papéis, ensaio físico 24/16/8 e confronto histórico | Stage diferente da master, não atesta item k no SHA final |
| 30k master anterior | 3e5885c2a8bad39b03a453eedb8c00a110731b40 | [run 42536491348](https://github.com/lucianox777/Jornada/actions/runs/42536491348) físico SUCCESS | Não substitui execução nova 42541800562 |

**Defeitos/regressões conhecidos:** o SHA anterior c6451cf2 teve CI FAIL por dois testes de reflection e literal documental de senha; correção [#1346](https://github.com/lucianox777/Jornada/pull/1346) integrada, neste SHA unidade e contratos PASS. A [#1302](https://github.com/lucianox777/Jornada/issues/1302) registra **OOM físico em 160 configurações** no [run 42283035832](https://github.com/lucianox777/Jornada/actions/runs/42283035832); 30k atual foi verde, mas **não há comparação completa mesmo perfil** comprovando eliminação, risco P0 ainda aberto. A [#1253](https://github.com/lucianox777/Jornada/issues/1253) documenta senha SQL efêmera exposta em log histórico; o novo run informa bootstrap mascarado, mas falta auditoria retrospectiva e confirmação de não reutilização. A [#1257](https://github.com/lucianox777/Jornada/issues/1257) registra risco de comparabilidade TF×TF0 sob abstenção. Não afirmar segurança ou ausência de regressão fora do que as evidências cobrem.

## E. Governança

### Triagem de todas as issues

[Inventário completo de 57 issues com justificativa e referências](Triagem_Issues_Fechamento_V9_20261108.md): **1 P0 histórico com risco impeditivo não reconciliado; 7 P1 necessárias à verificação; 47 P2 de evolução posterior; 2 checkpoints absorvidos**. As issues [#1220](https://github.com/lucianox777/Jornada/issues/1220) e [#1221](https://github.com/lucianox777/Jornada/issues/1221) foram encerradas como duplicadas por corresponderem exclusivamente a checkpoints de PRs **#1215** e **#1199**, respectivamente já mergeadas; evidência e explicação foram comentadas. **55 abertas** permanecem. Redução do total não é critério de aceite.

### Revisão das 31 referências à PRODAM

A [matriz por linha, classe, decisão e justificativa](Revisao_PRODAM_v5_00_20261108.md) identificou **31 referências literais na especificação v5.00**: **23 operacional/arquiteturalmente desnecessárias neutralizadas de forma pontual** na branch de fechamento e **8 mantidas** por história, referência legal/administrativa, negação explícita de integração ou legado SQL ENRIQUECIMENTO_PRODAM. Restam 8 ocorrências literais nesse Markdown **após integração da proposta**; a master auditada ainda não contém essas alterações até o merge. Nenhuma alteração na REF, migrações ou norma contratual. Oito PDFs com 51 menções editoriais já diferidos permanecem abertos na [#1344](https://github.com/lucianox777/Jornada/issues/1344); **neutralização total do acervo não foi afirmada**.

### V2 testemunha — determinismo, plano, hash e resultado

A implementação já contém: geração determinística do plano oficial em **Solution/scripts/v9-multiparameter-official-plan.py** (fonte/role pin, persistência create-only), documento **plan-receipt** com SHA de plano original e oficial; a CLI C# **V9NestedCalibrationCli.cs** cria diretório de execução novo/imutável, preserva plano efetivo, fitted content-addressed, selection-reference, training-split-manifest e mu-statistical-analysis; o coordenador C# vincula PlanSha, RequestSha, CandidatePairsSha e fitSnapshotSha, conta **24 avaliações físicas, 16 testemunhas/diagnósticos e 8 CP selecionáveis**; V2 é **WITNESS+ANCHOR**, não concorre à seleção. O auditor **v9-j-official-physical-audit.py** confere plano/receipt/hash e comparabilidade, com stage físico 42529456510 SUCCESS.

**Parecer técnico:** não se identifica necessidade técnica de arquivar **um plano estático adicional antes da execução**: o plano gerado efetivamente é salvo com hash e vínculo reproduzível; existe versionamento via arquivo+SHA/manifest. Porém **falta atestado físico específico no SHA de master final**, de modo que **item k permanece BLOCKED**, não PASS integral. Não alterar plan.json e REF aprovados nem dispensar explicitamente o versionamento existente.

### Workflows — finalidade, gatilhos, recursos e não interferência

| Workflow | Finalidade | Disparo e trava | Recursos, tempo e artefatos | Relação com mandatório |
| --- | --- | --- | --- | --- |
| [v9-medium-scale-e2e-technical.yml](../../.github/workflows/v9-medium-scale-e2e-technical.yml) | SCALE 100k sintético SQL com Calibrador/Runner operacional V8; **não HTTP** | **workflow_dispatch**, branch master e SHA exato, sem trigger push | ubuntu-latest, limite 180min, fase 165min; seed355, paralelismo4, batch10k; SQL descartável, resumo agregado 30 dias, cleanup | **Opcional**, não altera nem substitui 30k/CI; nenhum disparo novo nesta rodada |
| [v9-http-volume-1k-e2e-technical.yml](../../.github/workflows/v9-http-volume-1k-e2e-technical.yml) | 1k HTTP até Serving e replay | **workflow_dispatch**, master-only/SHA exato, sem trigger push | ubuntu-latest, limite 100min, fase 85min; SQL descartável, artifact JSON agregado 30 dias, sem ZIP/dados pessoais, cleanup | **Opcional**, não substitui E2E básico nem 30k; nenhuma execução nova |
| [v9-physical-seed42-probe.yml](../../.github/workflows/v9-physical-seed42-probe.yml) | Gate obrigatório de regressão 30k física, corpus seed42 e hashes | **push exclusivo master**, classificador impacto acumulativo, **sem dispatch** | runner SQL descartável, timeout físico 360min, evidence SHA-bound | **Gate obrigatório quando materialmente impactado**, executado 42541800562; política não alterada |
| [ci.yml](../../.github/workflows/ci.yml) | Unit, SQL integração, E2E, segurança, CodeQL, DDL e gates contratuais | CI do repositório nos eventos aprovados | paralelismo CI, artifacts de relatório; custo não quantificado neste corte | **Obrigatório**, SUCCESS no SHA 42541800549 |

Nenhum workflow opcional de escala foi iniciado, cancelado ou alterado. O mandato do PO exigia #1351 HTTP 10k DRAFT, mas houve **merge superveniente da #1351** por ação independente à Frente A, contrariando o congelamento. A #1352 **permanece DRAFT**, com autorização posterior do PO; não realizar merge nela.

## F. Pendências, gravidade e impacto

**P0 impeditiva até comprovação:** #1302 OOM historicamente físico em 160 configurações — evidência atual não prova perfil idêntico. A gravidade é risco histórico **não resolvido documentalmente**, não alegação de falha no HEAD atual.

**P1 necessárias ao mandato:** conclusão dos itens **b,c,e,f,g,j,k,m**, gates e inventário no **SHA pós-merge**, comparação analítica de f1bd50fd vs SHA final, mapeamento de ingestão/ledger/Policy, prova específica do V2 oficial, verificação de bundle Windows e cluster NODE1/NODE2, validação independente, e reconciliação de riscos de #1253/#1257/#1292/#946/#1089/#1228. Correções de produto só se defeito impeditivo for efetivamente demonstrado; não começar nova campanha científica por conveniência.

**P2 registradas, não impeditivas da modalidade estrita:** oito PDFs/51 ocorrências editoriais #1344 (diferimento expresso), HTTP 10k/100k e p95 #1348, 100k V9 multiparâmetro #1349, seeds adicionais, GPU, TEST/Gold novos, HML/PROD, novos contratos e apoio externo. O resultado 0 FP observado em 100k V8 não atesta FDR real ou V9; nada autoriza DT-15 ou mudança do modelo ativo.

## G. Conclusão

**NÃO CONCLUÍDO**. O sistema possui provas importantes na master afbefd4c47aa (CI completa, 30k físico real, V3 direto, M/U, TF0, sobrenome, E2E funcional e 162 SQL). Mas o mandato exige todos os itens obrigatórios a–m no SHA final, documentos integrados, riscos impeditivos reconciliados e nenhum resultado desconhecido. Não executar um teste obrigatório equivale a **NOT RUN**, não PASS. Nenhum FAIL atual foi encontrado nos gates executados; lacunas restantes não provam incapacidade, porém impedem aceite.

**Próximo critério verificável, sem ampliar o escopo:** integrar esta frente via PR protegida com checks, identificar SHA pós-merge e verificar CI e impacto/30k; comprovar apenas as superfícies faltantes conforme mandato; aditar o relatório no repositório, preservando a #1352 e documentando, sem apagar/reverter automaticamente, o fato do merge da #1351. Se alguma prova necessária continuar ausente, a classificação permanecerá **NÃO CONCLUÍDO**.

**Governança:** [mandato e precedência](Decisao_V9_Fechamento_Controlado_20261108.md), [triagem](Triagem_Issues_Fechamento_V9_20261108.md), [revisão PRODAM](Revisao_PRODAM_v5_00_20261108.md), [índice GOV-DOC-01](Indice_Decisoes_Vigentes.md), [issue #1345](https://github.com/lucianox777/Jornada/issues/1345).

## Incidente temporal após o corte inicial — #1351 integrada contrariando o congelamento

**Fato verificado diretamente no GitHub:** [PR #1353](https://github.com/lucianox777/Jornada/pull/1353), que registra o congelamento superveniente do PO, foi aberta às **2026-11-09 01:53:43 UTC**. A [PR #1351](https://github.com/lucianox777/Jornada/pull/1351) foi **MERGED às 2026-11-09 01:54:12 UTC** (29 segundos depois), gerando o commit master **`5c9a1598812b0cf5433a523f0f8498efa5ecba60`**, com fluxo HTTP 10k e documentação. Não foi feita por esta Frente A, que havia apenas consultado a PR ainda DRAFT e criado branch documental a partir de `afbefd4c`. O merge foi contrário ao requisito temporal de preservação, e **nenhuma aprovação retroativa é presumida**. Não executar `revert`, exclusão ou reescrita da master sem decisão expressa do PO; preservar artefatos, SHA e histórico integral. A [PR #1352](https://github.com/lucianox777/Jornada/pull/1352) **continua DRAFT** e não foi alterada.

**Novo corte adicional de prova:** no SHA `5c9a1598812b0cf5433a523f0f8498efa5ecba60`, [jornada-ci 42547980358](https://github.com/lucianox777/Jornada/actions/runs/42547980358) e [30k físico 42547980370](https://github.com/lucianox777/Jornada/actions/runs/42547980370) concluíram **SUCCESS**, incluindo os jobs full physical 30k e SQL clean; V3 direto 42547980328, M/U 42547980343 e TF0 42547980353 SUCCESS. Na primeira leitura deste aditamento, V3 broad 42547980344 e sobrenome 42547980401 ainda **IN_PROGRESS**. Esses resultados são de outro SHA e não substituem os testes a–m por item nem a verificação **pós-merge** desta PR documental. A classificação geral continua **NÃO CONCLUÍDO**. A [PR #1355](https://github.com/lucianox777/Jornada/pull/1355) propõe triagem em paralelo; não efetuar merge cruzado nem sobrescrever seus registros sem reconciliação.
