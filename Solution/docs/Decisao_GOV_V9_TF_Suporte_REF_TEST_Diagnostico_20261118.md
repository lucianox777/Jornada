# GOV-V9 — Tratamento de suporte TF, REF congelada e repetições TEST

**Autoridade:** Product Owner, 18/11/2026. **Status:** decisão aprovada, implementação em andamento na PR #1723; não declarar concluída antes da integração e execução de evidências. **Política:** GOV-DOC-01.

## Contexto e evidência
Run #44411153793, SHA 2bc76e84f581aa6b20f500637614999fc1e12f6b, população 1000: ADMISSION/PREPARE passaram e TRAIN físico falhou com `System.IO.InvalidDataException: TF ativa sem suporte TRAIN U: Mother/LastContentSurname`. AUDIT/VALIDATION/TEST foram pulados. A falha não demonstra defeito da fonte IBGE nem autoriza inventar suporte U.

## Decisões aprovadas
1. **TRAIN:** avaliar cada hipótese com falha estatística identificável isoladamente, sem abortar indevidamente as demais; preservar fail-closed para falhas estruturais e não mascarar ausência de suporte U. Exigir evidência de 324 resultados/estados auditáveis antes de liberar AUDIT.
2. **REF/IBGE:** reutilizar fonte canônica congelada e conferir ID/hash/versão; evitar downloads e recomputações redundantes. A decisão vigente [IBGE congelado sem regenerar 30k](Decisao_GOV_V9_100K_IBGE_Congelado_Sem_Regenerar_30K_20261113.md) já proíbe regenerar o bootstrap histórico. Reidratar a REF local no SQL efêmero de cada job é diferente de download ou recomputação; preservar SQL independente entre TRAIN, VALIDATION e TEST.
3. **TEST:** introduzir parâmetro de repetições, com 3 apenas para diagnóstico DEV explicitamente não científico, preservando 20 como padrão e mínimo do protocolo científico vigente. Não publicar ou certificar resultado diagnóstico. Manter vencedor SHA-pinado, corpus TEST independente, nenhuma refit ou seleção.

## Implementação, riscos e precedência
Primeira entrega na PR #1723: argumento CLI opcional `--test-repetitions N`, exposto como input `test_end_to_end_repetitions` no workflow manual, com default 20 e guarda explícita `JORNADA_V9_DIAGNOSTIC_NON_SCIENTIFIC=true` e `JORNADA_RUNTIME_MODE=DEV` para menos de 20. A propagação de 3 pelo workflow foi implementada; a revisão de contratos de latência e critérios científicos depende de testes de integração: não declarar a modalidade 3 operacional antes disso. As correções de tratamento por hipótese e auditoria da carga REF ainda precisam ser concluídas e testadas. Não substituir uma falha estatística por zero, suavização arbitrária ou resultado aprovado.

## Rastreabilidade
- Run: https://github.com/lucianox777/Jornada/actions/runs/44411153793
- PR: https://github.com/lucianox777/Jornada/pull/1723
- Precedência: GOV-DOC-01 e decisão IBGE congelado de 13/11/2026.
