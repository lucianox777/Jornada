# Jornada — Instalador de Produção Windows

Este diretório contém o instalador versionado da Jornada para Windows Server.

## Topologia canônica

O caminho de produção é nativo no Windows:

- .NET 8 Runtime + ASP.NET Core Runtime;
- executáveis publicados da Jornada;
- SQL Server 2022 existente/nativo ou SQL externo homologado;
- Bronze em NAS compartilhado e Staging/logs locais por VM;
- cinco processos residentes iniciados em background no boot;
- ferramentas de execução única instaladas na VM, **sem agendamento automático**;
- `Jornada.Integrador.CSharp.exe` como cliente sob demanda.

A implantação cluster canônica usa dois nós simétricos. Veja `CLUSTER.md` e `Jornada.Cluster.Production.example.json`.

Docker é opcional na produção Windows. O `docker-compose.yml` da Solution é um harness local Linux e não é o runtime canônico de produção.

## Arquivos

- `Install-JornadaProduction.ps1` — instalação/upgrade host;
- `Install-JornadaCluster.ps1` — adapta o mesmo bundle a NODE1/NODE2 e aos caminhos compartilhados/locais;
- `Build-WindowsProductionBundle.ps1` — publica executáveis, ferramentas, configuração e DDL;
- `Invoke-JornadaComponent.ps1` — runner dos processos residentes registrados no Agendador;
- `Invoke-JornadaLinkageCalibration.ps1` — calibração manual `GENERATE_DRAFT -> CONFERENCIA -> VALIDATE -> ACTIVATE`, fail-closed na tolerância governada;
- `Invoke-JornadaLinkageRun.ps1` — Linkage manual com preflight de modelo ativo;
- `Jornada.Cluster.Production.example.json` — configuração cluster de produção sem segredo real;
- `Jornada.Cluster.Test.json` — mesmo schema para o harness local.

## Bundle versionado

Em uma máquina de build:

```powershell
.\install\windows-production\Build-WindowsProductionBundle.ps1
```

O bundle contém:

- APIs e workers residentes;
- Parameters Worker e Linkage Runner;
- `Jornada.Bronze.Verify`;
- `Jornada.Linkage.Evaluation`;
- `Jornada.Linkage.Conference`;
- Integrador C#;
- contratos/configurações versionadas;
- `config\release\configuration-bundle.json`;
- DDL canônico;
- OpenAPI;
- instaladores/scripts;
- `MANIFEST.sha256` cobrindo o payload.

`configuration-bundle.json` fornece uma identidade técnica pequena para o conjunto de configuração. Os perfis Test e Production declaram a mesma `configurationBundleVersion` e `solutionSchema`; o instalador cluster rejeita payload/config incompatíveis. O monitor operacional usa essa identidade como uma dimensão de saúde do sistema.

## Configuração

Para produção cluster, copie `Jornada.Cluster.Production.example.json` para um arquivo fora do Git, preencha hosts/caminhos/conexão e instale o mesmo bundle nas duas VMs:

```powershell
.\Install-JornadaCluster.ps1 `
  -ConfigPath C:\Jornada-Install\Jornada.Cluster.Production.json `
  -PayloadRoot C:\Jornada-Install\bundle `
  -NodeId NODE1
```

Na outra VM, use `NODE2`.

A configuração real pode conter conexão SQL e outros valores sensíveis; mantenha ACL restrita e não versione segredos.

## Processos em background

A configuração cluster admite somente os cinco processos residentes abaixo, todos `AtStartup`:

- `Jornada-Api`;
- `Jornada-ResultadoApi`;
- `Jornada-Processor`;
- `Jornada-OperationsMaintenance`;
- `Jornada-BronzeMaintenance`.

No Windows Server eles ficam em background como tarefas do Agendador do Windows. O Agendador é usado aqui como mecanismo de serviço/supervisão no boot; não como scheduler de Linkage/Calibrador.

## Execuções únicas diretamente na VM

Depois da instalação cluster, a VM contém:

```text
C:\Program Files\Jornada\jobs\Invoke-JornadaLinkageCalibration.ps1
C:\Program Files\Jornada\jobs\Invoke-JornadaLinkageRun.ps1
C:\Program Files\Jornada\tools\Jornada.Bronze.Verify\Jornada.Bronze.Verify.exe
C:\Program Files\Jornada\tools\Jornada.Linkage.Evaluation\Jornada.Linkage.Evaluation.exe
```

A convenção operacional é executar no NODE2, mas o bundle é igual nos dois nós.

Calibrar/promover modelo:

```powershell
cd 'C:\Program Files\Jornada'
.\jobs\Invoke-JornadaLinkageCalibration.ps1
```

Executar Linkage:

```powershell
.\jobs\Invoke-JornadaLinkageRun.ps1
```

O segundo comando recusa iniciar se o banco não tiver exatamente um modelo `ATIVO`. Isso complementa a própria proteção interna do Runner, que já exige modelo ativo.

`Jornada.Linkage.Evaluation` é ferramenta DEV/HML somente-leitura; não é um daemon e não publica identidade. `Jornada.Linkage.Conference` é execução governada separada que compara Core × Evaluation e grava somente evidência agregada. O wrapper de calibração sempre a executa antes de `VALIDATE`; com a configuração técnica `FROZEN` em `V1_2026-09-26` (LLR 0,01), a promoção exige conferência efetiva `CONFORME`, fingerprint inalterado e budgets FP persistidos dentro dos limites. O congelamento do número não autoriza HML/Produção por si só.

## SQL Server

`sql.mode` aceita:

- `Existing` — SQL Server já instalado na máquina ou rede;
- `External` — SQL Server externo homologado;
- `InstallFromMedia` — instala SQL Server 2022 a partir de mídia licenciada.

Para `InstallFromMedia`, somente Standard e Enterprise são aceitos. O instalador não baixa nem incorpora licença/mídia SQL.

O SQL Server é o banco operacional desta implantação. Microsoft Fabric, quando usado, pertence à camada analítica e não substitui o runtime operacional. PostgreSQL não faz parte desta topologia de produção.

## Bronze, NAS e Staging

Em produção:

```text
Bronze  -> storage.bronzeRoot -> NAS compartilhado
Staging -> nodes[].stagingRoot -> disco local de cada VM
Logs    -> nodes[].logsRoot    -> disco local de cada VM
```

O dimensionamento de vCPU, RAM, SAN e NAS deve ser homologado pela infraestrutura/PRODAM. O software não fixa esses números.

## Monitor operacional

O monitor é servido pela própria `Jornada.Api`, portanto não existe um terceiro processo para ele:

```text
http://NODE1:5080/monitor
http://NODE2:5080/monitor
```

Com balanceador/VIP externo, use `/monitor` no endereço do VIP. A página lê o SQL compartilhado e apresenta uma visão do cluster inteiro.

## Validação sem alterar a máquina

```powershell
.\Install-JornadaCluster.ps1 `
  -ConfigPath .\Jornada.Cluster.Production.example.json `
  -PayloadRoot C:\Jornada-Install\bundle `
  -NodeId NODE1 `
  -ValidateOnly
```

ou use `-PlanOnly`.

## Docker local

O harness Test usa quatro containers persistentes:

```text
jornada-node1
jornada-node2
sqlserver
jornada-nas
```

O NAS local é Samba/SMB e expõe a persistência Bronze. No perfil padrão, NODE1/NODE2 continuam montando o named volume diretamente para manter os testes rápidos e determinísticos; a semântica integral de um NAS SMB de produção deve ter um ensaio de fidelidade separado.

Execute:

```powershell
.\scripts\local-cluster.ps1 up
```

O comando imprime os endpoints do cluster, o monitor e os caminhos/comandos de execução única. Há também:

```powershell
.\scripts\local-cluster.ps1 calibrate
.\scripts\local-cluster.ps1 linkage
```

## Segurança e bloqueio conhecido de Produção

A configuração real e `manual-runtime.json` podem conter conexão SQL. O instalador aplica ACL restrita à configuração.

A `Jornada.Api` permanece fail-closed fora de Development enquanto o adaptador corporativo de identidade/secret store não estiver implementado e homologado. O instalador não contorna essa proteção nem habilita chaves sintéticas de Development em Produção.
