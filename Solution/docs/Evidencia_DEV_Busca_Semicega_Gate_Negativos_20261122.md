# Evidência parcial — testes de fronteira da busca semicega DEV (22/11/2026)

**Rastreabilidade:** plano GOV-DEV-E2E-INTEGRAL-V9-PLANO-20261122, frente D, issue #1854; dependências #539 e #612. **Estado:** proposta de testes estáticos, CI pendente.

## Contexto e justificativa
A busca semicega não deve ser habilitada em HML/PROD nem por mera coincidência do nome do banco. O gate existente exige flag, ambiente Development e marcador residente SQL. A presente mudança reforça os testes negativos de precedência: negação antes de abrir SQL e exigência do marcador residente `Jornada.EnvironmentProfile`.

## Impacto e limites
Somente testes; não altera configuração, visibilidade institucional, endpoint, banco ou autorização. Não comprova top 5, ausência de score, isolamento de órgãos ou E2E HTTP físico; esses critérios continuam pendentes em #1854. Não há nova decisão de produto: são verificações de implementação de salvaguardas já aprovadas.

## Evidência e precedência
Executar testes no SHA da PR e aguardar CI/revisão antes de integração. Não revoga GOV-DOC-01, #539, #378 ou bloqueios HML/PROD.