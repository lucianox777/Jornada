# GOV-V9 — Compatibilidade estritamente fixada do snapshot V2 histórico (19/11/2026)

**Estado:** correção técnica proposta na PR #1450; pendente de CI e validação; não constitui aprovação de TRAIN, TEST, ativação ou promoção.

## Contexto e evidências

No job A2 da execução `44623650605`, o V2 histórico passou na verificação SHA-256, mas falhou na leitura canônica: faltam somente `ContextPopulation` e `UnpublishedOccurrenceCount`. O diagnóstico indicou `LEGACY_INELIGIBLE_REASONS_UNIT_TF_CALIBRATIONS_BYTES_MATCH`. Logo, os bytes coincidem exatamente com a codificação histórica que omite esses dois campos, e a única condição de exclusão foi a presença de proveniência TF por unidade.

## Decisão técnica e justificativa

Permitir a codificação histórica com `UnitTfCalibrations` **somente** quando o SHA-256 do conteúdo é exatamente `8bdf08affbbcd88550cb055e42a063c1bfff1ffb968b55a3707be6179475d29f` (V2 original fixado pelo executor de comparação). Manter comparação byte a byte contra `EncodeLegacyBootstrapV0`, verificação do SHA esperado pelo chamador, validação integral do payload e demais condições da rotina de compatibilidade. Nenhum outro artefato com calibração TF por unidade obtém exceção.

## Impactos, limites e precedência

A mudança afeta apenas a leitura do artefato histórico explicitamente fixado. Não recalcula TF, não altera parâmetros, fingerprints, corpora, separação TRAIN/VALIDATION, políticas de termos não publicados, gates ou critérios de promoção. Falhas permanecem fail-closed. A compatibilidade específica prevalece sobre a restrição genérica de TF por unidade apenas para o SHA fixado; as demais salvaguardas e decisões científicas vigentes permanecem superiores. CI e testes de regressão ainda devem comprovar a implementação antes da integração.

## Rastreabilidade

PR #1450; execução A2 `44623650605`; commit de implementação `74708e3eaffd8c8a4f5f0c73c38f55cd56b01c5b`; política GOV-DOC-01. Não considerar atividade concluída antes dos gates e da integração documental.
