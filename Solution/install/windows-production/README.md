# Jornada — Instalador de Produção Windows

Este diretório contém o instalador versionado da Jornada para Windows Server.

## Topologia canônica

O caminho de produção deste instalador é **nativo no Windows**:

- .NET 8 Runtime + ASP.NET Core Runtime;
- executáveis publicados da Jornada;
- SQL Server 2022 nativo, SQL Server já existente ou SQL externo/Fabric;
- armazenamento Bronze/Staging em caminho absoluto e durável;
- processos contínuos e jobs registrados no **Agendador de Tarefas do Windows**;
- `Jornada.Integrador.CSharp.exe` como cliente padrão para envio/consulta.

Docker é opcional. Em Windows Server, `MobyWindows` instala runtime para **contêineres Windows**. Ele não é usado para executar o `docker-compose.yml` local da Solution, porque esse compose usa SQL Server Linux. Docker Desktop não faz parte do instalador de Windows Server.

## Arquivos

- `Install-JornadaProduction.ps1` — instalação/upgrade idempotente da aplicação;
- `Build-WindowsProductionBundle.ps1` — publica os executáveis e monta o payload de produção;
- `Invoke-JornadaComponent.ps1` — runner usado pelas tarefas do Windows;
- `Jornada.Production.example.json` — modelo de configuração sem segredo real.

## Preparar o bundle

Em uma máquina de build com o SDK fixado pela Solution:

```powershell
.\install\windows-production\Build-WindowsProductionBundle.ps1
```

O bundle é criado em `.local\windows-production-bundle` por padrão e contém:

- APIs;
- Workers;
- Linkage Runner/Parameters Worker;
- Integrador C#;
- contratos JSON Schema;
- DDL canônico `Jornada_Fase1.sql`;
- OpenAPI;
- instalador.

O CI também gera esse bundle como artefato.

## Configuração

Copie:

```text
Jornada.Production.example.json
```

para um arquivo **fora do Git**, por exemplo:

```text
C:\Jornada-Install\Jornada.Production.json
```

A configuração real pode conter a chave do Integrador e por isso deve receber ACL restrita.

### SQL Server

`sql.mode` aceita:

- `Existing` — SQL Server já instalado na máquina ou rede;
- `External` — SQL externo, inclusive SQL Database no Microsoft Fabric;
- `InstallFromMedia` — instala SQL Server 2022 a partir de mídia licenciada.

Para `InstallFromMedia`, somente `Standard` e `Enterprise` são aceitos. Developer/Evaluation não são permitidos por este instalador de produção. O instalador não baixa nem incorpora mídia/licença SQL. A mídia deve ser fornecida pela infraestrutura e `sql.setupExe` deve apontar para `setup.exe`.

Se a mídia exigir PID, configure `sql.productKeyEnvironmentVariable` e injete a chave em variável de ambiente antes de instalar. A chave não vai para o JSON nem para o Git.

### Docker

`docker.mode` aceita:

- `None` — recomendado quando a Jornada roda nativamente;
- `Existing` — valida `docker.exe` existente;
- `MobyWindows` — instala Docker CE/Moby para contêineres Windows usando o script oficial Microsoft.

Docker não é pré-requisito do runtime nativo da Jornada.

## Validação sem alterar a máquina

```powershell
.\Install-JornadaProduction.ps1 `
  -ConfigPath C:\Jornada-Install\Jornada.Production.json `
  -PayloadRoot C:\Jornada-Install\bundle `
  -ValidateOnly
```

ou:

```powershell
.\Install-JornadaProduction.ps1 `
  -ConfigPath C:\Jornada-Install\Jornada.Production.json `
  -PayloadRoot C:\Jornada-Install\bundle `
  -PlanOnly
```

## Instalação

Execute PowerShell elevado:

```powershell
Set-ExecutionPolicy Bypass -Scope Process -Force
.\Install-JornadaProduction.ps1 `
  -ConfigPath C:\Jornada-Install\Jornada.Production.json `
  -PayloadRoot C:\Jornada-Install\bundle
```

O instalador:

1. valida sistema operacional/configuração/payload;
2. instala .NET 8 quando necessário;
3. valida ou instala Docker conforme `docker.mode`;
4. valida ou instala SQL conforme `sql.mode`;
5. cria o banco Jornada e aplica `Jornada_Fase1.sql` quando `initializeDatabase=true`;
6. copia os executáveis/contratos para `installationRoot`;
7. cria Bronze, Staging e logs em `dataRoot`;
8. gera `integrador.config.json` se o Integrador estiver habilitado;
9. protege arquivos de configuração com ACL;
10. registra as tarefas habilitadas no Agendador do Windows.

## Tarefas

O JSON de produção controla cada tarefa. Os triggers aceitos são:

- `AtStartup`;
- `Daily` + `at` (`HH:mm`);
- `Weekly` + `days` + `at`.

Os processos contínuos recomendados no mesmo host são:

- `Jornada-Api`;
- `Jornada-ResultadoApi`;
- `Jornada-Processor`;
- `Jornada-OperationsMaintenance`;
- `Jornada-BronzeMaintenance`.

O modelo inclui, inicialmente **desabilitados**, exemplos para:

- `Jornada-Linkage-GenerateDraft` — geração de parâmetros do Fellegi-Sunter;
- `Jornada-Linkage-Incremental`;
- `Jornada-Integrator-EnviarTodos` — chama `--enviar-todos` e move ZIPs enviados para `Enviados`.

As cadências desses jobs devem ser homologadas antes de `enabled=true`; o instalador não inventa frequência institucional.

## CLI do Integrador

```text
Jornada.Integrador.CSharp.exe /help
Jornada.Integrador.CSharp.exe --help
Jornada.Integrador.CSharp.exe -h
```

Operações:

```text
--enviar <arquivo.zip>
--enviar-todos
--resultado <nome-exato-do-zip.zip>
```

`--resultado` aceita somente o nome do ZIP, nunca SHA nem caminho.

## Segurança e bloqueio conhecido de Produção

A configuração real e os arquivos de tarefa podem conter conexão SQL e/ou chave do Integrador. O instalador remove herança de ACL e mantém acesso para `SYSTEM`, Administradores e, quando configurada, a conta das tarefas.

**Importante:** a `Jornada.Api` atual é fail-closed fora de `Development`: a autenticação/autorização de Produção permanece `DENY_BY_DEFAULT_PENDING_CORPORATE_IDENTITY` até a integração do mecanismo corporativo de identidade/secret store. O instalador não contorna essa proteção nem habilita chaves sintéticas de Development em Produção.

Assim, o instalador pode provisionar toda a infraestrutura e subir os processos, mas o tráfego funcional de Produção deve permanecer bloqueado até o adaptador de identidade corporativa ser implementado/homologado.
