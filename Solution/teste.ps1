param(
    [ValidateSet('HML','DEV','PROD')]
    [string]$RuntimeMode='HML'
)

$ErrorActionPreference = "Stop"
$env:JORNADA_RUNTIME_MODE=$RuntimeMode.ToUpperInvariant()
$modeDetail=switch($env:JORNADA_RUNTIME_MODE){
    'DEV' {'corpus adicional habilitado'}
    'PROD' {'operações destrutivas protegidas'}
    default {'padrão HML'}
}
Write-Host "Modo runtime da Console: $($env:JORNADA_RUNTIME_MODE) ($modeDetail)"
$SolutionRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $SolutionRoot

$Solution = Join-Path $SolutionRoot "Jornada.sln"
$Project = Join-Path $SolutionRoot "src\Jornada.DevConsole\Jornada.DevConsole.csproj"
$DotnetRoot = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"
$LocalDotnet = Join-Path $DotnetRoot "dotnet.exe"
$DotnetExe = if (Test-Path $LocalDotnet) { $LocalDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }

if (Test-Path $LocalDotnet) {
    $env:DOTNET_ROOT = $DotnetRoot
    $env:PATH = "$DotnetRoot;$env:PATH"
}

$Pwsh7 = "C:\Program Files\PowerShell\7"
if (Test-Path (Join-Path $Pwsh7 "pwsh.exe")) {
    $env:PATH = "$Pwsh7;$env:PATH"
}

if (-not (Test-Path $Solution)) { throw "Jornada.sln nao encontrado em $SolutionRoot" }
if (-not (Test-Path $Project)) { throw "Jornada.DevConsole.csproj nao encontrado em $Project" }

# Evita manter no localhost:5000 uma Console DEV antiga depois de atualizar o
# checkout. Se a porta estiver ocupada por outro processo, falha em vez de matar
# algo que não pertence à Jornada.
$listenerPids = @()
try {
    $listenerPids = @(Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique)
}
catch {
    $listenerPids = @()
}
foreach ($listenerPid in $listenerPids) {
    $proc = Get-CimInstance Win32_Process -Filter "ProcessId=$listenerPid" -ErrorAction SilentlyContinue
    $commandLine = [string]$proc.CommandLine
    if ($commandLine -match 'Jornada\.DevConsole') {
        Write-Host "Encerrando Console DEV anterior (PID $listenerPid) para carregar o checkout atual..."
        Stop-Process -Id $listenerPid -Force -ErrorAction Stop
        Start-Sleep -Milliseconds 300
    }
    else {
        throw "A porta localhost:5000 já está ocupada pelo PID $listenerPid e não pertence à Jornada.DevConsole."
    }
}

$git = Get-Command git -ErrorAction SilentlyContinue
if ($git) {
    $revision = (& $git.Source -C $RepoRoot rev-parse --short=12 HEAD 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($revision)) {
        $env:JORNADA_DEV_CONSOLE_SOURCE_SHA = $revision
        Write-Host "Revisão da Console DEV: $revision"
    }
}

# Executa a CLI a partir da raiz do repositorio. Assim o bootstrap local pode
# usar o SDK 10.x instalado no perfil sem alterar o global.json normativo da Solution.
Set-Location $RepoRoot

$Version = (& $DotnetExe --version).Trim()
if (-not $Version.StartsWith("10.")) { throw ".NET SDK 10.x obrigatorio. Encontrado: $Version" }

Write-Host "Usando .NET $Version"
Write-Host "Restaurando dependencias em modo locked..."
& $DotnetExe restore $Solution --locked-mode
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Compilando Jornada.sln uma unica vez..."
& $DotnetExe build $Solution --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Iniciando Jornada.DevConsole..."
& $DotnetExe run --no-build --project $Project
exit $LASTEXITCODE
