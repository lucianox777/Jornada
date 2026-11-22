# Evidência de implementação — fronteira de gabarito externo E2E DEV (22/11/2026)

**Plano vigente:** GOV-DEV-E2E-INTEGRAL-V9-PLANO-20261122, etapa 8; frente B, issue #1851. **Estado:** implementação proposta, não integrada nem validada fisicamente.

## Contexto e justificativa
O plano aprovado exige ZIP HTTP sintético com verdade de referência fora do ZIP. Este incremento cria um preflight isolado para vincular um ZIP a um gabarito externo por SHA-256, rejeitar identidades duplicadas e entradas ZIP inseguras, e emitir recibo sem expor a lista de identidades. Evita confundir gabarito embutido no payload com evidência independente.

## Escopo e impacto
- `scripts/e2e-external-truth-boundary.py`: entrada `--zip`, `--truth`, `--receipt`; JSON externo com `zipSha256` e `expectedIdentities`; recibo com SHA e contagem.
- `tests/scripts/test_e2e_external_truth_boundary.py`: casos de aceitação e rejeição para digest, esquema, duplicação e caminhos.
- **Não** gera ZIP conforme contrato de ingestão, não implementa oráculo estatístico, não executa Runner V9, não compara scores/decisões e não prova E2E físico. A etapa 8 continua pendente de integração com gerador e oráculo.
- A validação de presença de bytes idênticos do arquivo de verdade dentro do ZIP é uma defesa adicional, não garantia de ausência de qualquer informação derivada do gabarito.

## Precedência, governança e rastreabilidade
Implementação parcial de requisito já aprovado, **sem nova decisão do PO**. Não altera GOV-DOC-01, gates científicos, ativação produtiva, nem os critérios 1k→10k→100k. Evidência final exige CI no SHA da PR, revisão e posterior integração. Referências: #1851, #1584, plano oficial e índice de decisões vigentes.
