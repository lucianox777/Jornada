# Evidência DEV — preflight de integridade do fixture HTTP 1k (22/11/2026)

## Contexto e justificativa
Frente B, issue #1851, plano aprovado `Decisao_GOV_DEV_E2E_Integral_V9_Plano_Aprovado_20261122.md`. O harness já permite fase HTTP de 1.000 pessoas em banco descartável, mas precisava recusar inconsistências entre pessoas e registros sintéticos **antes** de gerar o ZIP e enviar HTTP.

## Implementação e impactos
`v9-http-volume-fixture-preflight.py` valida manifesto presente, cardinalidade exata de 1.000 pessoas e 1.000 registros, CPF sintético único e vínculo biunívoco dos registros com `idPessoaEntrega`. O harness `local-e2e.sh` chama o preflight antes da criação do ZIP no opt-in `JORNADA_E2E_HTTP_VOLUME_COUNT=1000`, salvando recibo agregado sem identidades. Testes offline negativos cobrem CPF duplicado, registro órfão, pessoas truncadas e contagem não aprovada; incluídos na CI obrigatória.

## Limitações
O preflight não é oráculo independente de linkage, não valida scores, clusters ou abstenção e não executa SQL/HTTP sozinho. Nenhuma campanha TRAIN/TEST, ativação HML/PROD ou E2E 100k foi iniciada. CI, revisão e merge permanecem necessários.

## Precedência e rastreabilidade
GOV-DOC-01; plano E2E integral V9 DEV; issue #1851. Não cria nova decisão de PO; índice de decisões vigentes não alterado.
