# Evidência DEV — despacho produtivo V9 fail-closed (22/11/2026)

## Contexto
Frente A, issues #1584 e #1488, sob o plano `Decisao_GOV_DEV_E2E_Integral_V9_Plano_Aprovado_20261122.md`. O Runner possui ensaios sintéticos não publicadores, mas o adaptador produtivo V9 ainda não está certificado.

## Justificativa e implementação
Foi adicionado teste estático de regressão para impedir que seletores explícitos V9 alcancem o carregador SQL legado ou adquiram lock de pipeline; o despacho tipado continua recusando execução produtiva não certificada. O scorer sintético exige `JornadaHarness`, `MODEL_VALIDATION`, modelo v1 e `Publish=false`. O teste passa a integrar a CI obrigatória.

## Impactos e limitações
Nenhuma ativação de V9, publicação, TRAIN/TEST ou execução SQL física. Este incremento preserva bloqueios e não implementa o adaptador produtivo. A comprovação do Runner RunAsync real, oráculo independente e E2E após Processor permanece pendente.

## Precedência e rastreabilidade
GOV-DOC-01; plano E2E integral V9 DEV aprovado; issues #1584/#1488. Evidência de implementação, não decisão nova do PO. Verificações CI e integração da PR são necessárias antes de considerar esta entrega concluída.
