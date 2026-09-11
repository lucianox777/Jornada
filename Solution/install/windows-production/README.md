# Jornada — Instalador de Produção Windows

Este diretório contém o instalador versionado da Jornada para Windows Server.

## Topologia canônica

O caminho de produção deste instalador é **nativo no Windows**:

- .NET 8 Runtime + ASP.NET Core Runtime;
- executáveis publicados da Jornada;
- SQL Server 2022 nativo, SQL Server já existente ou SQL externo;
- armazenamento Bronze/Staging em caminho absoluto e durável;
- processos contínuos e jobs registrados no **Agendador de Tarefas do Windows**;
- `Jornada.Integrador.CSharp.exe` como cliente padrão para envio/consulta.

Docker é opcional. Em Windows Server, `MobyWindows` instala runtime para **contêineres Windows**. Ele não é usado para executar o `docker-compose.yml` local da Solution, porque esse compose usa SQL Server Linux. Docker Desktop não faz parte do instalador de Windows Server.

## Arquivos

- `Install-Jornada.ps1` — **entrada canônica** para instalação/upgrade; fecha o banco pelo manifesto/ledger antes de registrar tarefas;
- `Install-JornadaProduction.ps1` — implementação core de infraestrutura, payload e tarefas, chamada pelo instalador canônico;
- `Invoke-JornadaMigrationLedger.ps1` — aplica o `database/migrations/manifest.txt` com SHA-256 e ledger fail-closed;
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
- `database/Jornada_Fase1.sql`, usado somente como baseline de banco ainda vazio;
- `database/Jornada_Identidade_Progressiva.sql`, fundação aditiva anterior ao manifesto;
- `database/migrations/manifest.txt` e todas as migrações normativas da linha 3.70;
- OpenAPI;
- instalador e runner do ledger.

O `MANIFEST.sha256` do bundle cobre também os scripts de migração. O CI valida que o `manifest.txt` do bundle é byte a byte o manifesto normativo da Solution.

## Configuração

Copie `Jornada.Production.example.json` para um arquivo **fora do Git**, por exemplo:

```text
C:\Jornada-Install\Jornada.Production.json
```

A configuração real pode conter a chave do Integrador e por isso deve receber ACL restrita.

### SQL Server

`sql.mode` aceita:

- `Existing` — SQL Server já instalado na máquina ou rede;
- `External` — SQL externo previamente provisionado;
- `InstallFromMedia` — instala SQL Server 2022 a partir de mídia licenciada.

Para `InstallFromMedia`, somente `Standard` e `Enterprise` são aceitos. Developer/Evaluation não são permitidos por este instalador de produção. O instalador não baixa nem incorpora mídia/licença SQL. A mídia deve ser fornecida pela infraestrutura e `sql.setupExe` deve apontar para `setup.exe`.

Se a mídia exigir PID, configure `sql.productKeyEnvironmentVariable` e injete a chave em variável de ambiente antes de instalar. A chave não vai para o JSON nem para o Git.

Quando `initializeDatabase=true`, o instalador canônico trata os estados da seguinte forma:

- banco ausente: cria o banco, aplica `Jornada_Fase1.sql`, aplica a fundação de identidade progressiva e então percorre o manifesto;
- banco vazio já existente: aplica baseline + fundação + manifesto;
- banco com baseline mas sem fundação progressiva: aplica somente a fundação e segue para o manifesto;
- banco já inicializado: **não reaplica o baseline**; executa somente o caminho de manifesto/ledger;
- estado inconsistente (ledger/marker sem baseline esperado): falha fechado e exige correção/restauração.

Em `sql.mode=External`, o banco deve existir previamente; o instalador não tenta criá-lo.

### Ledger de migrações

A ordem de upgrade é definida exclusivamente por `database/migrations/manifest.txt`. Para cada entrada, `Invoke-JornadaMigrationLedger.ps1`:

1. calcula o SHA-256 do arquivo;
2. adquire lock exclusivo transacional (`sp_getapplock`);
3. consulta `jornada.schema_migration` com `UPDLOCK/HOLDLOCK`;
4. se a migração já existe, exige o mesmo SHA-256;
5. se ainda não existe, executa seus batches e grava o ledger **na mesma transação**;
6. faz rollback completo da migração se qualquer batch ou o registro no ledger falhar.

Uma reexecução com conteúdo diferente sob o mesmo nome falha; o instalador não substitui nem corrige silenciosamente o checksum. O marcador `Jornada.SolutionSchema=3.70` só é aceito ao final com o inventário obrigatório contabilizado.

## Docker

`docker.mode` aceita:

- `None` — recomendado quando a Jornada roda nativamente;
- `Existing` — valida `docker.exe` existente;
- `MobyWindows` — instala Docker CE/Moby para contêineres Windows usando o script oficial Microsoft.

Docker não é pré-requisito do runtime nativo da Jornada.

## Validação sem alterar a máquina

```powershell
.\Install-Jornada.ps1 `
  -ConfigPath C:\Jornada-Install\Jornada.Production.json `
  -PayloadRoot C:\Jornada-Install\bundle `
  -ValidateOnly
```

ou:

```powershell
.\Install-Jornada.ps1 `
  -ConfigPath C:\Jornada-Install\Jornada.Production.json `
  -PayloadRoot C:\Jornada-Install\bundle `
  -PlanOnly
```

Com `initializeDatabase=true`, `-ValidateOnly` também valida o inventário, existência dos arquivos e hashes calculáveis das migrações, sem abrir conexão SQL.

## Instalação / upgrade

Execute PowerShell elevado:

```powershell
Set-ExecutionPolicy Bypass -Scope Process -Force
.\Install-Jornada.ps1 `
  -ConfigPath C:\Jornada-Install\Jornada.Production.json `
  -PayloadRoot C:\Jornada-Install\bundle
```

O instalador canônico:

1. valida configuração, payload e manifesto;
2. provisiona/valida runtimes, Docker e SQL com `initializeDatabase=false` e tarefas desabilitadas;
3. classifica o estado do banco sem reaplicar baseline sobre banco já inicializado;
4. aplica baseline/fundação somente quando necessários;
5. aplica/valida todas as migrações pelo ledger;
6. somente após o fechamento do schema, finaliza payload/configuração e registra as tarefas habilitadas.

Essa ordem evita publicar um novo conjunto de tarefas antes de saber que o banco chegou ao estado esperado. `Install-JornadaProduction.ps1` permanece como implementação core e compatibilidade interna; operações normais devem usar `Install-Jornada.ps1`.

## Tarefas

O JSON de produção controla cada tarefa. Os triggers aceitos são `AtStartup`, `Daily` + `at` (`HH:mm`) e `Weekly` + `days` + `at`.

Os processos contínuos recomendados no mesmo host são `Jornada-Api`, `Jornada-ResultadoApi`, `Jornada-Processor`, `Jornada-OperationsMaintenance` e `Jornada-BronzeMaintenance`.

O modelo inclui, inicialmente **desabilitados**, exemplos para `Jornada-Linkage-GenerateDraft`, `Jornada-Linkage-Incremental` e `Jornada-Integrator-EnviarTodos`. As cadências desses jobs devem ser homologadas antes de `enabled=true`; o instalador não inventa frequência institucional.

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

A configuração real e os arquivos de tarefa podem conter conexão SQL e/ou chave do Integrador. O instalador remove herança de ACL e mantém acesso para `SYSTEM`, Administradores e, quando configurada, a conta das tarefas. Arquivos temporários usados na orquestração do instalador são removidos ao final da execução.

A `Jornada.Api` continua fail-closed fora de `Development`: a autenticação/autorização de Produção permanece `DENY_BY_DEFAULT_PENDING_CORPORATE_IDENTITY` até a integração do mecanismo corporativo de identidade/secret store. O instalador não contorna essa proteção nem habilita chaves sintéticas de Development em Produção.
