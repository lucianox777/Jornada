# C3.3a — Estado efetivo da supervisão independente, somente leitura

**Estado desta etapa:** backend + ensaio CI; **não implementa o toggle**
nem os botões de worker. O próximo incremento C3.3b tratará
transições OFF↔ON e exclusão RunOnce; C3.4 tratará a UI Chromium.

## Fonte de verdade e contrato HTTP

`GET /api/workers/supervisor` é uma rota **somente leitura** da
`Jornada.DevConsole`, com `Cache-Control: no-store`. Retorna
`composeProject`, `mode`, `toggleAvailable=false`,
`sqlRunning`, `apiReady`, `resultadoApiLive` e os três workers,
cada um com `state`, `hostPid`, `restartCount`, `instanceId`,
`sqlProcessId`, `heartbeatAgeSeconds` e `containerId`.

O backend consulta o **estado real** do daemon Docker (não memória
da sessão da Console) e o SQL privado `JornadaE2E`. Para todos os
serviços exige labels exatas do projeto efêmero e nome do serviço,
contêiner único sem identidade ambígua, SQL/duas APIs vivos e
healthy, API `/health/ready` + Resultado `/health` via Docker exec
dentro da rede privada. Para worker `ATIVO`, exige simultaneamente
PID host real, restart policy `unless-stopped`, status SQL
`RUNNING` e heartbeat com até 45 s em
`controle.runtime_componente` (distinguindo PID SQL interno do host).

A identificação do projeto é calculada a partir de
`GITHUB_RUN_ID` e `GITHUB_RUN_ATTEMPT`, nunca a partir de
parâmetros HTTP. OFF só pode ser mostrado depois de obter confirmação
SQL/APIs saudáveis e **zero workers em execução**. ON só pode ser
mostrado com os três workers e heartbeat válidos. Um worker ausente,
com heartbeat vencido ou um estado misto provoca `ERRO` ou
`REINICIANDO`, **nunca um falso OFF/ON**. Uma consulta falha retorna
HTTP 503, `mode=ERRO`; fora do perfil retorna HTTP 409, sem invocar
Docker. Não cria processos de worker, não executa start/stop/kill,
não altera DB ou quaisquer volumes.

## Proteções de acesso

A rota só está disponível com todas as condições:
`JORNADA_RUNTIME_MODE=DEV`, `GITHUB_ACTIONS=true`,
`CI=true`, `GITHUB_REPOSITORY=lucianox777/Jornada`,
`JORNADA_WORKERS_E2E_RUNTIME_TEST=true`, run/attempt válidos,
`JORNADA_WORKERS_E2E_ID=ci<run><attempt>`,
senha dedicada de SQL presente, image tag ausente ou `test`, e
sem `DOCKER_HOST` nem `DOCKER_CONTEXT` apontando para daemon remoto.

Não acessar o Docker do usuário, `JornadaLocal`, dados IBGE originais,
NODE Compose canônico, HML/PROD ou outros ambientes; as labels
não podem selecionar contêineres fora do projeto. Segredos nunca
devem ser impressos em logs de erro.

## Aceite incremental

O E2E que já compila a Console executa, no **mesmo job privado**
e **sem segunda compilação**:

1. Após C3.2e e antes de subir workers: iniciar uma instância
   transitória da Console em HTTP **somente loopback** e exigir
   resposta `mode=OFF`, três `PARADO` com PID nulo.
2. Executar C3.2f1, C3.2f2b e C3.2f2c reais existentes.
3. Iniciar outra instância transitória da Console e exigir
   `mode=ON`, três `ATIVO`, PID host, contagem de reinícios
   positiva e heartbeat SQL recente. Confirmar SQL/APIs ativos.
4. Não iniciar nem parar contêineres a partir da Console; somente
   o **processo filho da própria Console** é finalizado pelo ensaio.
   As evidências ficam em
   `.local/e2e/c3-3a-supervisor/snapshot-off.json` e
   `snapshot-on.json`.

Teste de segurança estático/negativo roda no job `unit`
existente; não invoca Docker nem SQL.

**Não marcar DT-20 nem DT-21 como concluídas:** o toggle,
RunOnce dos dois Maintenances, bloqueio atômico de RunOnce no backend,
SIGKILL por botão, histórico externo e a UI com dois botões
ainda requerem implementação e gates específicos.
