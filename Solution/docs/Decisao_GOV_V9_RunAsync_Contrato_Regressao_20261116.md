# GOV-V9-RUNASYNC-CONTRATO-REGRESSAO-20261116

## Contexto
Issue #1584: paridade científica com o Runner produtivo permanece sem certificação. Inspeção do código existente mostra uma via opt-in `IV9NonPublishingFrozenCohortScorer` dentro do **verdadeiro** `ProbabilisticLinkageBatchRunner.RunAsync`, distinta do ensaio `RehearseV9NonPublishingSyntheticTestAsync`.

## Decisão de implementação proposta
Adicionar teste estrutural de regressão para exigir: recusa de `Publish=true` e modos diferentes de `MODEL_VALIDATION` antes do lock e da criação do run; restrição a `JornadaHarness`; preparação global de coorte antes de persistir o primeiro lote; contabilidade de universo integral; caminho terminal não publicador e marcação de falhas/cancelamentos. O teste não altera produção.

## Limites e impactos
A existência da via opt-in **não prova** equivalência com o modelo SQL V9 pinado nem com o fluxo integral de produção. O teste é estático e não executa SQL. Não confundir com certificação de paridade, não remover `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED`, não iniciar TRAIN/TEST, não publicar Gold/Serving. Próxima prova requerida: integração física end-to-end com modelo pinado, SQL descartável, execução real `RunAsync`, comparação independente de decisões e estados, isolamento e não publicação.

## Precedência e rastreabilidade
GOV-DOC-01; issue #1584; `Solution/src/Jornada.Linkage.Runner/ProbabilisticLinkageBatchRunner.cs`; `Solution/tests/scripts/test_v9_real_runasync_nonpublication_contract.py`. Estado: proposta em PR, pendente CI e merge.
