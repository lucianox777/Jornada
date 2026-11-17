# GOV-V9-SQL-INDICE-ANTES-BULK-ROLLBACK-20261116

**Autoridade:** Product Owner, 16/11/2026. **Política:** GOV-DOC-01.

## Contexto e decisão

O PO observou aumento do tempo até o início das hipóteses, de aproximadamente 5–6 minutos para 10–12 minutos, na campanha V9 100k. A transferência PREPARE→TRAIN exibiu download e verificação rápidos, mas não há benchmark controlado que atribua causalidade à posição do índice. O PO determinou expressamente restaurar a criação de `IX_v9_cal_sig` antes do `SqlBulkCopy` e executar nova campanha comparativa.

## Implementação e precedência

Reverter **somente** a ordem de criação do índice de assinaturas em `V9PhysicalSqlAdaptiveCalibrationProbe.cs`: criar `IX_v9_cal_sig(part,pass_id,assinatura,obs_id)` junto à tabela temporária `#v9_cal_sig`, antes de `BulkSignaturesAsync`; retirar a construção pós-carga. Esta decisão **substitui operacionalmente** GOV-V9-SQL-INDICE-ASSINATURAS-APOS-BULK-20261116, sem invalidar sua justificativa histórica. Manter SqlBulkCopy BatchSize=25_000, JOINs, 324 hipóteses, 20 replays elegíveis, P95 físico, isolamento, PREPARE→TRAIN, AUDIT/VALIDATION e artefatos 30 dias.

## Validação, impactos e rastreabilidade

A alteração não prova melhora de desempenho nem altera a campanha já em execução, fixada por SHA. Antes de nova campanha, integrar PR após checks obrigatórios. Nova execução científica 100k requer despacho manual pelo proprietário, SHA exato da master e aprovação de preflight de segurança. Comparar tempos de início do workflow, PREPARE, início do TRAIN, entrada no step C#, primeira hipótese e throughput, com mesmo corpus, seed, plano e ambiente; registrar divergências e resultados. **Não iniciar automaticamente nova execução onerosa.**

Referências: commit original 9520bb66c8d47096dfe5dd19ec43338d352610ba; campanhas 43908715448 e 43984278752.
