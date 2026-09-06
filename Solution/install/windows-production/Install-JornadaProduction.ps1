[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$ConfigPath,
    [Parameter(Mandatory = $true)] [string]$PayloadRoot,
    [switch]$ValidateOnly,
    [switch]$PlanOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require-Property($Object, [string]$Name) {
    $property = $Object.PSObject.Properties[$Name]
    if (-not $property -or $null -eq $property.Value) { throw "Configuração obrigatória ausente: $Name" }
    return $property.Value
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-WindowsServer {
    return (Get-CimInstance Win32_OperatingSystem).Caption -like '*Windows Server*'
}

function To-EnvironmentTable($Object) {
    $result = @{}
    if ($null -eq $Object) { return $result }
    foreach ($property in $Object.PSObject.Properties) { $result[$property.Name] = [string]$property.Value }
    return $result
}

function Merge-Environment([hashtable]$Base, $Overlay) {
    $result = @{}
    foreach ($key in $Base.Keys) { $result[$key] = $Base[$key] }
    if ($null -ne $Overlay) {
        foreach ($property in $Overlay.PSObject.Properties) { $result[$property.Name] = [string]$property.Value }
    }
    return $result
}

function Test-HhMm([string]$Value) {
    $parsed = [datetime]::MinValue
    return [datetime]::TryParseExact($Value, 'HH:mm', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsed)
}

function Get-ComponentPath([string]$Component) {
    switch ($Component) {
        'Api' { 'apps\Jornada.Api\Jornada.Api.exe'; break }
        'ResultadoApi' { 'apps\Jornada.Resultado.Api\Jornada.Resultado.Api.exe'; break }
        'Processor' { 'apps\Jornada.Processor.Worker\Jornada.Processor.Worker.exe'; break }
        'OperationsMaintenance' { 'apps\Jornada.Operations.Maintenance.Worker\Jornada.Operations.Maintenance.Worker.exe'; break }
        'BronzeMaintenance' { 'apps\Jornada.Bronze.Maintenance.Worker\Jornada.Bronze.Maintenance.Worker.exe'; break }
        'LinkageParameters' { 'apps\Jornada.Linkage.Parameters.Worker\Jornada.Linkage.Parameters.Worker.exe'; break }
        'LinkageRunner' { 'apps\Jornada.Linkage.Runner\Jornada.Linkage.Runner.exe'; break }
        'Integrator' { 'clients\Jornada.Integrador\Jornada.Integrador.CSharp.exe'; break }
        default { throw "Componente desconhecido: $Component" }
    }
}

function Assert-Config($Config, [string]$ResolvedPayloadRoot) {
    $installRoot = [string](Require-Property $Config 'installationRoot')
    $dataRoot = [string](Require-Property $Config 'dataRoot')
    if (-not [IO.Path]::IsPathRooted($installRoot)) { throw 'installationRoot deve ser absoluto.' }
    if (-not [IO.Path]::IsPathRooted($dataRoot)) { throw 'dataRoot deve ser absoluto.' }

    $dotnet = Require-Property $Config 'dotnet'
    if ([bool]$dotnet.installIfMissing -and -not [Uri]::IsWellFormedUriString([string]$dotnet.hostingBundleUrl, [UriKind]::Absolute)) {
        throw 'dotnet.hostingBundleUrl inválida.'
    }

    $docker = Require-Property $Config 'docker'
    if ([string]$docker.mode -notin @('None','Existing','MobyWindows')) { throw 'docker.mode deve ser None, Existing ou MobyWindows.' }
    if ([string]$docker.mode -eq 'MobyWindows') {
        if (-not [Uri]::IsWellFormedUriString([string]$docker.mobyInstallScriptUrl, [UriKind]::Absolute)) { throw 'docker.mobyInstallScriptUrl inválida.' }
        $expectedHash = [string]$docker.mobyInstallScriptSha256
        if ($expectedHash -notmatch '^[a-fA-F0-9]{64}$') { throw 'docker.mobyInstallScriptSha256 deve conter o SHA-256 homologado do script Moby.' }
    }

    $sql = Require-Property $Config 'sql'
    if ([string]$sql.mode -notin @('Existing','External','InstallFromMedia')) { throw 'sql.mode deve ser Existing, External ou InstallFromMedia.' }
    if ([string]::IsNullOrWhiteSpace([string]$sql.connectionString)) { throw 'sql.connectionString é obrigatório.' }
    if ([string]$sql.connectionString -match '(?i)Encrypt\s*=\s*false|TrustServerCertificate\s*=\s*true') {
        throw 'A connection string de produção não pode desabilitar TLS nem confiar cegamente no certificado.'
    }
    if ([string]$sql.mode -eq 'InstallFromMedia') {
        if ([string]$sql.edition -notin @('Standard','Enterprise')) { throw 'InstallFromMedia aceita somente SQL Server Standard ou Enterprise.' }
        if ([string]::IsNullOrWhiteSpace([string]$sql.setupExe)) { throw 'sql.setupExe é obrigatório em InstallFromMedia.' }
        if (-not ($ValidateOnly -or $PlanOnly) -and -not (Test-Path -LiteralPath ([string]$sql.setupExe))) { throw "setup.exe não encontrado: $($sql.setupExe)" }
        if (-not $sql.sqlSysAdminAccounts -or @($sql.sqlSysAdminAccounts).Count -eq 0) { throw 'sql.sqlSysAdminAccounts deve conter ao menos uma conta/grupo.' }
    }

    $http = Require-Property $Config 'http'
    foreach ($name in @('apiUrls','resultadoUrls','jornadaApiBaseUrl')) {
        if ([string]::IsNullOrWhiteSpace([string]$http.$name)) { throw "http.$name é obrigatório." }
    }

    $taskAccount = Require-Property $Config 'taskAccount'
    if ([string]::IsNullOrWhiteSpace([string]$taskAccount.user)) { throw 'taskAccount.user é obrigatório.' }

    $names = @{}
    foreach ($task in @(Require-Property $Config 'tasks')) {
        $taskName = [string]$task.name
        if ([string]::IsNullOrWhiteSpace($taskName)) { throw 'Toda tarefa precisa de name.' }
        if ($names.ContainsKey($taskName)) { throw "Nome de tarefa duplicado: $taskName" }
        $names[$taskName] = $true
        $null = Get-ComponentPath ([string]$task.component)
        $trigger = Require-Property $task 'trigger'
        if ([string]$trigger.type -notin @('AtStartup','Daily','Weekly')) { throw "Trigger inválido em $taskName." }
        if ([string]$trigger.type -in @('Daily','Weekly') -and -not (Test-HhMm ([string]$trigger.at))) { throw "Horário HH:mm inválido em $taskName." }
        if ([string]$trigger.type -eq 'Weekly' -and (-not $trigger.days -or @($trigger.days).Count -eq 0)) { throw "Tarefa semanal sem dias: $taskName." }
    }

    $integrator = Require-Property $Config 'integrator'
    if ([bool]$integrator.enabled) {
        $accessKeyValue = [string]$integrator.accessKey
        if ([string]::IsNullOrWhiteSpace([string]$integrator.gestor)) { throw 'integrator.gestor é obrigatório.' }
        if ([string]::IsNullOrWhiteSpace($accessKeyValue) -or [string]::Equals($accessKeyValue, 'CHANGE_ME', [StringComparison]::Ordinal)) {
            throw 'integrator.accessKey deve ser configurada quando o Integrador estiver habilitado.'
        }
        if ([string]::IsNullOrWhiteSpace([string]$integrator.endpoints.envio) -or [string]::IsNullOrWhiteSpace([string]$integrator.endpoints.resultado)) { throw 'Endpoints do Integrador são obrigatórios.' }
        if (-not ([string]$integrator.endpoints.resultado).Contains('{nomeArquivo}')) { throw 'integrator.endpoints.resultado deve conter {nomeArquivo}.' }
    }

    $required = @(
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
    foreach ($relative in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $ResolvedPayloadRoot $relative))) { throw "Payload incompleto: $relative" }
    }
}

function Ensure-DotNet8($DotNetConfig) {
    $runtimes = if (Get-Command dotnet -ErrorAction SilentlyContinue) { @(& dotnet --list-runtimes 2>$null) } else { @() }
    $core = @($runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App 8\.' }).Count -gt 0
    $asp = @($runtimes | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App 8\.' }).Count -gt 0
    if ($core -and $asp) { Write-Host '.NET 8 Runtime + ASP.NET Core Runtime: OK'; return }
    if (-not [bool]$DotNetConfig.installIfMissing) { throw '.NET 8 ausente e instalação automática desabilitada.' }

    $installer = Join-Path $env:TEMP 'jornada-dotnet-hosting-8.exe'
    Invoke-WebRequest -UseBasicParsing -Uri ([string]$DotNetConfig.hostingBundleUrl) -OutFile $installer
    $signature = Get-AuthenticodeSignature -FilePath $installer
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Subject -notmatch 'Microsoft') {
        Remove-Item -Force $installer -ErrorAction SilentlyContinue
        throw 'Hosting Bundle baixado não possui assinatura Authenticode válida da Microsoft.'
    }
    $process = Start-Process -FilePath $installer -ArgumentList @('/quiet','/norestart') -Wait -PassThru
    Remove-Item -Force $installer -ErrorAction SilentlyContinue
    if ($process.ExitCode -notin @(0,3010,1641)) { throw "Instalação .NET falhou. ExitCode=$($process.ExitCode)" }
}

function Ensure-Docker($DockerConfig) {
    switch ([string]$DockerConfig.mode) {
        'None' { Write-Host 'Docker: não solicitado.' }
        'Existing' {
            if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'docker.mode=Existing, mas docker.exe não foi encontrado.' }
            Write-Host 'Docker existente: OK'
        }
        'MobyWindows' {
            if (-not (Test-WindowsServer)) { throw 'MobyWindows exige Windows Server.' }
            $script = Join-Path $env:TEMP 'jornada-install-moby.ps1'
            Invoke-WebRequest -UseBasicParsing -Uri ([string]$DockerConfig.mobyInstallScriptUrl) -OutFile $script
            $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $script).Hash
            if (-not [string]::Equals($actualHash, [string]$DockerConfig.mobyInstallScriptSha256, [StringComparison]::OrdinalIgnoreCase)) {
                Remove-Item -Force $script -ErrorAction SilentlyContinue
                throw 'SHA-256 do script Moby diverge do valor homologado.'
            }
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script
            $exitCode = $LASTEXITCODE
            Remove-Item -Force $script -ErrorAction SilentlyContinue
            if ($exitCode -ne 0) { throw "Instalação Moby falhou. ExitCode=$exitCode" }
        }
    }
}

function Install-SqlFromMedia($SqlConfig) {
    $setup = (Resolve-Path -LiteralPath ([string]$SqlConfig.setupExe)).Path
    $accounts = @($SqlConfig.sqlSysAdminAccounts | ForEach-Object { '"' + [string]$_ + '"' })
    $args = @('/Q','/ACTION=Install','/FEATURES=SQLENGINE',"/INSTANCENAME=$($SqlConfig.instanceName)",'/TCPENABLED=1','/IACCEPTSQLSERVERLICENSETERMS','/UPDATEENABLED=TRUE',"/SQLSYSADMINACCOUNTS=$($accounts -join ' ')")
    $directories = @{
        SQLUSERDBDIR = [string]$SqlConfig.userDataDirectory
        SQLUSERDBLOGDIR = [string]$SqlConfig.userLogDirectory
        SQLTEMPDBDIR = [string]$SqlConfig.tempDbDirectory
        SQLBACKUPDIR = [string]$SqlConfig.backupDirectory
    }
    foreach ($key in $directories.Keys) {
        if (-not [string]::IsNullOrWhiteSpace($directories[$key])) {
            New-Item -ItemType Directory -Force -Path $directories[$key] | Out-Null
            $args += "/$key=$($directories[$key])"
        }
    }
    $pidVariable = [string]$SqlConfig.productKeyEnvironmentVariable
    if (-not [string]::IsNullOrWhiteSpace($pidVariable)) {
        $pidValue = [Environment]::GetEnvironmentVariable($pidVariable)
        if (-not [string]::IsNullOrWhiteSpace($pidValue)) { $args += "/PID=$pidValue" }
    }
    $process = Start-Process -FilePath $setup -ArgumentList $args -Wait -PassThru
    if ($process.ExitCode -notin @(0,3010)) { throw "SQL Server Setup falhou. ExitCode=$($process.ExitCode)" }
}

function Invoke-Sql([string]$ConnectionString, [string]$SqlText) {
    $connection = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 0
        $command.CommandText = $SqlText
        $null = $command.ExecuteNonQuery()
    }
    finally {
        if ($connection.State -ne [Data.ConnectionState]::Closed) { $connection.Close() }
        $connection.Dispose()
    }
}

function Initialize-Database($SqlConfig, [string]$DdlPath) {
    if (-not [bool]$SqlConfig.initializeDatabase) { return }
    $target = New-Object System.Data.SqlClient.SqlConnectionStringBuilder([string]$SqlConfig.connectionString)
    $database = $target.InitialCatalog
    if ([string]::IsNullOrWhiteSpace($database)) { throw 'connectionString deve conter Database/Initial Catalog.' }
    $master = New-Object System.Data.SqlClient.SqlConnectionStringBuilder([string]$SqlConfig.connectionString)
    $master.InitialCatalog = 'master'
    $escapedName = $database.Replace(']', ']]')
    $escapedLiteral = $database.Replace("'", "''")
    Invoke-Sql $master.ConnectionString "IF DB_ID(N'$escapedLiteral') IS NULL CREATE DATABASE [$escapedName];"

    $ddl = Get-Content -Raw -Encoding UTF8 $DdlPath
    foreach ($batch in [Text.RegularExpressions.Regex]::Split($ddl, '(?im)^\s*GO\s*(?:--.*)?$')) {
        if (-not [string]::IsNullOrWhiteSpace($batch)) { Invoke-Sql ([string]$SqlConfig.connectionString) $batch }
    }
    Write-Host 'DDL Jornada aplicado.'
}

function Install-Payload([string]$Payload, [string]$InstallRoot) {
    New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
    foreach ($folder in @('apps','clients','config','database')) {
        $source = Join-Path $Payload $folder
        $destination = Join-Path $InstallRoot $folder
        if (Test-Path -LiteralPath $destination) { Remove-Item -Recurse -Force $destination }
        Copy-Item -Recurse -Force -Path $source -Destination $destination
    }
    $scripts = Join-Path $InstallRoot 'scripts'
    New-Item -ItemType Directory -Force -Path $scripts | Out-Null
    Copy-Item -Force (Join-Path $Payload 'install\windows-production\Invoke-JornadaComponent.ps1') $scripts
}

function Write-Json([string]$Path, $Value) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    $Value | ConvertTo-Json -Depth 30 | Set-Content -Encoding UTF8 -Path $Path
}

function Set-RestrictedAcl([string]$Path, [string]$TaskUser) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    & icacls.exe $Path '/inheritance:r' '/grant:r' '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' /T /C | Out-Null
    if (-not [string]::Equals($TaskUser, 'SYSTEM', [StringComparison]::OrdinalIgnoreCase)) {
        & icacls.exe $Path '/grant:r' "$TaskUser`:(OI)(CI)RX" /T /C | Out-Null
    }
}

function Get-GlobalEnvironment($Config, [string]$InstallRoot, [string]$DataRoot) {
    $envTable = To-EnvironmentTable $Config.runtime.environment
    $envTable['ConnectionStrings__Jornada'] = [string]$Config.sql.connectionString
    $envTable['DOTNET_ENVIRONMENT'] = 'Production'
    $envTable['ASPNETCORE_ENVIRONMENT'] = 'Production'
    $envTable['Contracts__RepositoryRoot'] = $InstallRoot
    $envTable['BronzeStorage__Provider'] = 'FileSystem'
    $envTable['BronzeStorage__RootPath'] = Join-Path $DataRoot 'bronze'
    $envTable['IngestionStaging__RootPath'] = Join-Path $DataRoot 'staging'
    return $envTable
}

function New-Trigger($Trigger) {
    switch ([string]$Trigger.type) {
        'AtStartup' { return New-ScheduledTaskTrigger -AtStartup }
        'Daily' {
            $time = [datetime]::ParseExact([string]$Trigger.at, 'HH:mm', [Globalization.CultureInfo]::InvariantCulture)
            return New-ScheduledTaskTrigger -Daily -At $time
        }
        'Weekly' {
            $time = [datetime]::ParseExact([string]$Trigger.at, 'HH:mm', [Globalization.CultureInfo]::InvariantCulture)
            return New-ScheduledTaskTrigger -Weekly -WeeksInterval 1 -DaysOfWeek @($Trigger.days) -At $time
        }
    }
}

function Register-JornadaTask($Task, $Config, [string]$InstallRoot, [string]$DataRoot, [hashtable]$GlobalEnvironment) {
    if (-not [bool]$Task.enabled) { return }
    if ([string]$Task.component -eq 'Integrator' -and -not [bool]$Config.integrator.enabled) { throw "Tarefa $($Task.name) exige integrator.enabled=true." }

    $executable = Join-Path $InstallRoot (Get-ComponentPath ([string]$Task.component))
    $working = Split-Path -Parent $executable
    $taskEnvironment = Merge-Environment $GlobalEnvironment $Task.environment
    if ([string]$Task.component -eq 'Api') { $taskEnvironment['ASPNETCORE_URLS'] = [string]$Config.http.apiUrls }
    if ([string]$Task.component -eq 'ResultadoApi') {
        $taskEnvironment['ASPNETCORE_URLS'] = [string]$Config.http.resultadoUrls
        $taskEnvironment['JornadaApiBaseUrl'] = [string]$Config.http.jornadaApiBaseUrl
    }

    $taskConfigPath = Join-Path $InstallRoot ("config\tasks\{0}.json" -f [string]$Task.name)
    Write-Json $taskConfigPath ([ordered]@{
        name = [string]$Task.name
        executable = $executable
        workingDirectory = $working
        arguments = @($Task.arguments | ForEach-Object { [string]$_ })
        environment = $taskEnvironment
        logDirectory = Join-Path $DataRoot 'logs'
    })

    $runner = Join-Path $InstallRoot 'scripts\Invoke-JornadaComponent.ps1'
    $actionArgs = "-NoProfile -ExecutionPolicy Bypass -File `"$runner`" -TaskConfig `"$taskConfigPath`""
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $actionArgs -WorkingDirectory $working
    $trigger = New-Trigger $Task.trigger
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Days 3650)
    $taskUser = [string]$Config.taskAccount.user

    if ([string]::Equals($taskUser, 'SYSTEM', [StringComparison]::OrdinalIgnoreCase)) {
        Register-ScheduledTask -TaskName ([string]$Task.name) -Action $action -Trigger $trigger -Settings $settings -User 'SYSTEM' -RunLevel Highest -Force | Out-Null
    }
    else {
        $secretVariable = [string]$Config.taskAccount.passwordEnvironmentVariable
        if ([string]::IsNullOrWhiteSpace($secretVariable)) { throw 'Conta de tarefa customizada exige passwordEnvironmentVariable.' }
        $taskSecret = [Environment]::GetEnvironmentVariable($secretVariable)
        if ([string]::IsNullOrWhiteSpace($taskSecret)) { throw "Variável de segredo não definida: $secretVariable" }
        Register-ScheduledTask -TaskName ([string]$Task.name) -Action $action -Trigger $trigger -Settings $settings -User $taskUser -Password $taskSecret -RunLevel Highest -Force | Out-Null
    }
    Write-Host "Tarefa registrada: $($Task.name)"
}

$configFull = (Resolve-Path -LiteralPath $ConfigPath).Path
$payloadFull = (Resolve-Path -LiteralPath $PayloadRoot).Path
$config = Get-Content -Raw -Encoding UTF8 $configFull | ConvertFrom-Json
Assert-Config $config $payloadFull

Write-Host 'Configuração e payload: OK'
Write-Host "installationRoot=$($config.installationRoot)"
Write-Host "dataRoot=$($config.dataRoot)"
Write-Host "sql.mode=$($config.sql.mode)"
Write-Host "docker.mode=$($config.docker.mode)"
Write-Host "tarefas habilitadas=$(@($config.tasks | Where-Object { [bool]$_.enabled }).Count)"

if ($ValidateOnly -or $PlanOnly) {
    if (-not (Test-WindowsServer)) { Write-Warning 'Validação executada fora de Windows Server; o host de Produção deve ser Windows Server homologado.' }
    if ($PlanOnly) { Write-Host 'PLAN ONLY: nenhuma alteração aplicada.' } else { Write-Host 'VALIDATE ONLY: nenhuma alteração aplicada.' }
    exit 0
}

if (-not (Test-Administrator)) { throw 'Execute PowerShell elevado como Administrador.' }
if (-not (Test-WindowsServer)) { throw 'Instalação de Produção exige Windows Server.' }

Ensure-DotNet8 $config.dotnet
Ensure-Docker $config.docker
if ([string]$config.sql.mode -eq 'InstallFromMedia') { Install-SqlFromMedia $config.sql }

$installRoot = [IO.Path]::GetFullPath([string]$config.installationRoot)
$dataRoot = [IO.Path]::GetFullPath([string]$config.dataRoot)
foreach ($path in @($dataRoot,(Join-Path $dataRoot 'bronze'),(Join-Path $dataRoot 'staging'),(Join-Path $dataRoot 'logs'))) {
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

Install-Payload $payloadFull $installRoot
Initialize-Database $config.sql (Join-Path $installRoot 'database\Jornada_Fase1.sql')

if ([bool]$config.integrator.enabled) {
    Write-Json (Join-Path $installRoot 'clients\Jornada.Integrador\integrador.config.json') ([ordered]@{
        gestor = [string]$config.integrator.gestor
        accessKey = [string]$config.integrator.accessKey
        endpoints = [ordered]@{ envio = [string]$config.integrator.endpoints.envio; resultado = [string]$config.integrator.endpoints.resultado }
        polling = [ordered]@{ intervalSeconds = [int]$config.integrator.polling.intervalSeconds; timeoutSeconds = [int]$config.integrator.polling.timeoutSeconds }
        diretorioSaida = [string]$config.integrator.diretorioSaida
    })
}

$globalEnvironment = Get-GlobalEnvironment $config $installRoot $dataRoot
foreach ($task in @($config.tasks)) { Register-JornadaTask $task $config $installRoot $dataRoot $globalEnvironment }
Set-RestrictedAcl (Join-Path $installRoot 'config') ([string]$config.taskAccount.user)
if ([bool]$config.integrator.enabled) { Set-RestrictedAcl (Join-Path $installRoot 'clients\Jornada.Integrador') ([string]$config.taskAccount.user) }

Write-Host 'Instalação concluída.'
Write-Warning 'Jornada.Api permanece deny-by-default em Production enquanto o adaptador de identidade corporativa/secret store não estiver implementado e homologado.'
Write-Warning 'Cadências de linkage/calibração vêm desabilitadas no exemplo e só devem ser ativadas após HML.'
