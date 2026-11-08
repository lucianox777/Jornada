> **RETIFICAÇÃO DE GOVERNANÇA AD02 (08/11/2026):** este arquivo foi preparado antecipadamente e preservado como **INVENTÁRIO HISTÓRICO DE EVIDÊNCIAS, NÃO COMO EXECUÇÃO OU INÍCIO DO PASSO 9**. O PO corrigiu expressamente a sequência: concluir TODOS os passos 0–7, com os merges na master, antes de saltar o passo 8 dispensado e iniciar o teste geral a–m. O conteúdo abaixo permanece datado no SHA antigo para ajudar na análise posterior, **não representa gates executados do teste final**. [Decisão AD02](Decisao_V9_Mandato_Integracao_Final_Teste_Geral_20261108.md#gov-v9-final-20261108-b-ad02--correção-expressa-da-sequência-de-execução-pelo-po).

# GOV-V9-FINAL-20261108-B-AD01 — pré-auditoria do passo 9 (teste geral a–m)

**Registro:** 08/11/2026, 18h09 BRT. **Autoridade de escopo:** PO, decisão GOV-V9-FINAL-20261108-B-AD01. **Política:** GOV-DOC-01. **Estado:** PREFLIGHT PARCIAL / NÃO É TESTE GERAL FINAL. Não autoriza merge, release, ativação ou inferência científica.

## Ordem operacional vigente após a decisão do PO

O **passo 8** (triagem/resolução de issues) foi **DISPENSADO** e não será executado; as issues permanecem sem julgamento produzido por este mandato. Seguir diretamente para o **passo 9** do mandato — teste geral a–m. **Não confundir salto de etapa com dispensa das próprias pré-condições do passo 9**: a evidência definitiva só pode ser gerada na **master final, com zero PRs abertas**, depois dos merges serializados J → K → L, CI e 30k executados nos SHAs pós-merge. No estado desta pré-auditoria há **três PRs abertas** (#1337, #1318 e #1333); por isso nenhuma linha abaixo é aceite final.

**SHA master observado:** `db75f3a3f1a3ad38ec6d4f8f1ae3b2e254694b54` (merge #1332). **Campanha U:** [run 42519095371](https://github.com/lucianox777/Jornada/actions/runs/42519095371) 2/2 roles SUCCESS. **CI master:** [run 42512945589](https://github.com/lucianox777/Jornada/actions/runs/42512945589) SUCCESS. **30k física master:** [run 42512945477](https://github.com/lucianox777/Jornada/actions/runs/42512945477) SUCCESS. Todos representam a master **anterior** às alterações ainda nas PRs.

## Matriz a–m — verificação prévia de cobertura, NÃO de aceitação

Esta matriz reparte os grupos de requisitos citados no item 9 original em treze itens rastreáveis; não cria exigência nova. **PASS PRELIMINAR** significa evidência no SHA informado mas ainda não na master final. **NÃO COBERTO** significa ausência da prova necessária no SHA final; não é falha presumida nem autorização para omiti-la.

| Item | Superfície do passo 9 | Evidência localizada / limitação | Estado antes do merge final |
| --- | --- | --- | --- |
| **a** | CI completa da master | `jornada-ci` **42512945589 SUCCESS** no `db75f3a3`, com unidades, integração SQL, e2e, security-analysis e CodeQL. | **PASS PRELIMINAR**; repetir na master final. |
| **b** | Instalador Windows/bundle | `windows-production-bundle` na PR #1333 estava verde no HEAD anterior; não foi identificada execução de instalador pós-merge J/K/L na master final. | **NÃO COBERTO** no SHA final. |
| **c** | Cluster/topologia | `cluster-smoke` da PR #1333 e sua sequência de testes passaram; não prova a topologia resultante da master integrada. | **NÃO COBERTO** no SHA final. |
| **d** | E2E | `e2e`, `e2e-console` e `e2e-private-sql` SUCCESS na CI da master `42512945589`. | **PASS PRELIMINAR**; repetir. |
| **e** | Ingestão, ledger, API e Policy | CI da master cobriu e2e e contratos; a verificação de cobertura separada para **cada** superfície e cenário de Policy ainda não foi concluída. | **PARCIAL / NÃO COBERTO** integralmente. |
| **f** | Migração SQL e inventário | `integration-sql` e `ddl-upgrade` SUCCESS no run `42512945589`. Migração territorial 3.70→3.71 da G/#1334 já integrada e ensaio SQL real no **HEAD final** da G `b5114c45`, [run 42506908666](https://github.com/lucianox777/Jornada/actions/runs/42506908666), SUCCESS; versão candidata ainda 3.70 em CANDIDATE_INFO.json. Inventário completo pós-merge NÃO executado. | **PARCIAL / NÃO COBERTO** no SHA final. |
| **g** | Validação independente | Check `independent-validation` da PR #1337 SUCCESS no HEAD `c3db6f43` (não equivale à master final). | **NÃO COBERTO** no SHA final. |
| **h** | 30k físico completo | `v9-physical-seed42-probe` [42512945477](https://github.com/lucianox777/Jornada/actions/runs/42512945477) SUCCESS no SHA `db75f3a3`. Ocorreu antes dos merges pendentes. | **PASS PRELIMINAR**; exigir 30k após cada merge material e na master final. |
| **i** | Campanhas técnicas predeclaradas | U-raro [42519095371](https://github.com/lucianox777/Jornada/actions/runs/42519095371) **PRIMARY + PREDECLARED_SENSITIVITY SUCCESS**, 48 ensaios físicos sintéticos; V3 amplo `42512945445` SUCCESS. TRAIN apenas: nenhuma certificação FDR/Gold/TEST. | **PASS PRELIMINAR** para campanhas executadas; novos planos J ainda não provados. |
| **j** | Comparação histórica `f1bd50fd` × master final | A PR J contém comparador **somente analítico** com testes offline; a comparação real com artefatos da **master final** ainda NÃO foi executada. Não inventar `IDÊNTICO` ou `DIFERENTE`. | **NÃO COBERTO**. |
| **k** | Compatibilidade e testemunha V2 | PR #1337: `j-csharp-contract` [42522235028](https://github.com/lucianox777/Jornada/actions/runs/42522235028) SUCCESS (21 testes NUnit e 8 Python), plano oficial prevê V2 WITNESS+ANCHOR e CP8; **falta prova física C#/SQL do plano J no recorte real e regressão #1300 na master final**. | **PARCIAL / NÃO COBERTO** fisicamente. |
| **l** | Probe push-only, licença e segurança | No SHA `db75f3a3`, `.github/workflows/v9-physical-seed42-probe.yml` tem gatilho `on: push / branches: master` e **não** `workflow_dispatch`; `LICENSE` declara copyright exclusivo Luciano Vianna, ORCID 0009-0000-5591-3577; segurança da master `42512945589` SUCCESS. | **PASS PRELIMINAR**; revalidar na master final. |
| **m** | Neutralização comprovada, zero PRs, síntese do teste | #1333 tinha **12/12 checks SUCCESS** no HEAD `547868db`, mas os binários no próprio Git da PR ainda tinham **51 ocorrências em oito PDFs**; 30 referências SQL históricas precisam justificativa/exceções por hash; o pacote de 2 PDFs é apenas preview read-only; **3 PRs abertas**. | **NÃO COBERTO / PRÉ-CONDIÇÃO DE ACEITE NÃO ATENDIDA**. |

## Próximos atos permitidos, sem o passo 8

Retomar a fila técnica bloqueada de J → K → L, preservar decisões aprovadas nos documentos/índice, completar a prova física e comparativa da J, resolver ou declarar de modo correto os bloqueios documentais/binários, integrar serialmente PRs apenas com checks verdes e master CI + 30k de cada SHA. **Não realizar nenhuma triagem, classificação ou encerramento de issues** para completar o mandato. Assim que **zero PRs** e master **final** estiverem comprovados, realizar o teste geral a–m **de novo**, registrar SHA, runs, resultados e lacunas linha a linha, sem importar indevidamente os “PASS PRELIMINAR” desta matriz.

**Regra de falha:** se qualquer item necessário à aceitação não tiver prova, marcar **NÃO COBERTO / BLOQUEADO**; se teste falhar, abrir PR mínima, mesclar sob gates e **repetir todo o teste** na master final, conforme GOV-V9-FINAL-20261108-B. A dispensa do passo 8 não elimina correção de defeitos revelados pelos testes do passo 9.

**Precedência:** decisão PO GOV-V9-FINAL-20261108-B-AD01 documentada no [mandato original com adenda](Decisao_V9_Mandato_Integracao_Final_Teste_Geral_20261108.md#gov-v9-final-20261108-b-ad01--adenda-superveniente-aprovada-pelo-po-em-08112026). **Índice:** [Indice_Decisoes_Vigentes.md](Indice_Decisoes_Vigentes.md). **Painel único:** [PR #1318 comentário 6725665397](https://github.com/lucianox777/Jornada/pull/1318#issuecomment-6725665397). Esta matriz está na **branch documental #1318**, até o respectivo merge.
