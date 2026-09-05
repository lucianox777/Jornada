# Estado de Engenharia - v4.04

**Base Normativa:** v3.64  
**Solution Engenharia:** v4.04  
**Data:** 04/09/2026  

## Resultado consolidado

A v4.04 introduz a terminologia canônica mínima de vigência de Benefício Concedido, necessária para análise transversal entre Gestores, sem transformar a Jornada em sistema transacional de gestão do benefício.

`situacaoVigencia` admite somente `VIGENTE`, `SUSPENSA` ou `ENCERRADA`. `situacaoVigenciaDesde` é opcional. Quando `situacaoVigencia=ENCERRADA`, `motivoEncerramento` é obrigatório e admite somente `TERMINO_REGULAR`, `CANCELAMENTO` ou `CESSACAO`. Não existem `OUTRO`, `NAO_CLASSIFICADA` ou `NAO_INICIADA`.

## Extensibilidade factual

O modelo conceitual passa a distinguir os fatos de `CONCESSAO`, `PAGAMENTO` e `RECEBIMENTO` do Benefício. A Fase 1 continua implementando somente `CONCESSAO / Benefício Concedido`; Pagamento e Recebimento ficam previstos para fases futuras, com temporalidade, identificadores, valores e regras próprios. Uma Concessão pode gerar zero ou vários Pagamentos e cada Pagamento pode gerar zero ou vários Recebimentos.

## Compatibilidade

O campo genérico `situacao` permanece apenas para Serviço Prestado no contrato factual. Benefício Concedido usa campos de vigência semanticamente explícitos.

## Baseline pré-implantação e compatibilidade
O gate `contract-backward-compatibility-gate.py` continua fail-closed. A alteração incompatível dos contratos v1 de Benefício nesta release é registrada por `config/release/contract-compatibility-policy.json` no modo `PRE_DEPLOYMENT_BASELINE_RESET`, com `deployed=false`, limitada explicitamente aos schemas AA01, AR01 e POT1. OpenAPI, segurança e demais contratos não recebem waiver. Após implantação/publicação, alteração incompatível exige nova versão de contrato.
