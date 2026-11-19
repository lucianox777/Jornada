# GOV-V9 — Compatibilidade do V2 histórico fixado (19/11/2026)

**Estado:** proposta em branch isolada; integração e CI pendentes.

## Contexto e evidências
O A2 do run 44624353843 comprovou `V2_SHA_VERIFIED` e `V2_READ_OK` após a correção original 74708e3e. A falha posterior ocorreu após `V3_READ_START`, sem categoria interna confirmada. A PR #1450 contém atualizações Dependabot e não deve ser usada para integrar alterações científicas sem revisão independente.

## Decisão técnica, justificativa e limites
Aceitar a codificação legada com `UnitTfCalibrations` somente para o SHA original `8bdf08affbbcd88550cb055e42a063c1bfff1ffb968b55a3707be6179475d29f`. Preservar SHA esperado pelo chamador, correspondência exata dos bytes com `EncodeLegacyBootstrapV0`, validação integral do payload e falha fechada. Nenhum outro artefato recebe exceção; não modificar TF, TRAIN, VALIDATION, TEST ou certificação.

## Instrumentação V3
Marcadores seguros `V3_READ_START`, `V3_SHA_VERIFIED`, `V3_READ_FAILED_*` e `V3_READ_OK` distinguem tamanho, integridade, contrato, serialização e validação desconhecida. Não há liberação do V3 experimental, cuja proveniência oficial indica `certified=false` e `selectedForUse=false`.

## Bloqueio independente Memory Batch
O run 44624353499 falhou porque o run 38593005691 não expõe mais o artefato histórico `v9-physical-sql-active-candidate-wrong-link-30k` não expirado. Qualquer recuperação exige prova de identidade e proveniência; não substituir automaticamente.

## Precedência, impacto e rastreabilidade
GOV-DOC-01; implementação original 74708e3e; PR #1450 (investigação histórica); branch isolada `fix/v9-a2-v2-v3-diagnostics-20261119`. A alteração é de compatibilidade restrita e diagnóstico; não altera decisões científicas vigentes. Validar CI, A2/A4 e testes antes de integração. Não iniciar TRAIN/TEST científico.

## Acionamento automático da validação A2 (19/11/2026)
A PR isolada também amplia os filtros `pull_request.paths` de `.github/workflows/v9-parallel-orchestrator.yml` para incluir `V9FittedSnapshot.cs` e `V9FrozenJointIndependentComparisonCli.cs`. Justificativa: alterações nos contratos efetivamente consumidos pelo A2 precisam disparar seu preflight não físico, sem depender de execução manual. Impacto: maior cobertura de CI; nenhum workflow físico é acionado por esse orquestrador, cuja permissão de Actions permanece somente de leitura. Evidência de execução e aprovação ainda pendente. Commit `5f0ccd15bbe2f65f1885e7e438b12666cbc845d2`.
