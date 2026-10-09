# C3.3b1 — Toggle global OFF↔ON, somente CI DEV descartável

**Escopo:** implementação de **transição global dos três trabalhadores**
no projeto Compose privado `JornadaE2E`. Não é instalação no NODE
canônico, não publica a UI e **não conclui** C3.3/DT-20/DT-21.

## Contrato

`POST /api/workers/supervisor` recebe `{"mode":"OFF"}` ou
`{"mode":"ON"}` no endereço loopback da DevConsole temporária em CI.

- Fora de GitHub-hosted CI, `JORNADA_RUNTIME_MODE=DEV`, run/attempt,
  projeto/segredo privado validado e sem Docker remote context/host,
  **rejeitar** sem inspecionar Docker. Chamadas de rede não-loopback
  também são recusadas.
- O backend serializa transições com `SemaphoreSlim`, lê a situação
  real (Docker labels/PID/restart + SQL heartbeat + APIs prontas)
  antes de agir e novamente após, aguardando os três workers
  entrarem no modo desejado. Não confiar em variável de sessão.
- ON exige OFF comprovado e nenhuma execução finita Silver ativa;
  `docker compose --profile continuous ... up -d --no-build --no-deps
  --force-recreate` **somente** os três workers. Confere política
  `unless-stopped` individual, PID ativo e SQL heartbeat vivo.
  API, ResultadoApi e SQL mantêm contêiner/PID/restart count.
- OFF exige ON comprovado; para cada worker com labels exatas,
  executa **primeiro** `docker update --restart=no`, confirma
  que restart desarmou, e **só então** `docker stop`. Confirma
  contêiner parado e restart ainda `no` após 2 segundos, sem
  mexer nos três serviços de infraestrutura ou nos volumes.
- Requisições de modo já efetivo são idempotentes e não executam
  novo Compose. Um estado parcial/ERRO é recusado até reconciliação
  segura; não deve aparecer ON/OFF falso.
- O comando legado `silver` da Console usa PowerShell do cluster
  comum; para evitar acesso acidental ao usuário, **é bloqueado
  neste perfil efêmero**, inclusive em OFF, até ser criado o
  RunOnce de três workers exclusivamente para esse sandbox.

## Aceite no E2E já existente

Após a prova C3.2f2c e a observação C3.3a ON, o mesmo job inicializa
uma DevConsole efêmera no loopback e verifica via HTTP:
modo inválido recusado, Silver RunOnce legado recusado, ON→OFF
real, GET OFF, segundo OFF idempotente, OFF→ON real, GET ON e
segundo ON idempotente, com PID/restart/heartbeat SQL de cada worker.
Consulta de infraestrutura preservada e ausência de duplicação
de serviços/volumes são condições obrigatórias.

Prova `scripts/e2e-console-supervisor-global-toggle.py`;
artefatos em `.local/e2e/c3-3b1-global-toggle/`. Teste negativo
estático no job `unit` existente, nenhum novo build .NET.

## Ainda falta

C3.3b2 deverá fornecer **RunOnce realmente isolado** para Processor,
Operations Maintenance e Bronze Maintenance, com confirmação de
parada de RunOnce ativos antes de ON e lock compartilhado com
as rotas. C3.3c deverá expor `Parar processo` individual por
PID verificado e enriquecer logs reais. C3.4 criará a interface
Chromium com toggle global, dois botões verticalmente alinhados
por worker e indicador periódico, mantendo `Log da sessão`.
Até lá `toggleAvailable=false` permanece no contrato read-only.

**PROIBIDO:** `JornadaLocal`, IBGE original, NODE Compose de usuário,
HML/PROD, contêineres/volumes existentes, comandos globais `prune`
ou `down -v`. Não completar/mesclar sem E2E real verde da HEAD
exata. A Trilha 4 permanece suspensa, DT-10 separação CI por último.
