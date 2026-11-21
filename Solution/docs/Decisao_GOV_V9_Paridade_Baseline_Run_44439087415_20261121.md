# GOV-V9-PARIDADE-BASELINE-44439087415-20261121

**Autoridade:** PO, 21/11/2026. **Status:** APROVADA; paridade ainda não comprovada. **Política:** GOV-DOC-01.

## Decisão
O comportamento atual do calibrador e sistema deve corresponder ao comportamento efetivamente executado na campanha [#44439087415](https://github.com/lucianox777/Jornada/actions/runs/44439087415), SHA `317385ee3ee64cbb73add3bedd0149925f18b35f`. O PO confirmou expressamente essa execução, rejeitando as referências anteriormente sugeridas #42534643000 e #43105369099. A referência é **comportamental e observacional**, não certificação científica ou autorização de ativação.

## Evidências primárias verificadas
Jobs: ADMISSION success; PREPARE TRAIN 100k success; TRAIN físico 324 hipóteses V3 TF4 success; AUDIT independente success; E2E closeout failure; VALIDATION skipped; TEST skipped. **Não declarar workflow integral SUCCESS.**

Artefatos da execução: pré-auditoria ID **14490257319** (704352508 bytes); TRAIN ID **14490427116** (944330 bytes); auditoria ID **14490093743** (16482 bytes); preparação ID **14490101615** (83648 bytes); recibo REF ID **14490418052** (517 bytes). Conteúdo interno e métricas FP/TP/FN/FDR ainda não auditados nesta decisão.

## Justificativa, impacto e verificação
Fixar SHA e artefatos elimina ambiguidade de referência e permite detectar regressões. Comparar no mesmo corpus/seed/REF/configuração congelados: 324 hipóteses, fingerprints, parâmetros, seleção/finalistas, TP/FP/FN/FDR, scoring, Resolve, decisões, recibos e hashes. Diferenciar determinismo funcional de duração e recursos variáveis do runner. Documentar divergências e causas; **não afirmar paridade sem prova**. Não reproduzir a falha de closeout como requisito; preservar gates atuais.

## Precedência e rastreabilidade
Substitui somente a identificação incorreta da referência de comportamento; preserva LOCKED, segregação TRAIN/VALIDATION/TEST, gates fail-closed, GOV-DOC-01 e restrições de publicação/ativação. Referências: run #44439087415, SHA acima, [artefatos](https://github.com/lucianox777/Jornada/actions/runs/44439087415/artifacts). Próximo passo: ler artefatos e comparar SHA histórico com HEAD, registrando relatório de paridade.
