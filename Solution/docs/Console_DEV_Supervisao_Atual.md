# Console DEV — guia corrente de supervisão e operações dos workers

**Conferência:** `master` de 09/10/2026 (corte
`b4f74cb34e4346191ed58010b3d6b5cbb2db7e65`).
**Ambiente autorizado de prova:** **somente** projeto privado descartável de
GitHub Actions/DEV, base SQL `JornadaE2E`, nunca NODE/JornadaLocal,
host comum, HML, PROD ou massa real. Este guia substitui a interpretação
de “a implementar” das decisões de 08/10 na **parte já integrada**;
não altera suas regras de aceite, que continuam referência do projeto.

## 1. Objetivo e desenho em operação

A Console auxilia o desenvolvimento; ela não é painel corporativo de
produção nem substitui o scheduler homologado. Para os workers
`Processor`, `Operations Maintenance` e `Bronze Maintenance`,
o HTML `src/Jornada.DevConsole/Page.cs` apresenta:

- Um único modo de **Supervisão automática** para os três workers:
  **OFF** ou **ON**.
- Indicador automático de estado, PID e heartbeat observado,
  atualizado por leitura do backend (sem botão de Status).
- Para cada worker, **dois botões alinhados verticalmente**:
  **Executar uma vez** e **Parar processo**. Não há botão
  separado “Iniciar processo contínuo”; ON global faz isso.
- Log da sessão já existente, alimentado com eventos de comandos e
  resposta HTTP. O log local **não** prova a ocorrência de eventos
  quando a Console está fechada: é necessária fonte externa de
  telemetria/auditoria para garantir histórico completo.

## 2. Estados e ações

| Modo real | RunOnce | Parar processo | Interpretação |
|---|---|---|---|
| OFF, sem one-off | Permitido por worker, uma execução finita de cada vez | Desabilitado, não há residente | Três serviços residentes parados; API/Resultado/SQL continuam vivos |
| OFF com RUN_ONCE | Outro RunOnce e ON são recusados enquanto one-off ativo | Desabilitado | Perder conexão do navegador **não** libera o processo |
| ON, worker ATIVO | Recusado no backend e desabilitado na UI | Habilitado apenas com PID real/identidade válida | Serviço independente com política de reinício |
| REINICIANDO/ERRO/parcial | Não assumir OFF livre | Não agir sem identidade viva | Consultar log e fonte Docker/SQL; não inventar saúde |

**Invariante do ON:** se o RunOnce finito estiver em andamento,
a ativação é recusada com conflito; a implementação atual **não**
realiza o cancelamento automaticamente nem oferece contrato de
confirmação explícita para o mesmo RunOnce. Isso é a pendência
[C3.3b3](C3_3b3_Confirmacao_Cancelamento_RunOnce.md).

**Invariante do OFF:** desarmar restart dos três trabalhadores **antes**
da parada dos residentes; a API, ResultadoApi e SQL mantêm as próprias
identidades e estado. Ao retornar ON, conferir três PIDs/heartbeats
novos; uma resposta HTTP isolada não substitui essa prova.

## 3. API do backend da Console

| Rota | Uso | Limite de segurança |
|---|---|---|
| `GET /api/workers/supervisor` | Estado efetivo global, três workers, PIDs/restarts, identidades e idade de heartbeat, SQL/API/Resultado | Observa Docker + `controle.runtime_componente` do sandbox |
| `POST /api/workers/supervisor` com `{"mode":"ON"}`/`{"mode":"OFF"}` | Alternância global serializada | Perfil CI/DEV allowlisted; endpoint de mutação apenas loopback |
| `POST /api/workers/{worker}/run-once` | Executa ciclo finito de `processor`, `operations-maintenance` ou `bronze-maintenance` | Apenas com OFF efetivo e nenhuma outra execução |
| `POST /api/workers/{worker}/stop` | Interrompe **o residente** de worker allowlisted, observado por Docker/SQL | Apenas quando ativo e com PID/labels/estado comprovados |
| `GET /api/activity` | Histórico da **sessão** da Console | Não substitui log externo persistente nem telemetria de runtime |

As mutações DEV recusam acesso fora do profile GitHub Actions descartável,
repositório/run/attempt validados e loopback. O modo real não é
deduzido da última opção clicada. Chamadas negadas não
autorizam intervenções manuais em Docker de usuário.

## 4. Provas e limites de cada entrega

| Incremento | Evidência integrada | O que não provar por inferência |
|---|---|---|
| C3.2f1–f2c / #847–#850 | Três restarts independentes; ZIP real sintético; interrupção e rollback da transação; lease expirado/fencing/reprocessamento sem duplicar | HML/PROD nem massa real |
| C3.3a / #851 | OFF/ON observados via Console HTTP, PIDs e heartbeat | Ação de toggle ou UI (surgem depois) |
| C3.3b1 / #852 | ON→OFF→ON com preservação de APIs/SQL e desarme de restart | Cancelamento confirmado de finito ativo |
| C3.3b2 / #853 | RunOnce finito dos três trabalhadores, `exitCode=0`, OFF preservado | Histórico de execução durável |
| C3.3b3a / #854 | Cliente desconectado não abandona one-off; ON recusado até término | Botão de confirmação de cancelamento |
| C3.3c / #855 | Parada individual e prova de identidade/restart em sandbox | Comando de kill autorizado em cluster ordinário |
| C3.4 / #856 | Integração do painel, controles individuais e indicador na Console DEV; regressões E2E/Chromium conforme CI vinculada à PR | Implantação gráfica em HML/PROD |

O runner relevante é `scripts/e2e-private-sql-bootstrap-runtime.sh`,
que organiza casos depois do bootstrap `JornadaE2E`. Evidências
podem aparecer em `Solution/.local/e2e/` **na execução da CI**,
não como arquivos esperados na máquina do usuário. Conferir o
workflow da HEAD e os artefatos reais.

## 5. Segurança e resposta a incidentes

Não iniciar uma Console com essas permissões em `JornadaLocal` ou
outro cluster persistente. **Não** copiar/usar PID de testes
GitHub para operar sistema real. Quando o status estiver ERRO,
misto, sem heartbeat ou com one-off órfão, manter ON bloqueado,
consultar log e as evidências do ambiente autorizado, registrar
incidente e reconciliar sob procedimento específico. Não executar
`docker prune`, `down -v` ou limpeza ampla como “conserto”.

A recuperação de trabalho e a retomada de lote são responsabilidades
do worker, incluindo leases/heartbeats; restart do processo, por si,
não prova que o lote concluiu.

## 6. Pendências rastreadas

- **C3.3b3b:** autorização separada e token/ID de execução para
  cancelar RunOnce em curso, encerramento cooperativo e prova de zero
  contêineres one-off antes de ativar ON.
- **Observabilidade durável:** confirmar fonte externa para eventos
  produzidos com Console fechada e separar log efêmero de trilha
  institucional de auditoria.
- **Implantação HML/PROD:** identidade corporativa, segurança
  ambiente, scheduler e aceites próprios ainda não decorrem desses
  testes de CI.

Referências: [manual integrado](Manual_Sistema_Consolidado_20261009.md),
[DT-18](DT18_Servicos_Independentes_Console_DEV.md),
[DT-19](DT19_Console_Acoes_Workers.md),
[DT-20](DT20_Supervisao_Opt_In_Workers.md),
[DT-21](DT21_Testes_Resiliencia_Workers.md),
[runbook de testes](Runbook_Testes_Tecnicos.md).
