# GOV-V9-SEED42-PROVENIENCIA-DIAGNOSTICO-20261119

## Contexto e evidências
Após merge autorizado da PR #1748 (commit `2df1fb2f6fdffa175179fd9474f616a1a6aa4e10`), cinco workflows físicos da master falharam na etapa de reconstrução do corpus seed42 de 30 mil pessoas: runs 44632345074, 44632345240, 44632344927, 44632344817 e 44632344921. Todos reportaram `ValueError: Synthetic bootstrap differs from ref registry and live corpus`, emitido por `Solution/scripts/v9-tf-ref-provenance-gate.py`.

## Decisão técnica e justificativa
Preservar integralmente os critérios de autenticação do corpus e do registro histórico, mas tornar a divergência diagnosticável mediante emissão **somente dos nomes dos campos** que não coincidem. Não imprimir valores, SHA-256, dados pessoais ou conteúdo dos arquivos. A divergência permanece falha bloqueante, sem fallback, regravação de referência ou geração de novo registro aceito.

## Implementação, impactos e limites
A validação mantém todas as sete comparações existentes: `CorpusFingerprint`, `GenerationHypothesesFingerprint`, `GeneratorVersion`, `GeneratorSeed`, `People`, `Provenance` e `PinnedIbgeBootstrap`. Em caso de erro, lista os campos divergentes no `ValueError`. A alteração é observacional; não corrige ainda a divergência física nem autoriza TRAIN/TEST, ativação, publicação ou substituição do registro imutável.

## Precedência, governança e rastreabilidade
Subordinada à GOV-DOC-01, aos gates de proveniência e às regras de isolamento TRAIN/AUDIT/VALIDATION/TEST. A PR #1748 e sua compatibilidade V2/V3 não são consideradas causa da divergência sem prova adicional. A PR #1749 atualiza separadamente o índice da integração anterior; este registro acompanha a nova correção. A aprovação depende dos checks e da integração pela política do repositório.

## Evidências de validação da instrumentação
No commit `cd8fc627739320e47b7875544f0db80136af2749`, as execuções `jornada-pr-required-fast` #44634444066, `v9-experimental-fast-pr` #44634444083 e `V9 full-schema real RunAsync smoke` #44634444067 concluíram com sucesso. Os testes acrescentados verificam que somente nomes de campos divergentes aparecem na exceção, sem os valores de fingerprint, e que a igualdade estrita de `PinnedIbgeBootstrap` permanece obrigatória. Esses resultados validam a instrumentação na PR #1750, mas **não** comprovam a reconstrução física seed42/30k, a origem da divergência ou a certificação científica. O workflow `v9-physical-seed42-probe` é acionado por push na master; nenhuma integração ou execução física desta branch está aqui declarada.
