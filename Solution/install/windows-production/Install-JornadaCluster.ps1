[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$ConfigPath,
    [Parameter(Mandatory = $true)] [string]$PayloadRoot,
    [Parameter(Mandatory = $true)] [string]$NodeId,
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

function Test-HhMm([string]$Value) {
    $parsed = [datetime]::MinValue
    return [datetime]::TryParseExact(
        $Value,
        'HH:mm',
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::None,
        [ref]$parsed)
}

function Assert-AbsolutePath([string]$Value, [string]$Name, [string]$EnvironmentName) {
    if ([string]::IsNullOrWhiteSpace($Value)) { throw "$Name é obrigatório." }
    if ($EnvironmentName -eq 'Production' -and -not [IO.Path]::IsPathRooted($Value)) {
        throw "$Name deve ser absoluto em Production."
    }
    if ($EnvironmentName -eq 'Test' -and -not ([IO.Path]::IsPathRooted($Value) -or $Value.StartsWith('/'))) {
        throw "$Name deve ser absoluto em Test."
    }
}

function Assert-ClusterConfig($Config, [string]$RequestedNodeId) {
    if ([int](Require-Property $Config 'schemaVersion') -ne 1) {
        throw 'schemaVersion suportada: 1.'
    }

    $environmentName = [string](Require-Property $Config 'environment')
    if ($environmentName -notin @('Test','Production')) {
        throw 'environment deve ser Test ou Production.'
    }

    Assert-AbsolutePath ([string](Require-Property $Config 'installationRoot')) 'installationRoot' $environmentName

    $storage = Require-Property $Config 'storage'
    $bronzeRoot = [string](Require-Property $storage 'bronzeRoot')
    Assert-AbsolutePath $bronzeRoot 'storage.bronzeRoot' $environmentName

    $nodes = @(Require-Property $Config 'nodes')
    if ($nodes.Count -lt 1) { throw 'nodes deve conter ao menos um nó.' }

    $ids = @{}
    $selected = $null
    foreach ($node in $nodes) {
        $id = [string](Require-Property $node 'id')
        if ($id -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$') { throw "node.id inválido: $id" }
        if ($ids.ContainsKey($id)) { throw "node.id duplicado: $id" }
        $ids[$id] = $true

        $hostName = [string](Require-Property $node 'host')
        if ([string]::IsNullOrWhiteSpace($hostName)) { throw "node.host é obrigatório em $id." }

        foreach ($propertyName in @('dataRoot','stagingRoot','logsRoot')) {
            $value = [string](Require-Property $node $propertyName)
            Assert-AbsolutePath $value "nodes[$id].$propertyName" $environmentName
        }

        if ([string]::Equals([string]$node.stagingRoot, $bronzeRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Staging de $id não pode apontar para a Bronze compartilhada."
        }

        if ([string]::Equals($id, $RequestedNodeId, [StringComparison]::OrdinalIgnoreCase)) {
            $selected = $node
        }
    }

    if ($null -eq $selected) { throw "NodeId não existe na configuração: $RequestedNodeId" }

    $sql = Require-Property $Config 'sql'
    if ([string]$sql.mode -notin @('Existing','External','InstallFromMedia')) {
        throw 'sql.mode deve ser Existing, External ou InstallFromMedia.'
    }
    if ([string]::IsNullOrWhiteSpace([string]$sql.connectionString)) {
        throw 'sql.connectionString é obrigatório.'
    }
    if ($environmentName -eq 'Production' -and [string]$sql.connectionString -match '(?i)Encrypt\s*=\s*false|TrustServerCertificate\s*=\s*true') {
        throw 'A connection string de Production não pode desabilitar TLS nem confiar cegamente no certificado.'
    }

    $http = Require-Property $Config 'http'
    foreach ($name in @('apiUrls','resultadoUrls','jornadaApiBaseUrl')) {
        if ([string]::IsNullOrWhiteSpace([string]$http.$name)) { throw "http.$name é obrigatório." }
    }

    $validComponents = @('Api','ResultadoApi','Processor','OperationsMaintenance','BronzeMaintenance','LinkageParameters','LinkageRunner','Integrator')
    $taskNames = @{}
    foreach ($task in @(Require-Property $Config 'tasks')) {
        $taskName = [string](Require-Property $task 'name')
        if ([string]::IsNullOrWhiteSpace($taskName)) { throw 'Toda tarefa precisa de name.' }
        if ($taskNames.ContainsKey($taskName)) { throw "Nome de tarefa duplicado: $taskName" }
        $taskNames[$taskName] = $true

        if ([string]$task.component -notin $validComponents) { throw "Componente desconhecido em $taskName: $($task.component)" }
        $trigger = Require-Property $task 'trigger'
        if ([string]$trigger.type -notin @('AtStartup','Daily','Weekly')) { throw "Trigger inválido em $taskName." }
        if ([string]$trigger.type -in @('Daily','Weekly') -and -not (Test-HhMm ([string]$trigger.at))) {
            throw "Horário HH:mm inválido em $taskName."
        }
        if ([string]$trigger.type -eq 'Weekly' -and (-not $trigger.days -or @($trigger.days).Count -eq 0)) {
            throw "Tarefa semanal sem dias: $taskName."
        }
    }

    $integrator = Require-Property $Config 'integrator'
    if ([bool]$integrator.enabled) {
        $accessKey = [string]$integrator.accessKey
        if ([string]::IsNullOrWhiteSpace($accessKey) -or $accessKey -eq 'CHANGE_ME') {
            throw 'integrator.accessKey deve ser configurada quando o Integrador estiver habilitado.'
        }
    }

    return $selected
}

function Convert-TaskForNode($Task, $Config, $Node, [string]$RuntimeEnvironment) {
    $environment = [ordered]@{}
    if ($null -ne $Task.environment) {
        foreach ($property in $Task.environment.PSObject.Properties) {
            $environment[$property.Name] = [string]$property.Value
        }
    }

    $environment['BronzeStorage__Provider'] = 'FileSystem'
    $environment['BronzeStorage__RootPath'] = [string]$Config.storage.bronzeRoot
    $environment['IngestionStaging__RootPath'] = [string]$Node.stagingRoot
    $environment['DOTNET_ENVIRONMENT'] = $RuntimeEnvironment
    $environment['ASPNETCORE_ENVIRONMENT'] = $RuntimeEnvironment
    $environment['JORNADA_NODE_ID'] = [string]$Node.id

    return [ordered]@{
        name = [string]$Task.name
        enabled = [bool]$Task.enabled
        component = [string]$Task.component
        trigger = $Task.trigger
        arguments = @($Task.arguments | ForEach-Object { [string]$_ })
        environment = $environment
    }
}

$configFull = (Resolve-Path -LiteralPath $ConfigPath).Path
$payloadFull = (Resolve-Path -LiteralPath $PayloadRoot).Path
$config = Get-Content -Raw -Encoding UTF8 $configFull | ConvertFrom-Json
$node = Assert-ClusterConfig $config $NodeId
$environmentName = [string]$config.environment
$runtimeEnvironment = if ($environmentName -eq 'Test') { 'Development' } else { 'Production' }

Write-Host 'Configuração cluster: OK'
Write-Host "environment=$environmentName"
Write-Host "node.id=$($node.id)"
Write-Host "node.host=$($node.host)"
Write-Host "storage.bronzeRoot=$($config.storage.bronzeRoot)"
Write-Host "node.stagingRoot=$($node.stagingRoot)"
Write-Host "node.logsRoot=$($node.logsRoot)"

if ($environmentName -eq 'Test') {
    if ($ValidateOnly -or $PlanOnly) {
        Write-Host 'Perfil Test validado. A execução canônica desse perfil é Docker Compose local.'
        return
    }
    throw 'O perfil Test deve ser executado pelo harness Docker local; o instalador Windows aplica o perfil Production.'
}

if (-not ($ValidateOnly -or $PlanOnly)) {
    $expectedHost = ([string]$node.host -split '\.')[0]
    if (-not [string]::Equals($expectedHost, $env:COMPUTERNAME, [StringComparison]::OrdinalIgnoreCase)) {
        throw "NodeId $($node.id) está configurado para host '$($node.host)', mas esta máquina é '$env:COMPUTERNAME'."
    }

    foreach ($path in @([string]$config.storage.bronzeRoot, [string]$node.stagingRoot, [string]$node.logsRoot)) {
        New-Item -ItemType Directory -Force -Path $path | Out-Null
    }
}

$tasks = @()
foreach ($task in @($config.tasks)) {
    $tasks += Convert-TaskForNode $task $config $node $runtimeEnvironment
}

$hostConfig = [ordered]@{
    installationRoot = [string]$config.installationRoot
    dataRoot = [string]$node.dataRoot
    dotnet = $config.dotnet
    docker = $config.docker
    sql = $config.sql
    http = $config.http
    runtime = $config.runtime
    integrator = $config.integrator
    taskAccount = $config.taskAccount
    tasks = $tasks
}

$tempConfig = Join-Path $env:TEMP ("Jornada.Host.{0}.{1}.json" -f [string]$node.id, [Guid]::NewGuid().ToString('N'))
$hostConfig | ConvertTo-Json -Depth 40 | Set-Content -Encoding UTF8 -Path $tempConfig

try {
    $hostInstaller = Join-Path $PSScriptRoot 'Install-JornadaProduction.ps1'
    if (-not (Test-Path -LiteralPath $hostInstaller)) {
        throw "Instalador host não encontrado: $hostInstaller"
    }

    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $hostInstaller,
        '-ConfigPath', $tempConfig,
        '-PayloadRoot', $payloadFull
    )
    if ($ValidateOnly) { $arguments += '-ValidateOnly' }
    if ($PlanOnly) { $arguments += '-PlanOnly' }

    & powershell.exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "Instalador host falhou. ExitCode=$LASTEXITCODE" }

    if (-not ($ValidateOnly -or $PlanOnly)) {
        $taskDirectory = Join-Path ([string]$config.installationRoot) 'config\tasks'
        foreach ($task in @($tasks | Where-Object { [bool]$_.enabled })) {
            $taskPath = Join-Path $taskDirectory ("{0}.json" -f [string]$task.name)
            if (-not (Test-Path -LiteralPath $taskPath)) { continue }
            $taskConfig = Get-Content -Raw -Encoding UTF8 $taskPath | ConvertFrom-Json
            $taskConfig.logDirectory = [string]$node.logsRoot
            $taskConfig | ConvertTo-Json -Depth 40 | Set-Content -Encoding UTF8 -Path $taskPath
        }

        $legacyBronze = Join-Path ([string]$node.dataRoot) 'bronze'
        if (-not [string]::Equals(
            [IO.Path]::GetFullPath($legacyBronze),
            [IO.Path]::GetFullPath([string]$config.storage.bronzeRoot),
            [StringComparison]::OrdinalIgnoreCase)) {
            if ((Test-Path -LiteralPath $legacyBronze) -and @((Get-ChildItem -Force -LiteralPath $legacyBronze)).Count -eq 0) {
                Remove-Item -Force -LiteralPath $legacyBronze
            }
        }

        Write-Host "Nó $($node.id) instalado."
        Write-Host 'Bronze é compartilhada entre os nós; Staging e logs permanecem locais a esta VM.'
        Write-Host 'Balanceamento HTTP não é provisionado pela Jornada; o endpoint/VIP externo deve apontar para as VMs quando aplicável.'
    }
}
finally {
    Remove-Item -Force -LiteralPath $tempConfig -ErrorAction SilentlyContinue
}
