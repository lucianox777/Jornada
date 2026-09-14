# Monitor Operacional da Jornada

## Objetivo

O Monitor Operacional responde **o que a Jornada está fazendo agora**. Ele é uma superfície somente-leitura da aplicação e não substitui o monitoramento de infraestrutura da PRODAM.

A interface é servida pela própria `Jornada.Api` em:

```text
/monitor
```

Os dados são obtidos pela API protegida:

```text
GET /api/v1/monitor/status
```

A página atualiza o snapshot a cada 5 segundos. A chave informada pelo operador é mantida apenas em `sessionStorage` e não é incorporada ao HTML nem persistida pelo monitor.

## O que é mostrado

O snapshot reúne:

- presença viva dos nós e dos componentes residentes;
- readiness de SQL Server, Bronze, Staging e identidade/autorização corporativa;
- quantidade de lotes por estado;
- lotes em validação/processamento e o respectivo lease;
- entregas recentes;
- último ciclo de manutenção da Bronze;
- últimas execuções de linkage e seus resultados agregados.

O monitor lê as tabelas operacionais canônicas para fila, processamento, manutenção e linkage. A única persistência criada especificamente para o painel é `controle.runtime_componente`, usada para heartbeat dos processos residentes.

## Heartbeat

Os processos residentes publicam presença no SQL compartilhado:

```text
Api
ResultadoApi
Processor
OperationsMaintenance
BronzeMaintenance
```

O intervalo padrão é 10 segundos. Um componente com heartbeat `RUNNING` mais antigo que 35 segundos é apresentado como offline.

A identidade do nó segue:

1. `JORNADA_NODE_ID`, quando configurado pela topologia de cluster;
2. `Environment.MachineName`, como fallback.

Assim, na instalação de dois nós o painel apresenta `NODE1` e `NODE2` sem depender do balanceador externo. A parada limpa registra `STOPPED`; em queda abrupta, o heartbeat simplesmente envelhece e o painel passa a considerar o componente offline.

A publicação é **best-effort**. Falha no mecanismo de monitoramento nunca deve derrubar API, Processor ou Maintenance.

## Jobs

Linkage e outros jobs agendados não são convertidos em processos residentes para aparecer no monitor. O estado do linkage é lido de `identidade.linkage_run`, preservando a semântica de job já adotada pela Jornada.

## Autorização

A API do monitor exige:

```text
credential type: GESTOR
scope: jornada.monitor.read
```

Credenciais `BENEFICIO` e `SERVICO` não podem receber esse scope. O perfil Development concede o scope às credenciais GESTOR sintéticas de `config/security/test-access-keys.json`.

## Limite de responsabilidade

O Monitor Operacional mostra a saúde funcional da Jornada e seu fluxo de trabalho. Métricas de host e infraestrutura permanecem responsabilidade da plataforma de infraestrutura, por exemplo:

- CPU e memória das VMs;
- saúde física/virtual do disco;
- latência de rede;
- disponibilidade do hipervisor;
- telemetria detalhada de SQL Server;
- saúde do NAS/SAN;
- balanceador/VIP externo.

A Jornada pode usar readiness para indicar que uma dependência não está utilizável, mas não replica a ferramenta corporativa de observabilidade da infraestrutura.

## Instalação

A migração `database/migrations/20260914_Operational_Monitor.sql` é idempotente e faz parte do baseline candidato v3.70. O bundle Windows concatena a migração ao DDL único consumido pelo instalador de produção.

A API só fica `ready` quando `controle.runtime_componente` existe, evitando ativar uma versão da aplicação cujo schema de monitor ainda não foi aplicado.
