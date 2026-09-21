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
    if ([string]::IsNullOrWhiteSpace([string](Require-Property $Config 'configurationBundleVersion'))) {
        throw 'configurationBundleVersion é obrigatória.'
    }
    if ([string]::IsNullOrWhiteSpace([string](Require-Property $Config 'solutionSchema'))) {
        throw 'solutionSchema é obrigatório.'
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

    $validResidentComponents = @('Api','ResultadoApi','Processor','OperationsMaintenance','BronzeMaintenance')
    $taskNames = @{}
    foreach ($task in @(Require-Property $Config 'tasks')) {
        $taskName = [string](Require-Property $task 'name')
        if ([string]::IsNullOrWhiteSpace($taskName)) { throw 'Toda tarefa precisa de name.' }
        if ($taskNames.ContainsKey($taskName)) { throw "Nome de tarefa duplicado: $taskName" }
        $taskNames[$taskName] = $true

        if ([string]$task.component -notin $validResidentComponents) {
            throw "tasks[] aceita somente processos residentes. Componente de execução única deve permanecer em jobs/tools: $($task.component)"
        }
        $trigger = Require-Property $task 'trigger'
        if ([string]$trigger.type -ne 'AtStartup') {
            throw "Processo residente $taskName deve usar trigger AtStartup; execuções únicas não são agendadas pelo cluster."
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

function Assert-BundleCompatibility($Config, [string]$ResolvedPayloadRoot) {
    $bundlePath = Join-Path $ResolvedPayloadRoot 'config\release\configuration-bundle.json'
    if (-not (Test-Path -LiteralPath $bundlePath)) {
        throw "Payload não contém configuration-bundle.json: $bundlePath"
    }
    $bundle = Get-Content -Raw -Encoding UTF8 $bundlePath | ConvertFrom-Json
    if ([int]$bundle.schemaVersion -ne 1) { throw 'configuration-bundle.schemaVersion não suportada.' }
    if ([string]$bundle.bundleVersion -ne [string]$Config.configurationBundleVersion) {
        throw "Bundle/config incompatíveis: payload=$($bundle.bundleVersion) config=$($Config.configurationBundleVersion)."
    }
    if ([string]$bundle.solutionSchema -ne [string]$Config.solutionSchema) {
        throw "SolutionSchema incompatível: payload=$($bundle.solutionSchema) config=$($Config.solutionSchema)."
    }
    if ([int]$bundle.clusterConfigSchemaVersion -ne [int]$Config.schemaVersion) {
        throw "Schema do cluster incompatível com o bundle: payload=$($bundle.clusterConfigSchemaVersion) config=$($Config.schemaVersion)."
    }
    Write-Host "configuration bundle=$($bundle.bundleVersion); SolutionSchema=$($bundle.solutionSchema): OK"
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
    $environment['JORNADA_CONFIGURATION_BUNDLE_VERSION'] = [string]$Config.configurationBundleVersion
    $environment['JORNADA_SOLUTION_SCHEMA_VERSION'] = [string]$Config.solutionSchema

    return [ordered]@{
        name = [string]$Task.name
        enabled = [bool]$Task.enabled
        component = [string]$Task.component
        trigger = $Task.trigger
        arguments = @($Task.arguments | ForEach-Object { [string]$_ })
        environment = $environment
    }
}

function New-ManualRuntime($Config, $Node, [string]$InstallRoot, [string]$RuntimeEnvironment) {
    $environment = [ordered]@{}
    if ($null -ne $Config.runtime.environment) {
        foreach ($property in $Config.runtime.environment.PSObject.Properties) {
            $environment[$property.Name] = [string]$property.Value
        }
    }
    $environment['ConnectionStrings__Jornada'] = [string]$Config.sql.connectionString
    $environment['DOTNET_ENVIRONMENT'] = $RuntimeEnvironment
    $environment['ASPNETCORE_ENVIRONMENT'] = $RuntimeEnvironment
    $environment['Contracts__RepositoryRoot'] = $InstallRoot
    $environment['BronzeStorage__Provider'] = 'FileSystem'
    $environment['BronzeStorage__RootPath'] = [string]$Config.storage.bronzeRoot
    $environment['IngestionStaging__RootPath'] = [string]$Node.stagingRoot
    $environment['JORNADA_NODE_ID'] = [string]$Node.id
    $environment['JORNADA_CONFIGURATION_BUNDLE_VERSION'] = [string]$Config.configurationBundleVersion
    $environment['JORNADA_SOLUTION_SCHEMA_VERSION'] = [string]$Config.solutionSchema
    $environment['LinkageParameters__ConferenceToleranceConfigPath'] =
        Join-Path $InstallRoot 'config\linkage\implementation-conference-tolerance.json'

    return [ordered]@{
        schemaVersion = 1
        nodeId = [string]$Node.id
        configurationBundleVersion = [string]$Config.configurationBundleVersion
        solutionSchema = [string]$Config.solutionSchema
        environment = $environment
        executables = [ordered]@{
            linkageParameters = Join-Path $InstallRoot 'apps\Jornada.Linkage.Parameters.Worker\Jornada.Linkage.Parameters.Worker.exe'
            linkageRunner = Join-Path $InstallRoot 'apps\Jornada.Linkage.Runner\Jornada.Linkage.Runner.exe'
            bronzeVerify = Join-Path $InstallRoot 'tools\Jornada.Bronze.Verify\Jornada.Bronze.Verify.exe'
            linkageEvaluation = Join-Path $InstallRoot 'tools\Jornada.Linkage.Evaluation\Jornada.Linkage.Evaluation.exe'
            linkageConference = Join-Path $InstallRoot 'tools\Jornada.Linkage.Conference\Jornada.Linkage.Conference.exe'
            integrator = Join-Path $InstallRoot 'clients\Jornada.Integrador\Jornada.Integrador.CSharp.exe'
        }
    }
}

$configFull = (Resolve-Path -LiteralPath $ConfigPath).Path
$payloadFull = (Resolve-Path -LiteralPath $PayloadRoot).Path
$config = Get-Content -Raw -Encoding UTF8 $configFull | ConvertFrom-Json
$node = Assert-ClusterConfig $config $NodeId
Assert-BundleCompatibility $config $payloadFull
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
        $installRoot = [string]$config.installationRoot
        $taskDirectory = Join-Path $installRoot 'config\tasks'
        foreach ($task in @($tasks | Where-Object { [bool]$_.enabled })) {
            $taskPath = Join-Path $taskDirectory ("{0}.json" -f [string]$task.name)
            if (-not (Test-Path -LiteralPath $taskPath)) { continue }
            $taskConfig = Get-Content -Raw -Encoding UTF8 $taskPath | ConvertFrom-Json
            $taskConfig.logDirectory = [string]$node.logsRoot
            $taskConfig | ConvertTo-Json -Depth 40 | Set-Content -Encoding UTF8 -Path $taskPath
        }

        $toolsSource = Join-Path $payloadFull 'tools'
        $toolsDestination = Join-Path $installRoot 'tools'
        if (Test-Path -LiteralPath $toolsSource) {
            if (Test-Path -LiteralPath $toolsDestination) { Remove-Item -Recurse -Force $toolsDestination }
            Copy-Item -Recurse -Force -Path $toolsSource -Destination $toolsDestination
        }

        $jobsDirectory = Join-Path $installRoot 'jobs'
        New-Item -ItemType Directory -Force -Path $jobsDirectory | Out-Null
        foreach ($scriptName in @('Invoke-JornadaLinkageCalibration.ps1','Invoke-JornadaLinkageRun.ps1')) {
            $source = Join-Path $payloadFull "install\windows-production\$scriptName"
            if (-not (Test-Path -LiteralPath $source)) { throw "Job manual ausente do bundle: $scriptName" }
            Copy-Item -Force -LiteralPath $source -Destination (Join-Path $jobsDirectory $scriptName)
        }

        $manualRuntime = New-ManualRuntime $config $node $installRoot $runtimeEnvironment
        $manualRuntimePath = Join-Path $installRoot 'config\manual-runtime.json'
        $manualRuntime | ConvertTo-Json -Depth 40 | Set-Content -Encoding UTF8 -Path $manualRuntimePath

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
        Write-Host 'Processos residentes: Agendador do Windows / AtStartup, executando em background.'
        Write-Host "Calibrador manual: $(Join-Path $jobsDirectory 'Invoke-JornadaLinkageCalibration.ps1')"
        Write-Host "Linkage manual:   $(Join-Path $jobsDirectory 'Invoke-JornadaLinkageRun.ps1')"
        Write-Host "Bronze Verify:     $(Join-Path $installRoot 'tools\Jornada.Bronze.Verify\Jornada.Bronze.Verify.exe')"
        Write-Host "Linkage Evaluation:$(Join-Path $installRoot 'tools\Jornada.Linkage.Evaluation\Jornada.Linkage.Evaluation.exe')"
        Write-Host 'Linkage Runner permanece bloqueado enquanto não houver exatamente um modelo ATIVO; o calibrador manual executa GENERATE_DRAFT -> CONFERENCIA -> VALIDATE -> ACTIVATE e falha fechado se a tolerância governada não estiver congelada ou a evidência não estiver CONFORME.'
        Write-Host 'Bronze é compartilhada entre os nós; Staging e logs permanecem locais a esta VM.'
        Write-Host 'Monitor do nó: http://localhost:5080/monitor (visão lida do SQL compartilhado; após instalação do módulo de monitoramento).'
        Write-Host 'Balanceamento HTTP não é provisionado pela Jornada; o endpoint/VIP externo deve apontar para as VMs quando aplicável.'
    }
}
finally {
    Remove-Item -Force -LiteralPath $tempConfig -ErrorAction SilentlyContinue
}
