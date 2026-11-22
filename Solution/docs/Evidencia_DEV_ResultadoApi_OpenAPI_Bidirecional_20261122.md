# Evidência DEV — OpenAPI bidirecional Resultado.Api (22/11/2026)

## Contexto e justificativa
Frente C, issue #1853, plano `Decisao_GOV_DEV_E2E_Integral_V9_Plano_Aprovado_20261122.md`. O gate OpenAPI existente compara rotas de Jornada.Api, mas Resultado.Api não possuía contrato bidirecional próprio.

## Implementação
Contrato `openapi/resultado-v1.openapi.json` para `/health` e `GET /api/v1/ingestao/resultados/{nomeArquivo}`; script `scripts/resultado-openapi-contract-gate.py` reutiliza o inventário de Minimal API e detecta rotas ausentes em ambos os sentidos. Testes negativos verificam rota omitida e rota extra. O teste legado que registrava a ausência do gate foi atualizado.

## Impacto e limitações
Contrato separado por host; não implica inclusão de `/health` na especificação pública da Jornada.Api. O gate compara métodos e caminhos, não valida semântica HTTP, segurança em execução, payloads ou SQL. A CI e testes locais precisam ser comprovados antes do merge. Nenhuma ativação HML/PROD, promoção V9 ou campanha científica é autorizada.

## Precedência e rastreabilidade
GOV-DOC-01; plano E2E integral V9 DEV aprovado; issue #1853; PR desta branch. Sem decisão nova do PO; índice vigente permanece regido pelo plano já documentado.

## Prova HTTP complementar da entrega sintética (#1853)

Com `JORNADA_E2E_RESULTADO_API=true`, o harness Linux `scripts/local-e2e.sh` inicia a Resultado.Api compilada em porta efêmera de loopback. Ela usa o mesmo SQL JornadaE2E e delega a autorização à Jornada.Api já iniciada. A prova consulta o nome do ZIP após a segunda entrega e exige que o identificador retornado seja o do segundo recibo. Exige duas entregas para esse nome, o estado PROCESSADA e um registro retransmitido conforme a consulta SQL do harness. A releitura autenticada deve reproduzir o mesmo detalhe. O teste exige 401 sem credencial, 400 para nome inválido e 404 com credencial sintética de outro gestor.

O recibo `.local/e2e/resultado-api-evidence.json` contém o SHA do checkout, o ID sintético da entrega, os códigos HTTP, as contagens conferidas e a duração das consultas. Sua emissão exige passar as verificações físicas. O gate OpenAPI estático segue independente. Permanecem pendentes a comparação com OpenAPI gerado em runtime e a validação pós-merge em DEV descartável. Incremento de implementação do plano aprovado, sem nova decisão de produto.
