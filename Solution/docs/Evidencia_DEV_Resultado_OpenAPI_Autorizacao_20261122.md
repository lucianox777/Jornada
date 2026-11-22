# Evidência parcial — Resultado.Api: gate de autorização junto ao OpenAPI (22/11/2026)

**Plano vigente:** GOV-DEV-E2E-INTEGRAL-V9-PLANO-20261122; frente C, issue #1853.

## Contexto e justificativa
A comparação bidirecional de rotas entre código e OpenAPI já existe. Ela, isoladamente, não detecta a remoção da autorização de `/api/v1/ingestao/resultados/{nomeArquivo}` nem a ausência do verificador delegado. O incremento adiciona verificações estáticas fail-closed para esses pontos ao gate existente.

## Alterações e impactos
- `scripts/resultado-openapi-contract-gate.py`: exige a rota protegida, `RequireAuthorization("jornada.ingestao.status")`, o registro do `ResultadoAccessVerifier` e os middlewares de autenticação/autorização.
- `tests/scripts/test_resultado_openapi_bidirectional.py`: casos negativos para retirada da autorização e do verificador, mantendo o contrato OpenAPI inalterado.
- Escopo restrito à defesa contra regressões textuais do código. Não comprova ordenação dinâmica de middleware, autorização efetiva HTTP, isolamento SQL, nem OpenAPI gerado em runtime. Esses itens continuam em #1853.

## Governança, precedência e rastreabilidade
Implementação incremental do plano já aprovado, sem nova decisão do PO. Preserva delegação de autoridade à Jornada.Api, isolamento de gestor e proibição de ativação HML/PROD sem gates. Revisão e CI no SHA da PR são obrigatórios antes da integração. GOV-DOC-01 permanece vigente.
