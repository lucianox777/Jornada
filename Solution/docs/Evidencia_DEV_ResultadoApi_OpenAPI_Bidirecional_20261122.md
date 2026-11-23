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

## Continuidade do recibo Resultado.Api — hash do ZIP e reconciliação por lote (23/11/2026)

**Contexto e achado:** o harness DEV existente já prova respostas HTTP da `Resultado.Api` relativas à segunda Entrega do mesmo ZIP, auditoria por status, negativas 401/400/404, duas entregas no mesmo nome e fato retransmitido. Entretanto, a versão anterior aceitava um payload SHA-256 divergente ou resumos de processamento e retenção incoerentes, desde que identificador, status e contagem isolada correspondessem.

**Incremento técnico da frente aprovada #1853:** calcular SHA-256 dos bytes locais do ZIP realmente enviado; exigir no GET autorizado o mesmo `payloadSha256` e `entregaId`, gestor SEHAB, nome exato, estado terminal `PROCESSADA`, leitura estável e volume recebido positivo. Validar lotes não duplicados e processados com as contagens da fixture (1 Pessoa + 1 Registro), totais detalhados/consolidados por classe e indicador de retenção, preservando a asserção de uma retransmissão do fato. Nenhum CPF, UUID ou ZIP é publicado no recibo agregador.

**Prova e efeito:** `e2e-resultado-entrega.py` agora emite apenas três campos adicionais de aceitação `payloadSha256Verified=true`, `lotesProcessados>=1` e `resumoItensConsistente=true`; `e2e-integral-dev-evidence.py` rejeita a ausência/valores indevidos. Testes Python offline positivos (inclusive consolidação por retenção) e adversariais de hash falso, outro gestor, lote duplicado, tipo inválido e resumos incorretos são executados pelo gate obrigatório existente `test_ci_required_gates.py`. Não modifica a `Resultado.Api` produtiva nem cria workflow novo. Essas regras são próprias da fixture sintética AA01 com 1 Pessoa e 1 Registro.

**Estado e limites:** implementação proposta na PR Draft [#1920](https://github.com/lucianox777/Jornada/pull/1920); CI e prova real de HTTP/SQL da `Resultado.Api` permanecem por confirmar no SHA da implementação. O dispatch E2E DEV é owner-only e não foi executado. Esta prova parcial não demonstra Runner V9, Gold integral nem marcos `E2E_NUCLEO_PASS`/`E2E_INTEGRAL_PASS`. **Precedência/GOV-DOC-01:** detalhamento da decisão já aprovada no plano E2E V9, vinculada a #1853/#1851, sem decisão nova do PO e sem alterar vigência do índice.
