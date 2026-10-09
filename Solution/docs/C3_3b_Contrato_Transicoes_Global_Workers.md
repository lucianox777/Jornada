# C3.3b — Controlador global OFF↔ON: contrato técnico antes do runtime

**Estado:** especificação de implementação, **sem backend mutável** e
sem aceitação C3.3b. A C3.3a (#851) trata apenas da leitura do
estado efetivo Docker/PID/SQL; não confundir `mode=ON` observado
com um botão/toggle já implementado.

## Domínio de segurança

Todos os comandos operacionais devem ficar confinados ao
`jornada-workers-e2e-ci<GITHUB_RUN_ID><GITHUB_RUN_ATTEMPT>`
GitHub-hosted com SQL sintético `JornadaE2E`, labels
`com.docker.compose.project` e `com.docker.compose.service`
exatas, serviço de destino em allowlist fixa
`processor`, `operations-maintenance`, `bronze-maintenance`.
Exigir guards idênticos aos da C3.3a, incluindo `DOCKER_HOST`/
`DOCKER_CONTEXT` vazios e ausência de qualquer objeto externo.
HML/PROD, NODE Compose canônico, `JornadaLocal`, IBGE original,
contêineres/volumes/segredos de usuário são proibidos.

Não aceitar project name, CID, worker, PID, comando Docker ou caminho
de Compose arbitrários vindos do corpo da chamada HTTP.

## Lock compartilhado e origem de verdade

- Um único `SemaphoreSlim(1,1)` atende **os três workers e as duas
  transições globais**: `POST /api/workers/supervisor/mode`,
  RunOnce individuais e `POST .../{worker}/stop`. O lock vive em
  serviço singleton da Console, mas **não é origem de estado**.
- Uma transição tem primeiro passo de reservar/excluir novos RunOnce;
  o bloqueio no backend deve acontecer **antes** da consulta de
  processos e durar até confirmar resultado. A UI desabilitar botão
  não é suficiente.
- `LiveExecutionService` deve registrar processos finitos reais,
  distinguir o command ID e manter `CancellationTokenSource`,
  PID filho e conclusão, com locking compartilhado. Atualmente
  `StartCommand` dispara `Task.Run` e não oferece cancelamento:
  não alegar que é possível matar RunOnce de forma segura sem
  refatorar esse ciclo primeiro.
- Reload/restart da Console **reconstrói** modo real por inspeção
  Docker/SQL e não dispara `up`/stop baseado em flag perdida. Se
  existem 1–2 workers, classificá-los como `ERRO` e bloquear
  RunOnce até reconciliar.
- Proibir reinício de banco, API, ResultadoApi ou containers com
  labels fora da allowlist; nunca usar `docker compose down`,
  `docker stop` arbitrário, `prune` ou `rm -v`.

## OFF → ON

1. Receber `enabled=true`, capturar lock e bloquear novos RunOnce
   **antes** da consulta de operações ativas.
2. Se qualquer RunOnce (Processor, Operations, Bronze) estiver ativo
   e `confirmStopRunOnce` não for `true`, responder HTTP 409 com
   confirmação exigida; não iniciar nenhum residente.
3. Após confirmação explícita, encerrar somente **os PIDs de RunOnce
   registrados da própria Console** e aguardar término de fato.
   Se algum não parar, manter `ERRO` e não liberar RunOnce; não
   iniciar residentes conflitantes.
4. Verificar que o SQL bootstrap, API e ResultadoApi da rede privada
   estão prontos, imagem já construída, e que nenhum worker fora
   do projeto está selecionado.
5. Configurar políticas independentes de restart
   `unless-stopped` para apenas os três serviços allowlisted
   no projeto descartável; iniciar os três com o Compose isolado
   **sem recompilar**.
6. Aguardar cada PID>1, política correta, heartbeat SQL recente,
   `instance_id` distinto e API/Resultado/SQL estáveis.
7. Só então responder `mode=ON`/status operacional; falha parcial
   mantém modo efetivo `ERRO` e os RunOnce ainda bloqueados.

## ON → OFF

1. Capturar o mesmo lock e bloquear novos RunOnce.
2. Conferir CID/labels/PIDs exatos. **Primeiro** desarmar a política
   automática de restart dos três serviços e conferir
   `HostConfig.RestartPolicy` sem restart (por exemplo,
   `docker update --restart=no` apenas para CIDs verificados do
   projeto). Não parar API/Resultado/SQL.
3. Em seguida executar parada administrativa somente dos três
   serviços (por exemplo, Compose `stop` com `--profile continuous`
   e `-p` já comprovado). Sem limpeza de volumes.
4. Conferir ausência de **todos** os PIDs de workers e impedir
   qualquer ressurgimento durante janela de observação. Só então
   destravar RunOnce.
5. Se parar 2/3, **não** reportar OFF e **não** liberar RunOnce.
   Expor `ERRO` para reconciliação controlada no mesmo projeto;
   não usar reset global para corrigir.

## Parar processo individual (C3.3c)

No modo ON e somente com PID host real + heartbeat ativo, botão
`Parar processo` dá SIGKILL **apenas** ao PID host do worker
identificado e depois verifica evento Docker `die(exitCode=137)`,
novo PID, RestartCount e SQL instance_id. O supervisor externo,
não a Console, reinicia o processo. Nenhum botão "Iniciar"
é permitido. Em OFF, o botão é proibido no backend.

## Contratos de teste pendentes

- TC-SV01 OFF inicial com zero residentes e três RunOnce elegíveis.
- TC-SV02/02b regressão Silver e novos RunOnce dos dois Maintenances.
- TC-SV03 falha 409 sem confirmação, confirmação explícita,
  bloqueio RunOnce atômico, ON com três PIDs/heartbeats.
- TC-SV04/05 parada individual e reinício externo sem afetar irmãos.
- TC-SV06 recuperação de trabalho separada de reinício do processo
  (C3.2f2c já provou essa capacidade **em API/worker isolados**;
  falta dispará-la pelo controle da Console).
- TC-SV07 OFF desarma restart antes da parada, não ressuscita.
- TC-SV08 refresh/restart recupera estado efetivo; concorrência
  de duas transições é serializada.
- TC-SV09 falha parcial não libera RunOnce.
- TC-SV10 negativa fora do sandbox; sem alteração a outras
  topologias.

O primeiro PR de código C3.3b deve acompanhar a **mesma execução
E2E SQL privada já existente**, sem criar outro build dotnet nem
separar CI (DT-10 fica por último). Só permitir squash merge após
logs/artifacts de transições reais da HEAD exata e todos os gates.
