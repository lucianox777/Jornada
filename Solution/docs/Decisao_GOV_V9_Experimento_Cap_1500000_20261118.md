# GOV-V9-EXPERIMENTO-CAP-1500000-20261118 — APROVADO PELO PO

## Decisão e escopo
Em 18/11/2026, o Product Owner aprovou **um experimento físico não científico** elevando exclusivamente o limite de incidências de candidatos de 1.000.000 para **1.500.000** no workflow manual `v9-one-time-physical-release-preflight.yml`. Não aprovou alterar o limite científico oficial, o vencedor congelado ou as regras de bloqueio.

## Contexto e justificativa
A execução diagnóstica [#44317521867](https://github.com/lucianox777/Jornada/actions/runs/44317521867) contou 1.057.070 incidências, 899.776 pares distintos e 157.294 repetições entre passes. O gate conservador interrompeu antes de materializar SQL e de executar a resolução, impedindo medir o custo real. O experimento de 1,5 milhão permite medir a execução física completa com margem sobre as incidências observadas, sem pressupor desempenho.

## Implementação, limites e evidência
- Variáveis explícitas `JORNADA_V9_DIAGNOSTIC_NON_SCIENTIFIC=true` e `JORNADA_V9_DIAGNOSTIC_PAIR_INCIDENCE_CAP=1500000`, aceitas somente em runtime DEV e no workflow manual físico específico, sob validação fail-closed.
- O valor padrão de 1.000.000 permanece para TEST científico e demais caminhos. Não alterar limites de assinaturas, vencedor, TF, TRAIN, VALIDATION ou algoritmos de resolução.
- O workflow exige SHA exato da master, owner, aprovação explícita, tentativa única, SQL descartável e seed nova; 20 repetições físicas. Resultados continuam diagnósticos, sem certificação, publicação ou ativação.
- Registrar tempo total e P95 caso conclua; eventual falha ou lentidão não autoriza ampliar novamente o limite sem nova avaliação.

## Impactos, precedência e rastreabilidade
Esta decisão **complementa** [GOV-V9-TEST-CENSO-PARES-DISTINTOS](Decisao_GOV_V9_TEST_Censo_Pares_Distintos_20261118.md) e [GOV-V9-DIAGNOSTICO-CAP-INCIDENCIAS](Decisao_GOV_V9_Diagnostico_Cap_Incidencias_20261118.md), sem revogar o limite científico vigente. A integração depende de CI e revisão. GOV-DOC-01.
