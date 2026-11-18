# Auditoria de artefatos científicos V9 e triagem de PRs — run 44329203788

Data: 2026-11-18. Fonte: artefatos oficiais do GitHub Actions, run 44329203788, SHA de código 344c401bf9ea2289b3437554b9f1caff9df7f5c4.

## Evidência diretamente observada

- `v9-tf4-e2e-closeout`: TRAIN, AUDIT, VALIDATION e TEST = success; `certified=false`, `activated=false`, `productionPublication=false`.
- `v9-tf4-validation-e2e-status`: três configurações comparadas e medidas, `VALIDATION_SCIENTIFIC_WINNER_SELECTED_NOT_CERTIFIED`, TEST não usado na seleção.
- `v9-tf4-independent-test`: vencedor congelado SHA `24f119ace8340d212dd567674b66da9b89d8ba8f28776cfc1a632d470ead9208`; 100.000 identidades-base, 170.369 observações, 125.187 aceitações verdadeiras, 90 falsas, 2.276 conflitos, 42.816 não resolvidas, recall global 0,9675766335, FDR 0,0007184080, P95 71.372,5429 ms. `TfWeight=0`, sem alegar benefício TF4 nem equivalência produtiva.
- `v9-v3-tf4-physical-train`: três workers simultâneos observados, `StopReason=COMPUTE_BUDGET_EXHAUSTED_NOT_OPTIMALITY`, nenhuma evidência de otimalidade global. Evidência de memória indica OOM delta 0.
- `v9-tf4-independent-test`: `productionRunnerFullPipelineParityCertified=false`; modelo operacional não ativo nem publicado.

## Triagem das PRs abertas (recomendação, não decisão de encerramento)

- #1604 **manter para avaliação técnica**: contém workflow diagnóstico SQL × C#, código Worker, testes e documentação; o sucesso científico não substitui sua medição física de desempenho. Branch com conflito exige rebase se a investigação continuar.
- #1613 **manter até reconciliação documental**: contém decisão GOV-DOC-01 sobre diagnóstico SQL/C# 100k e comparação histórica; conferir precedência e absorver conteúdo antes de eventual encerramento. Branch com conflito.
- #1641 **revisar seed inédita**: a execução registra seed TEST, mas não comprova, isoladamente, reserva atômica global por GUID prevista na PR. Branch com conflito.
- #1633 **manter em análise**: referência estatística comum às três coortes é decisão de governança distinta do sucesso do TEST; verificar implementação.
- #1576 **manter**: paridade Runner produtivo continua não certificada.
- #1719 **integrada**: correção de cancelamento e rollback do ledger sintético; não é prova física de rollback.

## Critérios para fechar sem perda de rastreabilidade

Antes de fechar PRs com conflito, comparar mudanças com master, transferir decisões ainda vigentes e evidências para documentos oficiais, registrar PR substituta ou issue e justificar o encerramento no próprio GitHub. Não fechar #1604/#1613 somente porque a campanha científica passou. Nenhuma ativação produtiva está autorizada. GOV-DOC-01.
