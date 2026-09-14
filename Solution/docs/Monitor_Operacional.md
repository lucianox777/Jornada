# Monitor Operacional da Jornada

## Objetivo

O Monitor Operacional responde **o que a Jornada está fazendo agora** e se os nós pertencem à mesma implantação técnica. Ele é uma superfície somente-leitura da aplicação e não substitui o monitoramento de infraestrutura da PRODAM.

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

- presença viva dos nós e dos cinco componentes residentes;
- readiness de SQL Server, Bronze, Staging e identidade/autorização corporativa;
- **saúde do bundle de configuração**;
- quantidade de lotes por estado;
- lotes em validação/processamento e o respectivo lease;
- entregas recentes;
- último ciclo de manutenção da Bronze;
- últimas execuções de linkage e seus resultados agregados.

O monitor lê as tabelas operacionais canônicas para fila, processamento, manutenção e linkage. A única persistência criada especificamente para o painel é `controle.runtime_componente`, usada para heartbeat dos processos residentes.

## Saúde do bundle de configuração

`config/release/configuration-bundle.json` define a identidade técnica do conjunto instalado, incluindo:

```text
bundleVersion
solutionSchema
clusterConfigSchemaVersion
```

O instalador cluster injeta nos processos residentes:

```text
JORNADA_CONFIGURATION_BUNDLE_VERSION
JORNADA_SOLUTION_SCHEMA_VERSION
```

Cada heartbeat persiste esses dois valores. O monitor compara três fontes independentes:

1. bundle/schema esperado pela própria API que está servindo o monitor;
2. bundle/schema reportado pelos componentes `ONLINE` de NODE1/NODE2;
3. `Jornada.SolutionSchema` efetivamente gravado como extended property no SQL Server.

Estados:

- `OK` — todos os processos online reportam o bundle esperado e o SQL está no `SolutionSchema` esperado;
- `DIVERGENTE` — ao menos um nó/componente ou o SQL pertence a outra versão; o estado geral do monitor vira `FALHA`;
- `NAO_CONFIGURADO` — as variáveis de identidade do bundle não foram fornecidas; usado para compatibilidade de desenvolvimento antes da instalação cluster versionada e não é tratado como divergência explícita.

Essa checagem detecta, por exemplo, NODE1 atualizado e NODE2 ainda executando configuração/binários de outra implantação, mesmo que os dois processos estejam vivos.

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

Assim, na instalação de dois nós o painel apresenta `NODE1` e `NODE2` sem depender do balanceador externo. A parada limpa registra `STOPPED`; em queda abrupta, o heartbeat envelhece e o painel passa a considerar o componente offline.

A publicação é **best-effort**. Falha no mecanismo de monitoramento nunca deve derrubar API, Processor ou Maintenance.

## Execuções únicas

Calibrador, Linkage Runner, Bronze Verify e Linkage Evaluation não são processos residentes do monitor.

O estado de Linkage é lido de `identidade.linkage_run`. Na implantação cluster, Calibrador e Runner ficam instalados na VM como scripts/ferramentas manuais, sem scheduler automático. O wrapper de Linkage exige exatamente um modelo `ATIVO`, e o wrapper de calibração executa `GENERATE_DRAFT -> VALIDATE -> ACTIVATE` antes de liberar o Runner.

## Onde o monitor fica

O monitor não ganha VM/container próprio. Ele faz parte de `Jornada.Api` e, portanto, existe nos dois nós:

```text
http://NODE1:5080/monitor
http://NODE2:5080/monitor
```

Com balanceador/VIP externo, o caminho preferencial é `/monitor` no endereço do balanceador. Como o estado é lido do SQL compartilhado, qualquer instância da API apresenta a visão do cluster inteiro.

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
- saúde física do NAS/SAN;
- balanceador/VIP externo.

A Jornada pode usar readiness e seus próprios heartbeats para indicar que uma dependência não está utilizável, mas não replica a ferramenta corporativa de observabilidade de infraestrutura.

## Instalação

A migração `database/migrations/20260914_Operational_Monitor.sql` é idempotente e faz parte do baseline candidato v3.70. A tabela armazena também `configuration_bundle_version` e `solution_schema_expected`.

O bundle Windows contém `config/release/configuration-bundle.json` e `MANIFEST.sha256`. O primeiro fornece a identidade lógica usada em runtime; o segundo garante integridade de cada arquivo do payload durante a validação do bundle.
