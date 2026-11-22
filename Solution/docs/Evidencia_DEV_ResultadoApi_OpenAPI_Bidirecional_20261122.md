# Evidência DEV — OpenAPI bidirecional Resultado.Api (22/11/2026)

## Contexto e justificativa
Frente C, issue #1853, plano `Decisao_GOV_DEV_E2E_Integral_V9_Plano_Aprovado_20261122.md`. O gate OpenAPI existente compara rotas de Jornada.Api, mas Resultado.Api não possuía contrato bidirecional próprio.

## Implementação
Contrato `openapi/resultado-v1.openapi.json` para `/health` e `GET /api/v1/ingestao/resultados/{nomeArquivo}`; script `scripts/resultado-openapi-contract-gate.py` reutiliza o inventário de Minimal API e detecta rotas ausentes em ambos os sentidos. Testes negativos verificam rota omitida e rota extra. O teste legado que registrava a ausência do gate foi atualizado.

## Impacto e limitações
Contrato separado por host; não implica inclusão de `/health` na especificação pública da Jornada.Api. O gate compara métodos e caminhos, não valida semântica HTTP, segurança em execução, payloads ou SQL. A CI e testes locais precisam ser comprovados antes do merge. Nenhuma ativação HML/PROD, promoção V9 ou campanha científica é autorizada.

## Precedência e rastreabilidade
GOV-DOC-01; plano E2E integral V9 DEV aprovado; issue #1853; PR desta branch. Sem decisão nova do PO; índice vigente permanece regido pelo plano já documentado.
