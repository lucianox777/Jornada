# V9 — Evidência técnica A2/V3 e Memory Batch — 19/11/2026

**Estado:** diagnóstico técnico em validação na PR #1450; sem aprovação de TRAIN científico, TEST, publicação ou promoção.

## Contexto e evidências
- No A2 do run 44624353843 (job 135914107882), o V2 histórico registrou `V2_SHA_VERIFIED` e `V2_READ_OK`. A etapa seguinte registrou `V3_READ_START` e terminou com `V9_CALIBRATOR_FAILED:INVALID_DATA:UNCLASSIFIED`. Isso não demonstra qual verificação interna do V3 falhou.
- O V3 experimental permanece fixado pelo SHA `8bf6d92db9efffa7cfdc26bc8d23ade01a6fc3cb430b5a5b0e9c425557cbdefc`, com `certified=false` e `selectedForUse=false` no manifesto de referências.
- No Memory Batch run 44624353499 (job 135914112504), o artefato `v9-physical-sql-active-candidate-wrong-link-30k` do run 38593005691 não estava disponível sem expiração. Não substituir o corpus sem prova independente de SHA/proveniência.

## Alteração técnica proposta
Instrumentação **diagnóstica** do A2 para o V3, análoga à do V2: limites de tamanho, SHA fixado, marcador `V3_SHA_VERIFIED`, e códigos finitos de falha de leitura, JSON, identidade, contrato, separação ou serialização. A validação integral e o comportamento fail-closed permanecem inalterados; nenhuma mensagem arbitrária de exceção, caminho ou dado científico é publicada.

## Plano de validação
1. CI do commit de instrumentação: confirmar compilação, testes e execução do A2.
2. Se A2 falhar, registrar o marcador `V3_READ_FAILED_*` exato antes de propor correção de compatibilidade.
3. Investigar fonte íntegra do corpus Memory Batch com proveniência verificável, sem substituição automática.
4. Validar A2, consolidação A4 e Memory Batch independentemente.
5. Somente após gates e integração considerar encerrada a atividade.

## Rastreabilidade e precedência
PR #1450; commits `74708e3eaffd8c8a4f5f0c73c38f55cd56b01c5b` (compatibilidade V2) e `638989b94cb25bcb24be5e7b74d1ba3626dcaade` (diagnóstico V3); `Solution/docs/Decisao_GOV_V9_Compatibilidade_V2_Historico_20261119.md`; política GOV-DOC-01. Esta evidência não cria decisão científica nova nem altera a precedência das decisões vigentes.
