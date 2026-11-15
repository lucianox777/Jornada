# V9 TF4/324 — investigação de desempenho e hipótese de rollback (15/11/2026)

**Natureza:** registro técnico de incidente e comparação retrospectiva, **não uma nova decisão do Product Owner** nem autorização para rollback amplo. Relacionado às decisões vigentes GOV-V9-TF4-JOBS-INDEPENDENTES-TIMEOUT-NATIVO-20261114, GOV-V9-ETA-TAXA-MEDIA-GLOBAL-CONCLUSOES-20261114 e GOV-DOC-01. **Estado:** logs reais examinados, mecanismo ainda sem causalidade provada; nenhuma nova campanha 100K nem TEST foi iniciada por esta investigação. **Ferramenta e relatório integrados:** [PR #1542](https://github.com/lucianox777/Jornada/pull/1542), squash `4990677331aabdc7b63806d22aeb2e23498cbe20`, CI rápida [#43674204732](https://github.com/lucianox777/Jornada/actions/runs/43674204732) **3/3 SUCCESS** e teste focused [#43674204736](https://github.com/lucianox777/Jornada/actions/runs/43674204736) **SUCCESS** (somente testes offline; sem prova causal ou nova execução 100K).

## Evidências de origem, SHA e resultado

| | Baseline físico concluído | Campanha física interrompida |
|---|---|---|
| GitHub run | [#43551194008](https://github.com/lucianox777/Jornada/actions/runs/43551194008) | [#43631379628](https://github.com/lucianox777/Jornada/actions/runs/43631379628) |
| Commit exato | `8ce35420fbb015fb281d8edcbc99213252aac183` | `814238795abccf5c8c64f61a83c871bd0374cc7b` |
| Job TRAIN | SUCCESS, antigo TRAIN + AUDIT | CANCELLED, novo `physical-100k` |
| Progresso físico | **324/324** | **308/324** (último progresso às 01:17:22 UTC de 15/11) |
| AUDIT | PASS, 3 finalistas no job antigo | SKIPPED (dependência de TRAIN) |
| VALIDATION | SKIPPED por gate anterior | SKIPPED (TRAIN não completo) |
| TEST | SKIPPED | SKIPPED |
| Artefato de pré-auditoria | Handoff antigo de finalistas gerado | **NÃO** gerado; somente artefato diagnóstico TRAIN pequeno |
| Encerramento | SUCCESS do workflow antigo, mas não E2E científico completo | CANCELLED do TRAIN e FAILURE do `e2e-closeout` |

O log do job recente termina com `##[error]The operation was canceled.`, aproximadamente seis horas após o início do workflow. Isso é **compatível** com o limite nativo do runner; o log sozinho não demonstra se houve uma solicitação humana de cancelamento nem uma causa raiz interna. Não chamar esta ocorrência de bug do algoritmo sem prova.

## Comparação direta — mesma campanha técnica, registros de progresso reais

Em ambas, TRAIN 100K seed 42, TF4/81, 324 hipóteses, `MaximumParallelRuleSets=0` (`AUTO_0`), `V9_100K_DISABLE_PYTHON_SQL_BUDGET=true` e `V9_REPLAY_TIMING_DIAGNOSTICS=0`. Para evitar comparar o relógio de startup do workflow, os tempos físicos usam o marco do step CSharp `V9_PHYSICAL_STEP_START_UTC`.

| Marco | Baseline desde início físico | Campanha recente desde início físico | Diferença relativa |
|---|---:|---:|---:|
| 64/324 | ~82,3 min | ~103,5 min | ~+26% |
| 278/324 | ~263,5 min | ~321,9 min | ~+22% |
| 308/324 | ~290,1 min | ~354,6 min | ~+22% |
| 324/324 | ~306,7 min | não alcançado | — |

Foi confirmada a presença de **308/308 IDs SHA curtos de hipóteses coincidentes** entre o conjunto concluído pela execução recente e o conjunto da anterior. Os **308 resultados `AVALIADA`/`REJEITADA` coincidem** para esses IDs; só **79 de 308** chegaram na mesma posição de conclusão, o que é compatível com concorrência e conclusão assíncrona. As 16 hipóteses restantes da execução anterior não têm correspondente concluído na recente. *Isto não certifica igualdade de scores, métricas, snapshots, SQL execution plans ou resultados que não foram produzidos.*

As mensagens `[V9 SQL GROUP]` mostram os seguintes tempos de `RunAsync`, na ordem dos logs (mesma assinatura de contagem de requests/options/margins, mas **sem identidade do frame congelada no log**):

| SQL group | Baseline | Recente | Diferença |
|---|---:|---:|---:|
| 64 requests / 32 opções / 2 margens | 160,9 s | 199,7 s | +24,1% |
| 4 requests / 2 opções / 2 margens | 20,6 s | 27,6 s | +34,0% |
| 128 requests / 64 opções / 2 margens (1º no log) | 371,6 s | 478,3 s | +28,7% |
| 128 requests / 64 opções / 2 margens (2º no log) | 481,8 s | 610,3 s | +26,7% |

**Limitação:** o tempo de `RunAsync` é do trecho SQL compartilhado do grupo, **não** das avaliações de replay físico congelado nem do job total. Como grupos podem se sobrepor e os dois grupos de 128 não trazem SHA de frame no log, não somar os tempos de SQL nem afirmar identidade de grupo pela posição. A lentidão registrada nos quatro grupos e nos marcos de progresso torna menos provável uma anomalia restrita apenas ao contador ETA.

## Controle de diferenças de código

O diff `8ce35420...81423879` contém **28 commits** e mudanças de workflow/TEST e documentação. Na etapa TRAIN: `V9NestedCalibrationCoordinator` perdeu o prazo local artificial e alterou **somente a mensagem/semântica do forecast**; o núcleo `V9PhysicalSqlAdaptiveCalibrationProbe` recebeu movimentação de um record de observação, sem alteração da rotina de probing. O replay compartilhado foi movido para `Jornada.Linkage.Core` com mudanças mínimas de contrato e API, sem prova física isolada de custo equivalente. O fluxo científico TRAIN TF4/81 não foi deliberadamente modificado. Nenhuma evidência confirma que **reverter os últimos 28 commits** recuperaria a performance, e isso também retiraria correções de encadeamento e integração TEST/VALIDATION. Também não está provado que o GitHub-hosted runner foi a causa: faltam dados de CPU efetivamente disponível, pressões de memória/IO, planos SQL e duração separada de replay.

## Próximos testes propostos, sem consumir nova campanha 100K

1. **Não executar rollback global como diagnóstico.** Conservar SHA e comparações e priorizar identificação de regressão/variação do runner antes de mudar a `master`. Rollback seletivo fica condicionado a evidência verificável, preservando decisões do PO, cadeia AUDIT→VALIDATION e TEST fail-closed.
2. **Ferramenta determinística offline:** `Solution/scripts/v9-tf4-compare-train-job-logs.py` recebe dois *logs locais* e emite um JSON sanitizado, com validação da sequência 324, associação de SHA/resultados, marcos e tempos SQL sem inferir causalidade. Testes adversariais de corrupção, lacunas e timestamps rodam na CI; nenhuma requisição ao GitHub ou dado científico é processado pelo script.
3. Se outra execução 100K for posteriormente autorizada, coletar em log **CPU disponível real, pressão de memória/IO, paralelismo admitido, número de conexões, fase SQL vs replay** e usar a opção já existente `replay_timing_diagnostics` para observar replay (sem alterar a seleção); comparar no mesmo cenário de runner. Não aumentar limites, transformar TEST em diagnóstico, liberar `test_required=true` ou repetir a coorte TEST one-use.
4. Considerar **checkpoint científico com provas de integridade** ou **partição em jobs completos** somente mediante novo desenho, validação de equivalência e decisão específica do PO; não dividir os 324 resultados de forma improvisada nem marcar um fragmento como TRAIN concluído.

## Precedência

Este relatório **não altera** GOV-V9-TF4-JOBS-INDEPENDENTES-TIMEOUT-NATIVO, GOV-V9-CADEIA-OBRIGATORIA-E2E-PREFLIGHT, GOV-V9-VALIDATION-ARQUIVO-OPERACIONAL-FINAL-TEST-NOME, nem GOV-DOC-01. Serve como evidência corrente sobre um incidente real posterior aos recibos de integração. `TRAIN 308/324` **não** habilita AUDIT, VALIDATION, TEST ou certificação. Necessário novo TRAIN 324/324 completo para a cadeia obrigatória.

## Continuação de instrumentação operacional — 15/11/2026 (proposta em PR, ainda sem medição 100K)

A comparação retrospectiva provou **vazão e grupos SQL mais lentos**, mas não separou a duração agregada pós-SQL (replay + indexação + log), porque os logs #43551194008/#43631379628 não tinham relógio de fechamento de cada grupo nem identificador de grupo estável. Comparar o primeiro e segundo grupo de 128 requests **pela ordem de chegada dos logs** não prova que eram o mesmo frame; não concluir causalidade só por esse proxy.

**Implementação instrumental para a próxima campanha explicitamente autorizada:** no coordenador físico, preservar a medição `[V9 SQL GROUP]` e acrescentar `group_sha` (prefixo SHA da primeira hipótese congelada do grupo). Após avaliar as mesmas hipóteses e emitir as mesmas mensagens de progresso, acrescentar **uma única linha por grupo**, `[V9 PERF GROUP]`, com `group_sha`, `requests`, `replay_calls`, `sql_seconds`, `post_sql_seconds` e `total_seconds`. O segundo intervalo mede exclusivamente o tempo de parede **após** a fase SQL, incluindo preparação, replays, callbacks, console e outras operações, **não** a soma de P95 científico nem CPU isolada; grupos SQL ainda podem se sobrepor em paralelo. Reutiliza o cronômetro já existente e **não cria segunda linha por hipótese**, não altera o algoritmo/TP/FP/FDR/TF/P95.

No passo de verificação de AUTO_0 já existente no workflow manual, imprimir `V9_TRAIN_RUNNER_CAPACITY` com `os_cpu_count`, CPUs acessíveis pela afinidade Linux e limites cgroup CPU/memória, sem path de usuário, conexão SQL, segredos, dados de pessoa ou nova etapa PREPARE. Instrumentação **somente observacional** e não bloqueante: não modifica admission de RAM/disco/SQL, escalonamento, recursos, runner, `timeout-minutes`, `test_required` ou autorização manual.

O comparador offline `v9-tf4-compare-train-job-logs.py` aceita logs históricos sem SHA (comparação *ordinal_unverified*) e futuros com SHA em ambos os lados (pareamento pelo hash mesmo que os grupos completem fora de ordem), e verifica consistência de tempos SQL/pós-SQL e número de replays. Cada relatório continua marcado `causalityCertified=false`. Os testes de CI cobrem SHA trocado, phase timings inconsistentes e invariância da única chamada científica por hipótese. Com o HEAD apenas, **não existe nova medição** de ganho ou mitigação de timeout; o próximo TEST científico ainda não está liberado.

**Autoridade/precedência:** prosseguimento da investigação operacional solicitada pelo PO, dentro da decisão já vigente sobre diagnóstico de timeout e sem aprovação de rollback amplo. O uso de flags de replay continua **opcional e falso por padrão**, conforme GOV-V9-LOG-UNICO-PROGRESSO-REPLAY-20261113. As decisões GOV-V9-TF4-JOBS-INDEPENDENTES-TIMEOUT-NATIVO, GOV-V9-TIMESTAMPS-JOB-360, cadeia TRAIN→AUDIT→VALIDATION e TEST one-use prevalecem. GOV-DOC-01.

## Recibo de integração da instrumentação — 15/11/2026

A [PR #1544](https://github.com/lucianox777/Jornada/pull/1544) foi integrada à `master` por squash no SHA **`8c71678d65d87550d1077cc7bacfd178e0dcd9ce`**. O HEAD exato `358b384b3451838746d5c0402f9960bdedea12ae` passou:

- [CI rápida #43690333560](https://github.com/lucianox777/Jornada/actions/runs/43690333560): `security-guards`, `unit-runtime` e `deterministic-build` **3/3 SUCCESS**.
- [Focused #43690333573](https://github.com/lucianox777/Jornada/actions/runs/43690333573): **SUCCESS**.
- [Integração cross-job sintética #43690333554](https://github.com/lucianox777/Jornada/actions/runs/43690333554): TRAIN, AUDIT, VALIDATION e closeout **SUCCESS**, TEST opcional **SKIPPED** (não são 100K reais).
- [Contrato offline V2/V3 #43690333615](https://github.com/lucianox777/Jornada/actions/runs/43690333615): **SUCCESS**.

**Significado restrito:** a instrumentação e os novos testes offline estão integrados. Não houve, nesta PR, nova execução física de 100K, nova evidência de throughput, causalidade da lentidão nem prova de que um rollback recuperaria a performance. A próxima campanha permanece manual e gated; TEST científico e produção continuam bloqueados conforme #1488/#1478. GOV-DOC-01.

## Correção de observabilidade autorizada em 15/11/2026

O PO determinou “Corrija” após o encerramento da execução #43631379628. A lacuna concreta identificada é a ausência de checkpoints de recursos após o cancelamento: o wrapper só persistia o recibo ao terminar. Uma mudança **sem alterar ciência/SQL/paralelismo**, documentada em [GOV-V9-OBSERVABILIDADE-TIMEOUT-PARCIAL-20261115](Decisao_GOV_V9_Observabilidade_Timeout_Parcial_20261115.md), acrescenta checkpoint atômico aproximadamente por minuto de CPU/IO/pressão/RAM, com indisponibilidade representada por null e publicação apenas como diagnóstico TRAIN mesmo após timeout. O replay por hipótese permanece opcional (OFF por padrão). **Não é correção comprovada de desempenho**; requer nova evidência física e autorização separada para repetir 100K. GOV-DOC-01.
