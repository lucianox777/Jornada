param(
    [Parameter(Mandatory = $true)]
    [string]$TaskConfig
)

$ErrorActionPreference = 'Stop'
$configPath = (Resolve-Path -LiteralPath $TaskConfig).Path
$config = Get-Content -Raw -Encoding UTF8 $configPath | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($config.executable) -or -not (Test-Path -LiteralPath $config.executable)) {
    throw "Executável da tarefa não encontrado: $($config.executable)"
}

$workingDirectory = if ([string]::IsNullOrWhiteSpace($config.workingDirectory)) {
    Split-Path -Parent $config.executable
} else {
    $config.workingDirectory
}
if (-not (Test-Path -LiteralPath $workingDirectory)) {
    throw "Working directory não encontrado: $workingDirectory"
}

if ($config.environment) {
    foreach ($property in $config.environment.PSObject.Properties) {
        [Environment]::SetEnvironmentVariable($property.Name, [string]$property.Value, 'Process')
    }
}

$arguments = @()
if ($config.arguments) {
    foreach ($argument in $config.arguments) { $arguments += [string]$argument }
}

$logDirectory = if ([string]::IsNullOrWhiteSpace($config.logDirectory)) {
    Join-Path $workingDirectory 'logs'
} else {
    $config.logDirectory
}
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd'
$taskName = if ([string]::IsNullOrWhiteSpace($config.name)) { 'Jornada' } else { [string]$config.name }
$logPath = Join-Path $logDirectory "$taskName-$stamp.log"

Push-Location $workingDirectory
try {
    "[$([DateTimeOffset]::Now.ToString('O'))] START $($config.executable) $($arguments -join ' ')" | Out-File -FilePath $logPath -Append -Encoding utf8
    & $config.executable @arguments *>> $logPath
    $exitCode = $LASTEXITCODE
    "[$([DateTimeOffset]::Now.ToString('O'))] EXIT $exitCode" | Out-File -FilePath $logPath -Append -Encoding utf8
    exit $exitCode
}
finally {
    Pop-Location
}
