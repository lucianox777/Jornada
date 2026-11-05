# V9 — coleta de evidência de 30k por PR automática (05/11/2026)

Atualização de acompanhamento do orquestrador #1282 à política de CI integrada pela PR #1285 (merge `a5ccb0dbf6b57e9d33f48c2368b5d3db909a83d9`).

O coletor continua estritamente **read-only**; não despacha workflows, não inicia SQL, não lê TEST e não altera modelos. Para `v9-physical-seed42-probe.yml`, além de `workflow_dispatch`, ele passa a consultar `pull_request`. Uma campanha automática só conta como evidência quando sua execução:
- é **completed / success**, corresponde à branch do próprio repositório e ao SHA exato da frente;
- contém o job `V9 full physical 30k SQL TRAIN (when impacted)` **completed/success**;
- contém o required check `SQL físico 30k clean — FS blind Rank-Resolve TF0` **completed/success**;
- não é preflight, skip, run antigo, run cancelado, outra branch, outro SHA nem workflow genérico.

A fonte `workflow_dispatch` e os outros workflows independentes mantêm as exigências originais. Relatório continua separando `EXECUTED`, `TECHNICALLY_APPROVED`, `STATISTICALLY_ELIGIBLE`, `CERTIFIED` e `OPERATIONALLY_AUTHORIZED`; a presença de 30k **não certifica** o modelo. Usar a avaliação física automatizada é permissão de execução, não autorização DT-15 de publicação.

A mudança evita falso negativo do relatório de coordenação, sem copiar nem reclassificar evidências e sem alterar a política de impacto do probe.
