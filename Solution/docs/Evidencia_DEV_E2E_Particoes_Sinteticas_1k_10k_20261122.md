# Evidência técnica — fronteiras de partições sintéticas E2E (22/11/2026)

**Escopo aprovado:** plano `GOV-DEV-E2E-INTEGRAL-V9-PLANO-20261122`, issue #1851; progressão governada 1k → 10k. Sem nova decisão do PO.

## Implementação

A suíte `Solution/tests/scripts/test_v9_http_volume_partition_boundaries.py` exercita as extremidades das dez janelas de 1.000 registros, cobrindo índices 1–10.000 sem sobreposição, CPF sintético determinístico, chaves correspondentes entre pessoas/registros, rejeição de contagens/sementes/índices fora do envelope e proteção contra sobrescrita de saída existente. Integrada ao gate `unit-runtime`.

## Limites

Executa apenas geração local de pequenas amostras das bordas, **não** gera ou envia dez ZIPs de 1k, não acessa SQL/HTTP, não executa Runner V9, não compara com oráculo científico, não comprova E2E integral nem certifica performance de 10k/100k. A execução física permanece sujeita aos gates existentes. Evidência rastreável à #1851 e ao plano aprovado; não altera o índice de decisões vigentes.
