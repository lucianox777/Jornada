# GOV-V9 — Correção técnica do binding TF4 para população configurada (18/11/2026)

## Contexto e evidência
Execução manual GitHub Actions #44410130858, SHA 01a8c3908e3b5170ebfd286991d95018280ca039, população sintética 1000, preflight aprovado. ADMISSION e PREPARE passaram. TRAIN falhou no passo 'Build immutable technical and TF4 plans in TRAIN from verified raw corpus' com V9_V3_TF4_PLAN_FAIL_CLOSED. O primeiro plano técnico comprovou no receipt `people: 1000`, mas `Solution/scripts/v9-v3-four-name-100k-plan.py` ainda exigia `source_receipt.people == 100000`. AUDIT, VALIDATION e TEST não executaram; closeout falhou por consequência.

## Decisão operacional e precedência
Aplicar correção técnica de propagação da população explicitamente configurada, sem flexibilizar os controles de proveniência, hash, seed, políticas científicas, orçamento ou a independência de TEST. O valor padrão 100000 continua preservado para execuções históricas. A campanha de 1000 é diagnóstico técnico, não certificação científica. Esta correção complementa a correção de binding TRAIN da PR #1724 e não revoga critérios científicos anteriores nem substitui a deliberação sobre 10k na PR #1723.

## Implementação e impactos
O construtor TF4 recebe `--expected-people` (padrão 100000), exige inteiro positivo e verifica igualdade com o campo autenticado `people` do receipt técnico. O workflow encaminha `V9_SYNTHETIC_POPULATION` ao segundo construtor. Divergências permanecem fail-closed. Reexecutar CI antes do merge e usar novo SHA de master; somente execução física posterior pode comprovar o fluxo completo.

## Rastreabilidade
- Run: https://github.com/lucianox777/Jornada/actions/runs/44410130858
- PR de correção: branch `fix/v9-tf4-population-binding-1000-20261118`.
- Política: GOV-DOC-01.
