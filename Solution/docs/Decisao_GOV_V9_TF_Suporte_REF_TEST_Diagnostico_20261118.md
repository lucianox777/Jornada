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

## Retificação do Product Owner — 18/11/2026

O PO revogou a autorização para **rejeitar isoladamente hipóteses por ausência de suporte U** até esclarecimento do significado estatístico. A implementação correspondente foi revertida na PR #1723 (commit 6034e437). **Precedência:** esta retificação substitui somente o item 1 acima; as decisões REF e parametrização TEST permanecem vigentes. O TRAIN volta a falhar fechado em suporte U insuficiente. Não executar nem certificar campanha presumindo que a hipótese é simplesmente descartável.

**Verificação do código:** `V9TrainSqlNominalUCalibration.Frozen.HasSufficientSupport` exige simultaneamente `UnmatchedCandidatePairs >= 2` e `MatchingNominalPairs >= 2`. A implementação conta **pares candidatos não correspondentes no TRAIN**, não pessoas/observações únicas. Portanto a falha pode decorrer de zero ou um acordo nominal U entre pares não correspondentes, ou de menos de dois pares não correspondentes totais; não prova, por si só, que exista exatamente uma observação no corpus. Exige-se diagnóstico dos contadores por lado/unidade antes de definir nova política.

## Decisão posterior do Product Owner — 18/11/2026: cálculo nominal em populações pequenas e nomes inéditos

**Precedência:** substitui a política de rejeição por suporte mínimo U, inclusive o bloqueio por menos de dois pares ou menos de duas concordâncias; substitui também a exigência de presença do termo na referência IBGE como condição para prosseguir com TF. Revoga a proposta de descartar hipóteses individualmente por falta de suporte. Preserva as demais decisões REF e TEST deste documento.

**Regra funcional aprovada:** o sistema deve prosseguir normalmente com nomes inéditos, inclusive ausentes simultaneamente de TRAIN e IBGE, para pessoa e mãe, prenome e sobrenome. Não condicionar execução à quantidade de pares não correspondentes, acordos nominais, registros da população ou presença do nome em qualquer das duas fontes. A escassez de dados pode produzir resultados estatisticamente pouco representativos; isso é efeito do volume da amostra e não motivo para rejeição, penalização, limitação artificial da força TF ou interrupção. Demais bloqueios continuam responsáveis por seus próprios critérios. Situações de divisão por zero devem gerar aviso observável, sem abortar a campanha **por esse motivo**; não produzir infinito/NaN nem inventar valor estatístico como se fosse calculado. Preservar integridade, segurança, proveniência e paridade TRAIN/VALIDATION/TEST/produção.

**Base matemática:** U_ref=(A_U+α)/(N_U+2α), α>0, é definido para A_U=N_U=0 (U_ref=0,5); o limiar atual de dois pares e dois acordos é uma restrição de implementação, não exigência algorítmica. O TF atual também depende de frequência nominal IBGE; para termo ausente não há frequência publicada. A escolha de como obter a frequência desses termos e de como representar cálculo matematicamente indefinido **não foi especificada pelo PO** e não pode ser inventada silenciosamente. Requer solução única, versionada e auditável para todos os termos, sem bloquear a operação, a ser implementada e validada. Nenhuma certificação científica ou merge está implícita nesta decisão.

**Implementação:** pendente de alteração consistente nos contratos de fit, referência, auditoria de cobertura, scorer portável e testes. Rastreabilidade: PR #1723, discussão de 18/11/2026, decisão 19 e política GOV-DOC-01.
