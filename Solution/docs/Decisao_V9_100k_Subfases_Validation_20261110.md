# GOV-V9-100K-VALIDATION-SUBPHASES-20261110 — diagnóstico por fases da avaliação de VALIDATION

**Data:** 10/11/2026. **Mandato:** continuidade do calibrador até a próxima execução, sem consultas intermediárias do PO. **Origem:** [campanha #42889542560](https://github.com/lucianox777/Jornada/actions/runs/42889542560), SHA `78487d06a6bfcb49f0b8971345dd81fcdf06dd23`. **Precedência:** PRs #1406, #1407, #1408; issue #1349; GOV-DOC-01.

## Evidência
A campanha física 24×100k falhou em `PHYSICAL_SQL_BATCHES` por `InvalidDataException`, código seguro `UNCLASSIFIED_PHYSICAL_SQL_PROBE_AT_FS_VALIDATION_EVALUATE` registrado no `interrupted.json` do artefato #13798127032. Não houve primeira conclusão de lote nem certificação 24/24. A etapa identificada é o começo de `V9FellegiSunterAdaptiveCalibrationAdapter.EvaluateValidation`, **não é evidência de que TF, as 81 células ou o mecanismo SQL sejam causalmente incorretos**.

O monitor registrou 19 amostras, pico SQL `6494879 KiB`, cgroup SQL `6500560 KiB`, worker `4850780 KiB`, mínimo disponível no host `3544016 KiB`, 0 eventos OOM/OOM-kill observados, `sqlContainerOomKilled=false`. Isso não prova ausência universal de OOM, mas não justifica atribuí-lo como causa. A ausência de logs completos em GitHub Actions público é intencional e preserva sigilo.

## Decisão técnica instrumental
Acrescentar **callback opcional** de checkpoint ao adaptador físico de validação, chamado exclusivamente por seu caminho de calibração nested, mantendo callers existentes compatíveis. Seis subfases permitidas: `VALIDATION_FIT_CHECK`, `VALIDATION_ROWS`, `VALIDATION_TF_FROZEN_UNITS`, `VALIDATION_TF_PUBLISHED_COVERAGE`, `VALIDATION_PAIR_SCORING`, `VALIDATION_METRICS_AGGREGATE`. A classificação `UNCLASSIFIED_PHYSICAL_SQL_PROBE_AT_...` só aceita valores de whitelist compilada; entradas dinâmicas, mensagens, nome próprio, CPF, SQL, connection string e stack trace nunca saem do processo. Nenhuma identificação da opção/célula ou frequência no recibo público.

**Limitação:** o código informa o último marco iniciado antes da exceção, não a instrução exata. A correção causal continua pendente e dependerá do resultado; não alterar regras, coortes, TF, M/U, FDR, 24/16/8, SQL, recursos, TEST/Gold ou política de ativação como workaround.

## Testes, impactos e governança
Teste unitário cobre todas as subfases, rejeição de marcador arbitrário e não vazamento de canário. Integrar somente após gates no HEAD exato. Não iniciar execução 100k automaticamente; workflow permanece manual owner-only, exige preflight real e SHA congelado da master. Manter GOV-DOC-01 e índices sincronizados com PR e merge; documentar qualquer novo diagnóstico na #1349.
