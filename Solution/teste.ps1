$ErrorActionPreference = "Stop"
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
