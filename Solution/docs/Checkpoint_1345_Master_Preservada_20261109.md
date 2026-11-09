# GOV-V9-1345-CHECKPOINT-20261109 — Continuidade isolada, preservação da master

**Decisão aprovada pelo PO:** manter os resultados existentes da `master` relativamente à [issue #1345](https://github.com/lucianox777/Jornada/issues/1345) e continuar o fechamento **sem integrar novas mudanças agora**. **Estado:** registro e execução de auditoria **isolados nesta branch**; **NÃO é aceite** do mandato. Não mover `master`, não editar a #1352 ou #1356, não reverter a #1351, não mexer em `REF`, plano, TEST, Gold, DT-15 ou política 30k. Nova PR deve ser **DRAFT e sem merge**, até autorização posterior do PO.

## 1. Snapshot de referência imutável (não recalcular o passado)

- **SHA observado de `master` no início deste checkpoint:** `f3b33b5011fddc39b2e42effaceeb961fd4e1213`; merge documental [#1357](https://github.com/lucianox777/Jornada/pull/1357) de rotulagem de issues. Não é o mesmo SHA da matriz inicial `afbefd4c` nem do relatório integrado pela [#1354](https://github.com/lucianox777/Jornada/pull/1354), `e0f0f323`.
- **A matriz original da #1354 permanece válida apenas no seu corte histórico**: **5 PASS, 4 NOT RUN, 4 BLOCKED**, conclusão **NÃO CONCLUÍDO**. O arquivo [Relatorio_V9_Fechamento_Integracao_20261108.md](Relatorio_V9_Fechamento_Integracao_20261108.md) na `master` não deve ser retroativamente reescrito como se contivesse testes finais. Este checkpoint é seu complemento por SHA.
- **Mudanças da master desde o merge #1354:** somente [#1357](https://github.com/lucianox777/Jornada/pull/1357), relativa a documentação de labels, entre `e0f0f323` e `f3b33b50`; o diff dessa comparação contém os índices e a triagem, **não** motor, REF, plano ou workflows. Isso apoia a análise de impacto, mas **não substitui os runs no SHA atual**.
- **Resultados de engenharia históricos preservados:** `afbefd4c` teve [jornada-ci 42541800549](https://github.com/lucianox777/Jornada/actions/runs/42541800549) e [30k completo 42541800562](https://github.com/lucianox777/Jornada/actions/runs/42541800562) **SUCCESS**; `5c9a1598`, último SHA de alteração de código HTTP 10k, teve [CI 42547980358](https://github.com/lucianox777/Jornada/actions/runs/42547980358) e [full 30k 42547980370](https://github.com/lucianox777/Jornada/actions/runs/42547980370) **SUCCESS**, inclusive o job físico completo. `e0f0f323` teve [CI 42549657489](https://github.com/lucianox777/Jornada/actions/runs/42549657489) **SUCCESS**, [30k 42549657462](https://github.com/lucianox777/Jornada/actions/runs/42549657462) **SUCCESS** com **full 30k TRAIN SKIPPED** por classificador e **SQL 30k clean SUCCESS**; V2×V3/TF0 42549657424, M/U 42549657413 e sobrenome 42549657387 SUCCESS. V3 amplo 42549657377 e direto 42549657426 **estavam em andamento** na inspeção, sem atestar conclusão.
- **SCALE 100k:** [#1347](https://github.com/lucianox777/Jornada/pull/1347) executou **motor operacional V8**; não demonstra escalabilidade multiparâmetro V9, lacuna [#1349](https://github.com/lucianox777/Jornada/issues/1349). HTTP 10k entrou pela [#1351](https://github.com/lucianox777/Jornada/pull/1351) após a determinação de congelamento, fato documentado; não atribuir aprovação retroativa nem afetar o teste a–m.
- **V2 testemunha:** [Action 42529456510](https://github.com/lucianox777/Jornada/actions/runs/42529456510), artifacts 13637625606 e 13635944882, em **SHA de estágio 8832850a**: 24 braços físicos (16 testemunhas/diagnósticos, 8 candidatas), WITNESS+ANCHOR, planos, hashes, receipts e vínculos verificados; **não comprova J físico no SHA `f3b33b`**. O comparador histórico ali executado compara outros dois SHAs, **não** `f1bd50fd`×`f3b33b`.
- **OOM #1302:** há prova física positiva de **160/160 surname/seed42 sem OOM** em [run 42337308236](https://github.com/lucianox777/Jornada/actions/runs/42337308236), artifact **13560831743**, SHA de estágio `b1828034`, pico RSS global **6.224.506.880 B**, usando a margem contemporânea **0,0001** em vez de zero; portanto é incorreto dizer que nunca houve reteste. A reconciliação de equivalência e regressão ampla da master final permanecem como aceite separado; **não inventar falha nova**.

## 2. Execuções pós-merge da master `f3b33b5011fddc39b2e42effaceeb961fd4e1213`

**Fonte:** GitHub Actions do `head_sha` exato `f3b33b5011fddc39b2e42effaceeb961fd4e1213`; consulta dos jobs do 30k e da CI. Este é um **corte temporal**, não previsão de resultado futuro:

| Workflow | Run | Observação no corte | Conclusão que pode ser afirmada |
| --- | ---: | --- | --- |
| `v9-physical-seed42-probe` | [42550726275](https://github.com/lucianox777/Jornada/actions/runs/42550726275) | **COMPLETED/SUCCESS**; `classify impact` SUCCESS; `SQL físico 30k clean` SUCCESS; **`V9 full physical 30k SQL TRAIN` SKIPPED** | **PASS do workflow e da política de impacto documental**. **Não** declarar full 30k TRAIN executado neste SHA. Last full 30k no código `5c9a1598`. |
| `jornada-ci` | [42550726253](https://github.com/lucianox777/Jornada/actions/runs/42550726253) | **IN_PROGRESS** na consulta; `impact`, `ddl-upgrade`, `dependency-lock`, `security-guards`, `dt10-evidence` SUCCESS, demais jobs incompletos | **NOT RUN/UNKNOWN para o resultado final da suíte** até conclusão; não inferir verde de `e0f0f323`. |
| `v9-v3-direct-physical` | [42550726232](https://github.com/lucianox777/Jornada/actions/runs/42550726232) | **IN_PROGRESS** | Sem veredito no SHA atual. |
| `v9-mu-seed42-technical-regression` | [42550726231](https://github.com/lucianox777/Jornada/actions/runs/42550726231) | **IN_PROGRESS** | Sem veredito no SHA atual. |
| `v9-mu-tf0-controlled-same-frame` | [42550726273](https://github.com/lucianox777/Jornada/actions/runs/42550726273) | **IN_PROGRESS** | Sem veredito no SHA atual. |
| `v9-v3-surname-dependence-plan` | [42550726198](https://github.com/lucianox777/Jornada/actions/runs/42550726198) | **IN_PROGRESS** | Sem veredito no SHA atual. |
| `v9-v3-broad-train-regression` | [42550726339](https://github.com/lucianox777/Jornada/actions/runs/42550726339) | **IN_PROGRESS** | Sem veredito no SHA atual; observar especialmente `surname/seed42` 160 configurações e RSS. |

**Nenhuma Action adicional foi disparada por esta auditoria.** A ausência de `windows-production-installer`, `cluster-topology` e `linkage-independent-validation` entre as runs do SHA é verificável. Os três workflows **admitem `workflow_dispatch`**, além de gatilhos por mudança de caminho, mas não devem ser declarados `PASS` só porque existem no YAML. **Não alterar o workflow 30k**, que é push-only master.

## 3. Matriz a–m: situação preservada e próximos requisitos

| Item | Evidência no corte f3b33b | Status de aceite por item | Condição objetiva sem alterar escopo |
| --- | --- | --- | --- |
| **a** CI | 42550726253 ainda em andamento | **BLOCKED** | Atestar conclusão `jornada-ci` SUCCESS com jobs exigidos no SHA exato. |
| **b** Bundle Windows | Workflow próprio com despacho manual, sem run do SHA atual | **NOT RUN** | Executar e atestar bundle Windows, schema, instalador e artifact com HEAD SHA. |
| **c** Cluster/topologia | Workflow próprio com despacho manual, sem run do SHA atual | **NOT RUN** | Executar `cluster-smoke` com NODE1/NODE2 e Bronze, sem infraestrutura original do PO. |
| **d** E2E geral | E2E anterior `e0f0f323` aprovado; E2E atual ainda em CI | **BLOCKED** | Conferir E2E Console + SQL privado + agregado no run 42550726253. |
| **e** Ingestão/ledger/API/Policy | E2E e OpenAPI anteriores não cobrem matriz por superfície completa | **BLOCKED** | Vincular cada cenário positivo/negativo a um teste e artifact sem criar funcionalidade. |
| **f** Upgrade/inventário SQL | `ddl-upgrade` da CI atual SUCCESS; SQL integração e inventário geral ainda não encerrados | **BLOCKED** | Conciliar schema-manifest, migrações, payload e execução SQL do SHA. |
| **g** Validação independente | Workflow específico, sem run do SHA | **NOT RUN** | Executar e auditar relatório independente no SHA atual; não certifica FDR real/TEST. |
| **h** 30k físico/política | 42550726275 classificador e SQL clean SUCCESS; full TRAIN corretamente SKIPPED por impacto | **PASS da política**, não PASS de full TRAIN físico no SHA | Correlacionar o último full TRAIN de código 5c9a1598 com classificação documental f3b33b; registrar explicitamente SKIPPED. |
| **i** V3/M/U/TF0/sobrenome | Cinco runs no HEAD atual em andamento | **BLOCKED** | Registrar status final e artifacts de cada run, sem importar PASS anterior. |
| **j** Comparação histórica | Comparador offline existe; não há pareamento `f1bd50fd` × `f3b33b` atestado | **NOT RUN** | Obter dois pares válidos de plano+análise, verificar mesmo recorte e classificar idêntico/diferente/não comparável por evidência. |
| **k** V2 testemunha | Plano deterministicamente materializado e vinculado por SHA; stage 8832850a validado | **BLOCKED** | Verificação física J específica no SHA final ou justificação documental que atenda critério original sem reexecutar campanha não obrigatória. |
| **l** Segurança/licença/30k push-only | Security-guards SUCCESS; `LICENSE` e trigger sem mudança; jobs CodeQL/CI ainda em andamento | **BLOCKED** | Aguardar jobs segurança completos do SHA; separar histórico #1253. |
| **m** PRODAM/triagem/limites | 23/31 referências neutralizadas e 8 preservadas na master; #1357 registra labels reais; #1352/#1356 DRAFT; incidente #1351 preservado | **BLOCKED** | Atestar situação final documental e reconciliar divergência de governança sem apagar históricos ou desfazer merges. |

**Conclusão deste snapshot:** **NÃO CONCLUÍDO**, não por falha comprovada da CI atual, mas por provas `NOT RUN`/`BLOCKED` no teste geral. A distinção entre resultado físico já executado e teste pendente está preservada. Este quadro **não substitui** o relatório histórico da #1354 nem altera o estado da [issue #1345](https://github.com/lucianox777/Jornada/issues/1345).

## 4. Plano mínimo de continuidade — somente Frente A

1. **Não interferir nas Actions em andamento**: não cancelar, reiniciar, abrir campanha 100k V9, modificar REF, TEST, Gold ou release. Registrar os resultados por SHA ao terminarem; sem fingir execução futura.
2. **Completar o inventário por provas existentes** dos itens e/f e a comparação histórica j somente com artefatos válidos; qualquer ausência de arquivo/hash/recorte mantém `NOT RUN/BLOCKED`.
3. **Itens b/c/g**: são testes do mandato e têm workflow manual já pronto; aguardar capacidade de disparo com evidência real, não criar novos workflows experimentais nem usar checks de outro SHA como PASS. Estas execuções não foram iniciadas por esta branch.
4. **V2**: manter o plano efetivo create-only, seu hash e recibo; não exigir versão estática adicional, não modificar `plan.json` ou a REF.
5. **Revisar riscos históricos** (#1302, #1253, #1257) contra evidências observadas e precisão dos limites; nenhuma resolução automática apenas por número de issues fechadas.
6. **Fronteiras de governança**: #1352 e #1356 são pesquisas em rascunho, fora da Frente A; #1351 consta como mergeado, mas a desconformidade temporal permanece registrada; #1353 DRAFT. Nenhuma PR de pesquisa deve ser mesclada para fechar #1345.
7. **Aprovação/merge futuros**: manter esta branch e respectiva PR DRAFT isoladas. Quando o PO autorizar, reconciliar primeiro a master vigente, revisar o HEAD e então integrar por gates, produzindo o relatório A–G final no **SHA realmente resultante**. Até lá, **nenhuma afirmação de conclusão**.

**Rastreabilidade GOV-DOC-01:** [mandato aprovado](Decisao_V9_Fechamento_Controlado_20261108.md), [matriz histórica](Relatorio_V9_Fechamento_Integracao_20261108.md), [triagem vigente](Triagem_Issues_Fechamento_V9_20261108.md), [PRODAM](Revisao_PRODAM_v5_00_20261108.md), [issue #1345](https://github.com/lucianox777/Jornada/issues/1345). Nenhum resultado científico da V9 foi certificado ou ativado.
