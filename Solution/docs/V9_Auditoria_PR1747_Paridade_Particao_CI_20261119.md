# Auditoria técnica — PR #1747: paridade de partição e CI (19/11/2026)

**Natureza:** registro técnico de implementação e validação, sem nova decisão científica ou autorização de produção. **Estado:** correção implementada na PR; validação do HEAD atualizado pendente.

## Contexto e precedência
A PR #1712 integrou o replay diagnóstico do grafo SQL operacional. A PR #1747 corrige a comparação de `IdentityByObservation`: GUIDs distintos podem rotular a mesma partição de observações, portanto a equivalência é por membros de cada grupo, não por igualdade literal dos GUIDs. O conjunto de chaves e a cardinalidade continuam conferidos; `reference.Decisions.SequenceEqual(operational.Decisions)` permanece estrito, inclusive nos campos de candidato e modelo. Não altera decisões vigentes de TEST, LOCK, TRAIN, ativação ou RunAsync produtivo.

## Rastreabilidade de CI e correção
- Branch: `feat/v9-parity-partition-canonical-20261119`; PR: https://github.com/lucianox777/Jornada/pull/1747
- SHA anterior à correção do teste: `a6f5c789a57ed1843c804dc352eb052a849d87f6`.
- No SHA anterior: preflight Release SUCCESS (run #44625567830); RunAsync físico não publicador SUCCESS (#44625567917); SQL smoke e segundo RunAsync SUCCESS (#44625568087). No workflow obrigatório #44625568054, `classify-impact`, `deterministic-build` e `security-guards` SUCCESS; `unit-runtime` FAILURE.
- Erro primário: teste estrutural `test_v9_test_operational_batch_rehearsal.py::test_production_sql_retrieval_is_batched_but_resolve_is_global` exigia texto antigo `reference.IdentityByObservation.Any(`, removido intencionalmente na implementação da paridade canônica. Log: https://github.com/lucianox777/Jornada/actions/runs/44625568054
- Correção no commit `cfb31f5017ce4b66c5561c96b21579c0aad59469`: teste passa a exigir as duas chamadas `CanonicalGroups(...IdentityByObservation)` e rejeitar a expressão antiga. Não altera o runtime científico.
- **Pendente:** executar e conferir CI obrigatória no SHA atualizado, sem transportar aprovações do SHA anterior. Merge não autorizado por este registro.

## Impactos e limites
A intervenção corrige uma expectativa estrutural desatualizada, não relaxa os critérios de paridade e não prova equivalência completa com o `ProbabilisticLinkageBatchRunner.RunAsync` produtivo. Certificação, TEST científico e publicação continuam bloqueados pelos controles vigentes. GOV-DOC-01 exige manter este registro e o índice sincronizados.
