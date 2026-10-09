# DT-19 — interface mínima de controle dos workers (Console DEV)

**Revisão de estado — 09/10/2026:** o desenho de 08/10 foi **implementado
no código da Console DEV e integrado à `master`**: #851 (estado
real), #852 (toggle), #853 (três RunOnce), #855 (Parar individual) e
#856 (painel e estados), com regressões em CI. Os dois botões
`Executar uma vez` e `Parar processo` e o indicador automático
constam em `src/Jornada.DevConsole/Page.cs`; **não** existe botão
separado de status/início residente. Não interpretar menções
históricas abaixo a “catálogo atual sem os dois Maintenances” como
estado da `master` pós-#853. O log é de **sessão**; telemetria durável
fora da Console exige evidência separada. O cancelamento
**explicitamente confirmado** de RunOnce ativo permanece pendente,
conforme [C3.3b3](C3_3b3_Confirmacao_Cancelamento_RunOnce.md).
Funcionalidade restrita ao GitHub CI DEV descartável `JornadaE2E`,
não implantada/autorizada em HML/PROD.
Ver [guia atual](Console_DEV_Supervisao_Atual.md).

## Interface desejada

**UM controle global** `Supervisão automática: DESATIVADA / ATIVADA`
para os três workers: Processor, Operations Maintenance e Bronze
Maintenance. Inicia OFF no **novo perfil DEV descartável**.

**Cada worker mantém os botões verticalmente empilhados:**

```text
SUPERVISÃO AUTOMÁTICA:  [ DESATIVADA | ATIVADA ]   (global)

Processor Worker         ● ATIVO   PID 123  (indicador automático)
[ ▶ Executar uma vez ]
[ ■ Parar processo ]

Operations Maintenance   ○ PARADO  (indicador automático)
[ ▶ Executar uma vez ]
[ ■ Parar processo ]

Bronze Maintenance       ↻ REINICIANDO  (indicador automático)
[ ▶ Executar uma vez ]
[ ■ Parar processo ]

[ 📋 Log da sessão ]      (já existe no cabeçalho da Console)
```

**NÃO CRIAR** botões `Status do processo`, `Iniciar processo`,
`Iniciar contínuo`, `Desligar processo`, `Matar processo`,
`Simular falha` ou `Parar contínuo`. O único start contínuo é
a transição ON do supervisor global.

## Habilitação

| Controle | Supervisor OFF (padrão) | Supervisor ON |
|---|---|---|
| **Executar uma vez (RunOnce)** | Habilitado para os três workers sujeitos aos pré-requisitos reais e exclusão de execução já em andamento. | Desabilitado no navegador e **rejeitado também no backend**. |
| **Parar processo** | Desabilitado sem residente; RunOnce ativos são encerrados **somente na troca de modo** com confirmação explícita. | Habilitado **somente quando o PID real daquele trabalhador está vivo**. SIGKILL nele; supervisor reinicia automaticamente **apenas esse worker**. Durante reinício, desabilitado. |
| **Indicador automático de estado** | PARADO ou RUN_ONCE (quando finito ativo). | INICIANDO/ATIVO/REINICIANDO/RECUPERANDO/ERRO conforme evidência. |

Mostrar, sem clique, estado compacto (`ATIVO`, `PARADO`,
`REINICIANDO`, `ERRO`) e opcionalmente PID/último heartbeat.
O estado precisa ser **consultado periodicamente no backend** por
observação real do processo e heartbeat. A habilitação de `Parar
processo` ajuda a indicar que o processo subiu, mas não prova
saúde ou recuperação de dados.

## Aproveitamento do log existente — NÃO do log como status real

A Console já oferece `Log da sessão`, histórico de execuções e
saída de terminal. Reutilizar esta interface, incluindo por worker
eventos estruturados de:
- ativação/desativação do supervisor e mudanças de modo;
- término de RunOnce, PID morto, PID novo e contagem de reinícios;
- última saída, erros, heartbeat/alertas e evidências de recuperação
  quando disponíveis; distinguir **EXECUTÁVEL REINICIADO** de
  **TRABALHO RECUPERADO**.

**Limite encontrado no código:** o log existente é de **sessão da
Console**, não é atualmente fonte durável de processos de longa
duração; eventos do supervisor que ocorram sem Console ativa devem
ser recuperados de logs/estado do gerenciador externo. Não inferir
liveness apenas do último log. Um backend read-only de status
continua **necessário internamente**, embora **não exista botão
Status** no frontend.

## Preservação

- Preservar rotinas, comandos, códigos de saída e testes RunOnce
  existentes. Já há **RunOnce do Processor/Silver** na Console.
- Os dois **executáveis** de manutenção aceitam modo RunOnce, mas o
  catálogo da Console atual **não possui botões RunOnce individuais**
  de Operations Maintenance e Bronze Maintenance; será preciso
  expô-los para cumprir a regra de três RunOnce habilitados em OFF.
  Não declarar que estes botões já foram implementados.
- Novos recursos limitados a botão global, `Parar processo`,
  indicadores simples, ligação RunOnce ausente e integração no log.
  Controles ficam verticalmente empilhados, sem ações redundantes.
- Isolar transições sob lock atômico; evitar RunOnce e contínuo
  simultâneos e nunca parar NODE, APIs ou outros trabalhadores.

Ver [DT-18](DT18_Servicos_Independentes_Console_DEV.md),
[DT-20](DT20_Supervisao_Opt_In_Workers.md) e
[DT-21](DT21_Testes_Resiliencia_Workers.md).

**Proibições:** JornadaLocal, IBGE original, HML/PROD, volumes
compartilhados, Trilha 4 e reprocessamento automático de RESOLVIDOS.
