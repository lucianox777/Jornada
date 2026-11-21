# Incidente CI master — TEST SQL fixture (2026-11-21)

## Evidência

HEAD observado: `7161e61fc39508a860ee96898b87501d0a2d436b`. Workflow `jornada-ci` push run [44881128758](https://github.com/lucianox777/Jornada/actions/runs/44881128758) falhou em `unit-runtime` (job 136776688519). O log registra 2.270 testes aprovados e 1 teste ignorado: `Isolated_worker_consumes_exact_validation_file_and_reports_fixture_without_publication`. O próprio teste exige `JORNADA_V9_TEST_SQL_CONNECTION` e `JORNADA_V9_TEST_SQL_MARKER`, ausentes no runner genérico; `Assert.Ignore` contraria `--forbid-skipped`. O job agregado `unit` falha em consequência. O upload OpenAPI ausente é efeito da interrupção anterior.

## Correção mínima

Classificar o fixture que depende de SQL efêmero como `SQLSmoke`, não `Unit`, respeitando o filtro já existente do `unit-runtime` (`TestCategory!=Integration&TestCategory!=SQLSmoke`). Nenhuma alteração do gate `--forbid-skipped`, LOCKED, gerador, coorte ou decisões científicas. **Esta correção não prova execução do SQLSmoke**: a execução do fixture deve continuar exigida em ambiente SQL efêmero dedicado, sem confundir CI genérica com certificação científica.

## Gates para integração

Reexecutar checks obrigatórios na PR, verificar logs e evidência de não-skip, conferir LOCKED e DT-10, confirmar ruleset aplicável; squash apenas com todos os gates verdes. Não declarar master liberada antes de CI pós-merge verde. GOV-DOC-01: registro de incidente operacional, sem criar ou alterar decisão científica; índice de decisões vigentes permanece inalterado por ausência de nova decisão.
