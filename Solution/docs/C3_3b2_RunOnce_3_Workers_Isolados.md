# C3.3b2 — RunOnce independente dos três workers em Compose DEV privado

**Escopo desta etapa:** rota backend de execução finita por trabalhador,
três workers .NET reais, manutenção da supervisão global OFF e exclusão
com a transição ON. A interface gráfica C3.4 e o comando individual
`Parar processo` C3.3c **continuam pendentes**.

## Contrato

`POST /api/workers/{worker}/run-once` só aceita exatamente
`processor`, `operations-maintenance` ou `bronze-maintenance`;
fora do sandbox Github-hosted `JornadaE2E` DEV ou fora de loopback
deve recusar a chamada **antes de acessar o Docker**.

O `IsolatedWorkerSupervisorModeController` compartilha o mesmo
`SemaphoreSlim` do toggle OFF↔ON para **admitir atomicamente** uma
execução finita, conferindo `mode=OFF` real em Docker+SQL,
sem residentes nem outro RunOnce ativo. A execução corre fora do
lock, mas o registro concorrente `activeFinite` impede que ON
comece enquanto ela está viva. Uma segunda solicitação de RunOnce
também é recusada. Enquanto a execução está ativa o GET read-only
marca somente aquele worker `RUN_ONCE`, permanecendo o **modo global
OFF** (os residentes continuam parados). Conclusão finita
restaura a disponibilidade de ON, mas não o ativa implicitamente.

## Contêiner temporário, sem reinício automático

`worker-entrypoint.sh` suporta `--run-once` somente com
`JORNADA_WORKERS_E2E_RUN_ONCE=true`,
`JORNADA_WORKERS_E2E_RUN_ONCE_ALLOWED=true` e identificação
`ci<run><attempt>`, além dos guardas DEV/Development,
banco real `JornadaE2E`, NODE allowlisted e ligação SQL existente.
Cada DLL é lançada em PID 1, com a configuração finita correta:
`Processor__RunOnce=true`,
`MaintenanceExecution__RunOnce=true` ou
`BronzeMaintenance__RunOnce=true`, limite máximo 65 segundos.

O helper privado cria **somente** um contêiner Docker Compose
`run --rm --no-deps -T` por comando. Os residentes do mesmo serviço
precisam estar OFF e os três serviços `sqlserver`, `api` e
`resultado-api` devem estar healthy e conservar PID/RestartCount.
O executável finito deve terminar com código zero e o contêiner
one-off ser removido ao concluir. Sem construir outra imagem,
subir serviços do usuário ou abrir portas de host.

As labels `com.docker.compose.oneoff` distinguem contêiner
temporário de residente. O observador GET e o controlador global
passam a filtrar apenas serviços residentes; o ON é bloqueado se
houver um one-off pendente ou órfão. **Se um comando expirar e deixar
contêiner one-off órfão, ON falha fechado:** jamais assumir que
o serviço terminou só porque o comando controlador foi interrompido.

## Prova operacional obrigatória

No mesmo job E2E que já comprova rollback C3.2f2c e OFF↔ON C3.3b1:

1. ON: tentar RunOnce para os três workers e exigir HTTP 409;
   trabalhador desconhecido também deve ser recusado.
2. OFF: comprovar todos os residentes sem PID.
3. Um por vez, executar RunOnce de Processor, Operations Maintenance e
   Bronze Maintenance por HTTP da Console transitória, exigir
   `CONCLUIDO/exitCode=0` real do worker .NET e modo OFF efetivo
   depois de cada comando.
4. Reativar ON global e confirmar três PIDs independentes novos,
   heartbeat SQL fresco e API/Resultado/SQL preservados.
5. Publicar evidência `.local/e2e/c3-3b2-three-runonce/summary.json`
   somente após todos esses asserts. Testes negativos sem Docker
   rodam no mesmo job Unit já existente.

## Critérios pendentes após esta PR

C3.3b3: confirmação/encerramento seguro de RunOnce ativo
ao solicitar ON, sem race e sem deixar contêiner one-off órfão.
C3.3c: `Parar processo` individual por PID verdadeiro, evento
de restart, log de sessão/external history. C3.4: toggle e
dois botões empilhados verticalmente em cada card com refresh
automático, prova Chromium. DT-10 separação de CI será a última
etapa, e Trilha 4 continua suspensa.

**Proteção:** nunca atingir banco de usuário, IBGE original,
cluster NODE canônico, volumes/containers reais, HML/PROD ou
dados reais. Somente GitHub-hosted Compose `jornada-workers-e2e-ci*`,
sem portas host nem bind mounts, credenciais sintéticas.
