$ErrorActionPreference = "Stop"
$SolutionRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $SolutionRoot

$Solution = Join-Path $SolutionRoot "Jornada.sln"
$Project = Join-Path $SolutionRoot "src\Jornada.DevConsole\Jornada.DevConsole.csproj"
$LocalDotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
$DotnetExe = if (Test-Path $LocalDotnet) { $LocalDotnet } else { "dotnet" }

$Pwsh7 = "C:\Program Files\PowerShell\7"
if (Test-Path $Pwsh7) { $env:PATH = "$Pwsh7;$env:PATH" }

if (-not (Test-Path $Solution)) { throw "Jornada.sln nao encontrado em $SolutionRoot" }
if (-not (Test-Path $Project)) { throw "Jornada.DevConsole.csproj nao encontrado em $Project" }

$Version = (& $DotnetExe --version).Trim()
if (-not $Version.StartsWith("10.")) { throw ".NET SDK 10.x obrigatorio. Encontrado: $Version" }

Write-Host "Usando .NET $Version"
& $DotnetExe build $Solution
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $DotnetExe run --no-build --project $Project
exit $LASTEXITCODE
