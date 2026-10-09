# C3.2f2b — Base operacional para recuperação em SQL privado

**Estado da branch:** preparação. Nenhuma prova de recuperação é declarada.

## Hipótese verificável

Antes de injetar uma falha durante uma transação, é necessário provar que
a **mesma topologia privada** que reinicia os workers (C3.2f1) consegue
receber um ZIP real via API, processá-lo com o Processor residente, persistir
itens em SQL e rejeitar duplicação após repetição HTTP da mesma chave.

O script `scripts/e2e-private-api-ingestion-baseline-runtime.sh` usa
`JornadaE2E` e um projeto Compose
`jornada-workers-e2e-ci<GITHUB_RUN_ID><GITHUB_RUN_ATTEMPT>`, apenas em
GitHub Actions com opt-in explícito. Nunca usa JDBC/SQL do host,
`JornadaLocal`, cluster NODE canônico, HML/PROD ou dados reais.

## Critérios PASS de C3.2f2b, sem injeção de falhas

1. Container IDs, serviço e projeto devem coincidir nas labels; SQL, API,
   ResultadoApi e os três workers precisam estar em execução isoladamente.
2. API `/health/ready` e heartbeat do Processor em SQL devem ser reais,
   não apenas PID.
3. Um ZIP único e determinístico deve ser produzido das fixtures
   `tests/fixtures/ingestao/AA01_v2`; hash SHA-256 validado.
4. `POST /api/v1/ingestao/entregas` na API **dentro do contêiner privado**
   com credencial sintética de teste deve retornar HTTP 202 e `entregaId`.
   A API e o Processor compartilham somente Bronze/Staging do Compose.
5. A Entrega criada deve passar a `PROCESSADA` e ter um Lote. As contagens
   **por Entrega/Lote** devem ser exatamente 1 Entrega, 1 Lote, 1 Pessoa
   Silver, 1 Registro Silver, 2 itens processados e >= 1 Serving.
6. Um segundo POST com o mesmo ZIP/chave deve devolver o mesmo `entregaId`
   sem modificar nenhuma dessas contagens. Readiness da API e Processor
   devem permanecer válidos.
7. A prova `summary.json` registra `fault_injected=false` e
   `processing_recovery_verified=false`. Nenhuma propriedade do teste
   pode ser confundida com rollback, lease expirado ou reprocessamento.

## Próximo gate obrigatório (C3.2f2c)

Esta prova é somente a **base real de ingestão**, não recuperação.
Para C3.2f2c, repetir a entrega com uma chave diferente, instalar uma
barreira **determinística e reversível APENAS no SQL descartável** após
primeira escrita dentro de transação Serializable, comprovar que a
transação está aberta e o lote está PROCESSANDO antes da injeção de
SIGKILL exclusiva no Processor. Depois medir rollback, vencimento efetivo
da linha `ingestao.lote_heartbeat` pareada ao token, requeue automático,
fencing, tentativa nova, PROCESSADA terminal, cardinalidades finais e
replay idempotente. Emitir dados reais para o validador C3.2f2a com
evidências GitHub Actions e consultas SQL observáveis. Não fabricar
JSON de aceitação. Nenhuma alteração de aplicação deve ser habilitada
fora do sandbox DEV privado.
