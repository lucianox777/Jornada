[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ConfigPath,
    [Parameter(Mandatory = $true)]
    [string]$PayloadRoot,
    [switch]$ValidateOnly,
    [switch]$PlanOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RequiredProperty($Object, [string]$Name) {
    $property = $Object.PSObject.Properties[$Name]
    if (-not $property -or $null -eq $property.Value) { throw "Configuração obrigatória ausente: $Name" }
    return $property.Value
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-IsWindowsServer {
    $caption = (Get-CimInstance Win32_OperatingSystem).Caption
    return $caption -like '*Windows Server*'
}

function Convert-ObjectToHashtable($Object) {
    $result = @{}
    if ($null -eq $Object) { return $result }
    foreach ($property in $Object.PSObject.Properties) {
        $result[$property.Name] = [string]$property.Value
    }
    return $result
}

function Merge-Hashtable([hashtable]$Base, $Overlay) {
    $copy = @{}
    foreach ($key in $Base.Keys) { $copy[$key] = $Base[$key] }
    if ($null -ne $Overlay) {
        foreach ($property in $Overlay.PSObject.Properties) {
            $copy[$property.Name] = [string]$property.Value
        }
    }
    return $copy
}

function Test-Clock([string]$Value) {
    $parsed = [datetime]::MinValue
    return [datetime]::TryParseExact($Value, 'HH:mm', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsed)
}

function Get-ComponentRelativePath([string]$Component) {
    switch ($Component) {
        'Api' { return 'apps\Jornada.Api\Jornada.Api.exe' }
        'ResultadoApi' { return 'apps\Jornada.Resultado.Api\Jornada.Resultado.Api.exe' }
        'Processor' { return 'apps\Jornada.Processor.Worker\Jornada.Processor.Worker.exe' }
        'OperationsMaintenance' { return 'apps\Jornada.Operations.Maintenance.Worker\Jornada.Operations.Maintenance.Worker.exe' }
        'BronzeMaintenance' { return 'apps\Jornada.Bronze.Maintenance.Worker\Jornada.Bronze.Maintenance.Worker.exe' }
        'LinkageParameters' { return 'apps\Jornada.Linkage.Parameters.Worker\Jornada.Linkage.Parameters.Worker.exe' }
        'LinkageRunner' { return 'apps\Jornada.Linkage.Runner\Jornada.Linkage.Runner.exe' }
        'Integrator' { return 'clients\Jornada.Integrador\Jornada.Integrador.CSharp.exe' }
        default { throw "Componente desconhecido no agendamento: $Component" }
    }
}

function Assert-Configuration($Config, [string]$ResolvedPayloadRoot) {
    $installRoot = [string](Get-RequiredProperty $Config 'installationRoot')
    $dataRoot = [string](Get-RequiredProperty $Config 'dataRoot')
    if (-not [IO.Path]::IsPathRooted($installRoot)) { throw 'installationRoot deve ser absoluto.' }
    if (-not [IO.Path]::IsPathRooted($dataRoot)) { throw 'dataRoot deve ser absoluto.' }

    $null = Get-RequiredProperty $Config 'dotnet'
    $docker = Get-RequiredProperty $Config 'docker'
    if ([string]$docker.mode -notin @('None','Existing','MobyWindows')) {
        throw 'docker.mode deve ser None, Existing ou MobyWindows.'
    }

    $sql = Get-RequiredProperty $Config 'sql'
    if ([string]$sql.mode -notin @('Existing','External','InstallFromMedia')) {
        throw 'sql.mode deve ser Existing, External ou InstallFromMedia.'
    }
    if ([string]::IsNullOrWhiteSpace([string]$sql.connectionString)) { throw 'sql.connectionString é obrigatório.' }
    if ([string]$sql.mode -eq 'InstallFromMedia') {
        if ([string]$sql.edition -notin @('Standard','Enterprise')) {
            throw 'Produção aceita somente SQL Server Standard ou Enterprise em InstallFromMedia.'
        }
        if ([string]::IsNullOrWhiteSpace([string]$sql.setupExe)) { throw 'sql.setupExe é obrigatório em InstallFromMedia.' }
        if (-not $ValidateOnly -and -not (Test-Path -LiteralPath ([string]$sql.setupExe))) {
            throw "setup.exe do SQL Server não encontrado: $($sql.setupExe)"
        }
        if (-not $sql.sqlSysAdminAccounts -or @($sql.sqlSysAdminAccounts).Count -eq 0) {
            throw 'sql.sqlSysAdminAccounts deve conter ao menos um grupo/conta em InstallFromMedia.'
        }
    }

    $http = Get-RequiredProperty $Config 'http'
    foreach ($name in @('apiUrls','resultadoUrls','jornadaApiBaseUrl')) {
        if ([string]::IsNullOrWhiteSpace([string]$http.$name)) { throw "http.$name é obrigatório." }
    }

    $taskAccount = Get-RequiredProperty $Config 'taskAccount'
    if ([string]::IsNullOrWhiteSpace([string]$taskAccount.user)) { throw 'taskAccount.user é obrigatório.' }

    $tasks = @(Get-RequiredProperty $Config 'tasks')
    $names = @{}
    foreach ($task in $tasks) {
        if ([string]::IsNullOrWhiteSpace([string]$task.name)) { throw 'Toda tarefa deve ter name.' }
        if ($names.ContainsKey([string]$task.name)) { throw "Nome de tarefa duplicado: $($task.name)" }
        $names[[string]$task.name] = $true
        $null = Get-ComponentRelativePath ([string]$task.component)
        $trigger = Get-RequiredProperty $task 'trigger'
        if ([string]$trigger.type -notin @('AtStartup','Daily','Weekly')) { throw "Trigger inválido em $($task.name)." }
        if ([string]$trigger.type -in @('Daily','Weekly')) {
            if (-not (Test-Clock ([string]$trigger.at))) { throw "Horário HH:mm inválido em $($task.name)." }
        }
        if ([string]$trigger.type -eq 'Weekly' -and (-not $trigger.days -or @($trigger.days).Count -eq 0)) {
            throw "Tarefa semanal sem dias: $($task.name)."
        }
    }

    $integrator = Get-RequiredProperty $Config 'integrator'
    if ([bool]$integrator.enabled) {
        if ([string]::IsNullOrWhiteSpace([string]$integrator.gestor)) { throw 'integrator.gestor é obrigatório.' }
        if ([string]::IsNullOrWhiteSpace([string]$integrator.accessKey -or [string]$integrator.accessKey -eq 'CHANGE_ME')) {
            throw 'integrator.accessKey deve ser configurada quando integrator.enabled=true.'
        }
        if ([string]::IsNullOrWhiteSpace([string]$integrator.endpoints.envio) -or [string]::IsNullOrWhiteSpace([string]$integrator.endpoints.resultado)) {
            throw 'Endpoints do integrador são obrigatórios.'
        }
        if (-not ([string]$integrator.endpoints.resultado).Contains('{nomeArquivo}')) {
            throw 'integrator.endpoints.resultado deve conter {nomeArquivo}.'
        }
    }

    $requiredPayload = @(
        'apps\Jornada.Api\Jornada.Api.exe',
        'apps\Jornada.Resultado.Api\Jornada.Resultado.Api.exe',
        'apps\Jornada.Processor.Worker\Jornada.Processor.Worker.exe',
        'apps\Jornada.Operations.Maintenance.Worker\Jornada.Operations.Maintenance.Worker.exe',
        'apps\Jornada.Bronze.Maintenance.Worker\Jornada.Bronze.Maintenance.Worker.exe',
        'apps\Jornada.Linkage.Parameters.Worker\Jornada.Linkage.Parameters.Worker.exe',
        'apps\Jornada.Linkage.Runner\Jornada.Linkage.Runner.exe',
        'clients\Jornada.Integrador\Jornada.Integrador.CSharp.exe',
        'database\Jornada_Fase1.sql',
        'config\contracts',
        'install\windows-production\Invoke-JornadaComponent.ps1'
    )
    foreach ($relative in $requiredPayload) {
        $path = Join-Path $ResolvedPayloadRoot $relative
        if (-not (Test-Path -LiteralPath $path)) { throw "Payload incompleto: $relative" }
    }
}

function Ensure-DotNet8($DotNetConfig) {
    $runtimes = @()
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        $runtimes = @(& dotnet --list-runtimes 2>$null)
    }
    $hasCore = @($runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App 8\.' }).Count -gt 0
    $hasAsp = @($runtimes | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App 8\.' }).Count -gt 0
    if ($hasCore -and $hasAsp) {
        Write-Host '.NET 8 Runtime + ASP.NET Core Runtime: OK'
        return
    }
    if (-not [bool]$DotNetConfig.installIfMissing) {
        throw '.NET 8/AspNetCore 8 ausente e dotnet.installIfMissing=false.'
    }
    $url = [string]$DotNetConfig.hostingBundleUrl
    if (-not [Uri]::IsWellFormedUriString($url, [UriKind]::Absolute)) { throw 'dotnet.hostingBundleUrl inválida.' }
    $installer = Join-Path $env:TEMP 'jornada-dotnet-hosting-8.exe'
    Write-Host "Baixando .NET 8 Hosting Bundle: $url"
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $installer
    $process = Start-Process -FilePath $installer -ArgumentList @('/quiet','/norestart') -Wait -PassThru
    if ($process.ExitCode -notin @(0,3010,1641)) { throw "Instalação do .NET falhou. ExitCode=$($process.ExitCode)" }
    Remove-Item -Force $installer -ErrorAction SilentlyContinue
}

function Ensure-Docker($DockerConfig) {
    switch ([string]$DockerConfig.mode) {
        'None' {
            Write-Host 'Docker: não solicitado para o runtime de produção.'
        }
        'Existing' {
            if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'docker.mode=Existing, mas docker.exe não foi encontrado.' }
            Write-Host 'Docker existente: OK'
        }
        'MobyWindows' {
            if (-not (Test-IsWindowsServer)) { throw 'MobyWindows no instalador de produção exige Windows Server.' }
            $url = [string]$DockerConfig.mobyInstallScriptUrl
            if (-not [Uri]::IsWellFormedUriString($url, [UriKind]::Absolute)) { throw 'docker.mobyInstallScriptUrl inválida.' }
            $script = Join-Path $env:TEMP 'jornada-install-docker-ce.ps1'
            Write-Host 'Instalando Docker CE/Moby para contêineres Windows conforme script oficial Microsoft.'
            Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $script
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script
            if ($LASTEXITCODE -ne 0) { throw "Instalação Moby falhou. ExitCode=$LASTEXITCODE" }
            Remove-Item -Force $script -ErrorAction SilentlyContinue
        }
    }
}

function Install-SqlServerFromMedia($SqlConfig) {
    $setup = (Resolve-Path -LiteralPath ([string]$SqlConfig.setupExe)).Path
    $accounts = @($SqlConfig.sqlSysAdminAccounts | ForEach-Object { '"' + [string]$_ + '"' })
    $arguments = @(
        '/Q', '/ACTION=Install', '/FEATURES=SQLENGINE',
        "/INSTANCENAME=$($SqlConfig.instanceName)",
        '/TCPENABLED=1', '/IACCEPTSQLSERVERLICENSETERMS', '/UPDATEENABLED=TRUE',
        "/SQLSYSADMINACCOUNTS=$($accounts -join ' ')"
    )
    $optional = @{
        'SQLUSERDBDIR' = [string]$SqlConfig.userDataDirectory
        'SQLUSERDBLOGDIR' = [string]$SqlConfig.userLogDirectory
        'SQLTEMPDBDIR' = [string]$SqlConfig.tempDbDirectory
        'SQLBACKUPDIR' = [string]$SqlConfig.backupDirectory
    }
    foreach ($key in $optional.Keys) {
        if (-not [string]::IsNullOrWhiteSpace($optional[$key])) {
            New-Item -ItemType Directory -Force -Path $optional[$key] | Out-Null
            $arguments += "/$key=$($optional[$key])"
        }
    }
    $pidVariable = [string]$SqlConfig.productKeyEnvironmentVariable
    if (-not [string]::IsNullOrWhiteSpace($pidVariable)) {
        $pid = [Environment]::GetEnvironmentVariable($pidVariable)
        if (-not [string]::IsNullOrWhiteSpace($pid)) { $arguments += "/PID=$pid" }
    }
    Write-Host "Instalando SQL Server $($SqlConfig.edition) a partir de mídia licenciada."
    $process = Start-Process -FilePath $setup -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -notin @(0,3010)) { throw "SQL Server Setup falhou. ExitCode=$($process.ExitCode)" }
}

function Invoke-SqlBatch([string]$ConnectionString, [string]$Sql) {
    $connection = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 0
        $command.CommandText = $Sql
        $null = $command.ExecuteNonQuery()
    }
    finally {
        if ($connection.State -ne [Data.ConnectionState]::Closed) { $connection.Close() }
        $connection.Dispose()
    }
}

function Initialize-JornadaDatabase($SqlConfig, [string]$DdlPath) {
    if (-not [bool]$SqlConfig.initializeDatabase) { return }
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder([string]$SqlConfig.connectionString)
    $databaseName = $builder.InitialCatalog
    if ([string]::IsNullOrWhiteSpace($databaseName)) { throw 'sql.connectionString deve declarar Database/Initial Catalog para inicialização.' }
    $masterBuilder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder([string]$SqlConfig.connectionString)
    $masterBuilder.InitialCatalog = 'master'
    $safeDatabase = $databaseName.Replace(']', ']]')
    Invoke-SqlBatch $masterBuilder.ConnectionString "IF DB_ID(N'$($databaseName.Replace("'", "''"))') IS NULL CREATE DATABASE [$safeDatabase];"

    $ddl = Get-Content -Raw -Encoding UTF8 $DdlPath
    $batches = [Text.RegularExpressions.Regex]::Split($ddl, '(?im)^\s*GO\s*(?:--.*)?$')
    foreach ($batch in $batches) {
        if (-not [string]::IsNullOrWhiteSpace($batch)) { Invoke-SqlBatch ([string]$SqlConfig.connectionString) $batch }
    }
    Write-Host 'DDL Jornada_Fase1.sql aplicado com sucesso.'
}

function Set-SecureAcl([string]$Path, [string]$TaskUser) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    & icacls.exe $Path '/inheritance:r' '/grant:r' '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' /T /C | Out-Null
    if (-not [string]::Equals($TaskUser, 'SYSTEM', [StringComparison]::OrdinalIgnoreCase)) {
        & icacls.exe $Path '/grant:r' "$TaskUser`:(OI)(CI)RX" /T /C | Out-Null
    }
}

function Install-Payload([string]$ResolvedPayloadRoot, [string]$InstallRoot) {
    New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
    foreach ($folder in @('apps','clients','config','database')) {
        $source = Join-Path $ResolvedPayloadRoot $folder
        $destination = Join-Path $InstallRoot $folder
        if (Test-Path -LiteralPath $destination) { Remove-Item -Recurse -Force $destination }
        Copy-Item -Recurse -Force -Path $source -Destination $destination
    }
    $scriptsDestination = Join-Path $InstallRoot 'scripts'
    New-Item -ItemType Directory -Force -Path $scriptsDestination | Out-Null
    Copy-Item -Force -Path (Join-Path $ResolvedPayloadRoot 'install\windows-production\Invoke-JornadaComponent.ps1') -Destination $scriptsDestination
}

function Write-JsonFile([string]$Path, $Value) {
    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $Value | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -Path $Path
}

function Build-GlobalEnvironment($Config, [string]$InstallRoot, [string]$DataRoot) {
    $environment = Convert-ObjectToHashtable $Config.runtime.environment
    $environment['ConnectionStrings__Jornada'] = [string]$Config.sql.connectionString
    $environment['DOTNET_ENVIRONMENT'] = 'Production'
    $environment['ASPNETCORE_ENVIRONMENT'] = 'Production'
    $environment['Contracts__RepositoryRoot'] = $InstallRoot
    $environment['BronzeStorage__Provider'] = 'FileSystem'
    $environment['BronzeStorage__RootPath'] = (Join-Path $DataRoot 'bronze')
    $environment['IngestionStaging__RootPath'] = (Join-Path $DataRoot 'staging')
    return $environment
}

function New-JornadaTrigger($TriggerConfig) {
    switch ([string]$TriggerConfig.type) {
        'AtStartup' { return New-ScheduledTaskTrigger -AtStartup }
        'Daily' {
            $time = [datetime]::ParseExact([string]$TriggerConfig.at, 'HH:mm', [Globalization.CultureInfo]::InvariantCulture)
            return New-ScheduledTaskTrigger -Daily -At $time
        }
        'Weekly' {
            $time = [datetime]::ParseExact([string]$TriggerConfig.at, 'HH:mm', [Globalization.CultureInfo]::InvariantCulture)
            return New-ScheduledTaskTrigger -Weekly -WeeksInterval 1 -DaysOfWeek @($TriggerConfig.days) -At $time
        }
        default { throw "Trigger não suportado: $($TriggerConfig.type)" }
    }
}

function Register-JornadaTask($Task, $Config, [string]$InstallRoot, [string]$DataRoot, [hashtable]$GlobalEnvironment) {
    if (-not [bool]$Task.enabled) { return }
    if ([string]$Task.component -eq 'Integrator' -and -not [bool]$Config.integrator.enabled) {
        throw "Tarefa $($Task.name) habilitada, mas integrator.enabled=false."
    }

    $relative = Get-ComponentRelativePath ([string]$Task.component)
    $executable = Join-Path $InstallRoot $relative
    if (-not (Test-Path -LiteralPath $executable)) { throw "Executável não encontrado para $($Task.name): $executable" }
    $workingDirectory = Split-Path -Parent $executable
    $environment = Merge-Hashtable $GlobalEnvironment $Task.environment
    if ([string]$Task.component -eq 'Api') { $environment['ASPNETCORE_URLS'] = [string]$Config.http.apiUrls }
    if ([string]$Task.component -eq 'ResultadoApi') {
        $environment['ASPNETCORE_URLS'] = [string]$Config.http.resultadoUrls
        $environment['JornadaApiBaseUrl'] = [string]$Config.http.jornadaApiBaseUrl
    }

    $taskConfigPath = Join-Path $InstallRoot ("config\tasks\{0}.json" -f [string]$Task.name)
    $taskConfig = [ordered]@{
        name = [string]$Task.name
        executable = $executable
        workingDirectory = $workingDirectory
        arguments = @($Task.arguments | ForEach-Object { [string]$_ })
        environment = $environment
        logDirectory = (Join-Path $DataRoot 'logs')
    }
    Write-JsonFile $taskConfigPath $taskConfig

    $runner = Join-Path $InstallRoot 'scripts\Invoke-JornadaComponent.ps1'
    $actionArguments = "-NoProfile -ExecutionPolicy Bypass -File `"$runner`" -TaskConfig `"$taskConfigPath`""
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $actionArguments -WorkingDirectory $workingDirectory
    $trigger = New-JornadaTrigger $Task.trigger
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Days 3650)

    $user = [string]$Config.taskAccount.user
    if ([string]::Equals($user, 'SYSTEM', [StringComparison]::OrdinalIgnoreCase)) {
        Register-ScheduledTask -TaskName ([string]$Task.name) -Action $action -Trigger $trigger -Settings $settings -User 'SYSTEM' -RunLevel Highest -Force | Out-Null
    }
    else {
        $variable = [string]$Config.taskAccount.passwordEnvironmentVariable
        if ([string]::IsNullOrWhiteSpace($variable)) { throw 'Conta de tarefa diferente de SYSTEM exige taskAccount.passwordEnvironmentVariable.' }
        $password = [Environment]::GetEnvironmentVariable($variable)
        if ([string]::IsNullOrWhiteSpace($password)) { throw "Variável de senha não definida: $variable" }
        Register-ScheduledTask -TaskName ([string]$Task.name) -Action $action -Trigger $trigger -Settings $settings -User $user -Password $password -RunLevel Highest -Force | Out-Null
    }
    Write-Host "Tarefa registrada: $($Task.name)"
}

$configFullPath = (Resolve-Path -LiteralPath $ConfigPath).Path
$payloadFullPath = (Resolve-Path -LiteralPath $PayloadRoot).Path
$config = Get-Content -Raw -Encoding UTF8 $configFullPath | ConvertFrom-Json
Assert-Configuration $config $payloadFullPath

Write-Host 'Configuração e payload: OK'
Write-Host "Instalação: $($config.installationRoot)"
Write-Host "Dados: $($config.dataRoot)"
Write-Host "SQL mode: $($config.sql.mode)"
Write-Host "Docker mode: $($config.docker.mode)"
Write-Host "Tarefas habilitadas: $(@($config.tasks | Where-Object { [bool]$_.enabled }).Count)"

if ($ValidateOnly -or $PlanOnly) {
    if (-not (Test-IsWindowsServer)) { Write-Warning 'Validação executada fora de Windows Server; produção deve usar Windows Server homologado.' }
    if ($PlanOnly) { Write-Host 'PLAN ONLY: nenhuma alteração foi aplicada.' }
    else { Write-Host 'VALIDATE ONLY: nenhuma alteração foi aplicada.' }
    exit 0
}

if (-not (Test-IsAdministrator)) { throw 'Execute o instalador em PowerShell elevado (Administrador).' }
if (-not (Test-IsWindowsServer)) { throw 'O instalador de produção exige Windows Server.' }

Ensure-DotNet8 $config.dotnet
Ensure-Docker $config.docker
if ([string]$config.sql.mode -eq 'InstallFromMedia') { Install-SqlServerFromMedia $config.sql }

$installRoot = [IO.Path]::GetFullPath([string]$config.installationRoot)
$dataRoot = [IO.Path]::GetFullPath([string]$config.dataRoot)
foreach ($path in @($dataRoot, (Join-Path $dataRoot 'bronze'), (Join-Path $dataRoot 'staging'), (Join-Path $dataRoot 'logs'))) {
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}
Install-Payload $payloadFullPath $installRoot
Initialize-JornadaDatabase $config.sql (Join-Path $installRoot 'database\Jornada_Fase1.sql')

if ([bool]$config.integrator.enabled) {
    $integratorPath = Join-Path $installRoot 'clients\Jornada.Integrador\integrador.config.json'
    $integratorConfig = [ordered]@{
        gestor = [string]$config.integrator.gestor
        accessKey = [string]$config.integrator.accessKey
        endpoints = [ordered]@{
            envio = [string]$config.integrator.endpoints.envio
            resultado = [string]$config.integrator.endpoints.resultado
        }
        polling = [ordered]@{
            intervalSeconds = [int]$config.integrator.polling.intervalSeconds
            timeoutSeconds = [int]$config.integrator.polling.timeoutSeconds
        }
        diretorioSaida = [string]$config.integrator.diretorioSaida
    }
    Write-JsonFile $integratorPath $integratorConfig
}

$globalEnvironment = Build-GlobalEnvironment $config $installRoot $dataRoot
foreach ($task in @($config.tasks)) { Register-JornadaTask $task $config $installRoot $dataRoot $globalEnvironment }
Set-SecureAcl (Join-Path $installRoot 'config') ([string]$config.taskAccount.user)
if ([bool]$config.integrator.enabled) { Set-SecureAcl (Join-Path $installRoot 'clients\Jornada.Integrador') ([string]$config.taskAccount.user) }

Write-Host ''
Write-Host 'Instalação concluída.'
Write-Host 'IMPORTANTE: Jornada.Api permanece deny-by-default em Production enquanto o adaptador de identidade corporativa não estiver implementado/configurado.' -ForegroundColor Yellow
Write-Host 'Revise as tarefas desabilitadas de linkage/calibração e habilite somente após aprovação da cadência em HML.' -ForegroundColor Yellow
