# V9 — Validação E2E pós-merge e correção de regressões de CI (08/11/2026)

**Product Owner:** autorização de 08/11/2026 para **teste amplo end-to-end após integração da V9**, com resultados quantitativos e identificação de defeitos. **Governança:** GOV-DOC-01. **Escopo:** somente regressão técnica com corpus sintético, bancos SQL descartáveis e executáveis/cluster de DEV; nenhum dado real, TEST/Gold, DT-15, HML ou produção. **Estado inicial:** DIAGNÓSTICO COM FALHAS; **ainda NÃO APROVADO** como teste geral completo.

## Ponto de partida comprovado
A `master` contém o merge único #1343, SHA exato `c6451cf2fd92534f2edeb4b24fa9bde0d9327662`. Ao contrário da hipótese de Actions vazias, **os workflows após esse merge foram disparados e permaneciam ativos na primeira inspeção do PO**, incluindo:

- [jornada-ci 42532243521](https://github.com/lucianox777/Jornada/actions/runs/42532243521), **IN_PROGRESS** na leitura inicial, com jobs `e2e`, `e2e-console` e `e2e-private-sql` **SUCCESS**, mas `unit-runtime`, `unit-contracts` e agregador `unit` **FAILURE**. Os jobs SQL de integração, CodeQL e harness ainda estavam em progresso, de modo que **não se pode dizer CI verde**.
- [30k físico 42532243513](https://github.com/lucianox777/Jornada/actions/runs/42532243513): classificador de impacto SUCCESS; regressão física 30k em SQL **IN_PROGRESS** na observação inicial. Não cancelar ou disparar outro 30k por suposta inatividade.
- [Instalador Windows 42532243486](https://github.com/lucianox777/Jornada/actions/runs/42532243486) SUCCESS; [regressão M/U seed42 42532243472](https://github.com/lucianox777/Jornada/actions/runs/42532243472) SUCCESS. Outras Actions de cluster, validação independente e V3 no mesmo SHA são provas parciais a correlacionar, não um atestado geral.

## Causas observadas dos dois bloqueios de CI e correções sem relaxar guards

1. **`unit-runtime`/NUnit:** log do job `128888949252` em [42532243521](https://github.com/lucianox777/Jornada/actions/runs/42532243521) mostra **2 testes falhos em 2155 (2153 aprovados, 0 skipped)**: `Nested_request_hash_distinguishes_full_option_fingerprint_not_only_alpha_threshold` e `Three_estimators_share_exact_SQL_frame_but_keep_independent_model_evaluations` em `V9NestedCalibrationTests.cs`, `MissingMethodException`. São testes que instanciam o record privado `Request` por reflection passando **quatro argumentos**, enquanto a implementação J introduziu o **quinto parâmetro opcional** `DiagnosticOnly = false`. `Activator.CreateInstance` não preenche automaticamente esse default. **Correção pontual:** fornecer `false` explicitamente nos dois construtores reflexivos; **não alterar o record nem a semântica de deduplicação ou seleção**. As asserções de SHA distintas e SQL-frame idêntico são mantidas.
2. **`unit-contracts`/security:** log job `128888949273` apresenta `SOURCE SANITY GATE: FAIL` no documento `Decisao_V9_Mandato_Final_Unico_20261108.md`, por texto histórico de exemplo `SQLCMDPASSWORD=<valor>`. Não é credencial real: o próprio texto condena transporte de segredo via argumento. **Correção editorial segura:** representar a posição de valor como placeholder **`SQLCMDPASSWORD=<valor>`**, mantendo explícita a conduta proibida. **Nenhuma alteração em `source-sanity-gate.py`, na política allowlist, TLS, controles de segredo ou workflows**.
3. A falha do agregador `unit` é efeito lógico das duas falhas acima; não dispensar esse gate. Falhas de artefato consequentes não são tratadas como aceite.

## Plano do teste geral a–m na nova `master`

| Item | Prova requerida | Evidência inicial no SHA `c6451cf2` | Critério de resultado final |
| --- | --- | --- | --- |
| a | `jornada-ci` completa | run 42532243521 **NÃO VERDE** (unidades falhas) | SUCCESS real em todos os jobs obrigatórios, sem esconder SKIPPED |
| b | Instalador Windows e bundle | run 42532243486 SUCCESS | Confirmar SHA/arquivos instaláveis e provas do pacote |
| c | Cluster/topologia | run 42532243429 em progresso na leitura inicial | Prova funcional de topologia, disponibilidade e logs sem segredos |
| d | E2E API/Console/SQL | `e2e`, `e2e-console`, `e2e-private-sql` SUCCESS na CI | Evidência de casos, fixture, requests/replies e end-to-end real |
| e | Ingestão, ledger, API, Policy | Resultados E2E parciais; cobertura 1×1 ainda não demonstrada | Provas positivas e negativas individuais com contagens |
| f | SQL upgrade/inventário | `ddl-upgrade` SUCCESS, `integration-sql` em progresso | SQL efetivo/replay e inventário no SHA final |
| g | Validação independente | run 42532243461 em progresso | SUCCESS com contrato e limitações documentados |
| h | 30k SQL seed42 | run 42532243513 em progresso | SUCCESS do ensaio e agregador no SHA final |
| i | Ensaios técnicos V2/V3, TF/misturas | Regressão seed42 SUCCESS; demais estudos físicos em curso | Artifact com política, corpus, hashes, 24/16/8 e estatísticas |
| j | Comparação histórica | J pré-merge run 42529456510, PRIMARY/SENSITIVITY SUCCESS | Relatório analítico do mesmo recorte no SHA final; se divergente, `NÃO COMPARÁVEL` |
| k | V2 testemunha, CP apenas candidata e compatibilidade | J físico pré-merge 42529456510 SUCCESS | Paridade histórica e limites do contrato no SHA integrado |
| l | Segurança, copyright, probe push-only | `security-guards` SUCCESS; `unit-contracts` FAIL | Gate source-sanity, CodeQL e invariantes sem bypass |
| m | Documentação/PRs | `master` tinha 43 DOCX/PDF, exatamente duas exclusões aprovadas; issues #1344 aberta | Declarar **neutralização PARCIAL** (8 PDFs/51 ocorrências), 0 PRs abertas; #1344 fora do aceite editorial conforme PO |

**Classificação:** PASS/FAIL/NÃO COBERTO com evidência por run/job/artifact/SHA e valores TP/FP/FN, recall, FDR observado, CP95, pendências, fusões/fragmentações, latência e cobertura quando as fontes de teste efetivamente os fornecerem. **Não inventar indicadores** a partir de status verdes. Não trocar resultado TRAIN sintético por FDR científica certificada. O histórico [pré-auditoria a–m](Teste_Geral_V9_a_m_Preflight_20261108.md) não era execução do teste pós-merge.

## Execução e precedência operacional

O PO aprovou medir o funcionamento após o merge único. **CI completa e 30k ainda em andamento não devem ser duplicados/cancelados** para iniciar um teste só porque a interface parece ociosa. Registrar o estado da primeira execução em [issue #1345](https://github.com/lucianox777/Jornada/issues/1345), corrigir os dois erros reprodutíveis por PR protegida, concluir/inspecionar execuções antigas e então validar **a nova master no SHA resultante do merge corretivo**. A CI completa e o 30k após esse segundo push ocorrerão automaticamente; essa é a bateria física/E2E de engenharia necessária. Se cobertura funcional (e) ou comparação (j) seguir ausente, registrar NÃO COBERTO e desenvolver testes em PR separada, sem inventar sucesso.

**Precedência e riscos:** complementa `DC-V9-CI-LOTE-20261108` (PR fast; CI/30k no push da master) e `DC-V9-DOC-PDF-ADIAMENTO` (PDFs na issue #1344), sem mudar `CI-V9-30K-PUSH-ONLY`, P95, LOCK científico, D2 cancelado, defaults V2/V3 ou política de ativação. Atividade não concluída antes de runs e documentação no índice oficial.
