param(
    [string]$OutputDirectory = "",
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$solutionRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $solutionRoot '.local\windows-production-bundle'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { Remove-Item -Recurse -Force $output }
New-Item -ItemType Directory -Force -Path $output | Out-Null

function Resolve-Python3 {
    foreach ($candidate in @(
        @{ Name = 'python3'; Prefix = @() },
        @{ Name = 'python'; Prefix = @() },
        @{ Name = 'py'; Prefix = @('-3') }
    )) {
        $command = Get-Command $candidate.Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $command) { continue }
        $prefix = @($candidate.Prefix)
        & $command.Source @prefix -c 'import sys; raise SystemExit(0 if sys.version_info.major == 3 else 1)' 2>$null
        if ($LASTEXITCODE -eq 0) {
            return @{ Exe = $command.Source; Prefix = $prefix }
        }
    }
    throw 'Python 3 não encontrado (tentados: python3, python, py -3).'
}

$projects = [ordered]@{
    'Jornada.Api' = 'src\Jornada.Api\Jornada.Api.csproj'
    'Jornada.Resultado.Api' = 'src\Jornada.Resultado.Api\Jornada.Resultado.Api.csproj'
    'Jornada.Processor.Worker' = 'src\Jornada.Processor.Worker\Jornada.Processor.Worker.csproj'
    'Jornada.Operations.Maintenance.Worker' = 'src\Jornada.Operations.Maintenance.Worker\Jornada.Operations.Maintenance.Worker.csproj'
    'Jornada.Bronze.Maintenance.Worker' = 'src\Jornada.Bronze.Maintenance.Worker\Jornada.Bronze.Maintenance.Worker.csproj'
    'Jornada.Linkage.Parameters.Worker' = 'src\Jornada.Linkage.Parameters.Worker\Jornada.Linkage.Parameters.Worker.csproj'
    'Jornada.Linkage.Runner' = 'src\Jornada.Linkage.Runner\Jornada.Linkage.Runner.csproj'
}

$tools = [ordered]@{
    'Jornada.Bronze.Verify' = 'src\Jornada.Bronze.Verify\Jornada.Bronze.Verify.csproj'
    'Jornada.Linkage.Evaluation' = 'src\Jornada.Linkage.Evaluation\Jornada.Linkage.Evaluation.csproj'
}

Push-Location $solutionRoot
try {
    foreach ($entry in $projects.GetEnumerator()) {
        $destination = Join-Path $output ("apps\{0}" -f $entry.Key)
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        dotnet restore $entry.Value --locked-mode
        if ($LASTEXITCODE -ne 0) { throw "restore falhou: $($entry.Value)" }
        dotnet publish $entry.Value -c $Configuration --no-restore -o $destination
        if ($LASTEXITCODE -ne 0) { throw "publish falhou: $($entry.Value)" }
    }

    foreach ($entry in $tools.GetEnumerator()) {
        $destination = Join-Path $output ("tools\{0}" -f $entry.Key)
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        dotnet restore $entry.Value --locked-mode
        if ($LASTEXITCODE -ne 0) { throw "restore falhou: $($entry.Value)" }
        dotnet publish $entry.Value -c $Configuration --no-restore -o $destination
        if ($LASTEXITCODE -ne 0) { throw "publish falhou: $($entry.Value)" }
    }

    $clientProject = 'clients\Jornada.Integrador.CSharp\Jornada.Integrador.CSharp.csproj'
    $clientDestination = Join-Path $output 'clients\Jornada.Integrador'
    New-Item -ItemType Directory -Force -Path $clientDestination | Out-Null
    dotnet restore $clientProject --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'restore falhou: Jornada.Integrador.CSharp' }
    dotnet publish $clientProject -c $Configuration --no-restore -o $clientDestination
    if ($LASTEXITCODE -ne 0) { throw 'publish falhou: Jornada.Integrador.CSharp' }
}
finally {
    Pop-Location
}

$configDestination = Join-Path $output 'config'
New-Item -ItemType Directory -Force -Path $configDestination | Out-Null
Copy-Item -Recurse -Force -Path (Join-Path $solutionRoot 'config\contracts') -Destination $configDestination
foreach ($folder in @('governance','hml','observability','operations','possibilities','release')) {
    $source = Join-Path $solutionRoot ("config\$folder")
    if (Test-Path -LiteralPath $source) { Copy-Item -Recurse -Force -Path $source -Destination $configDestination }
}

# O bundle Windows consome a mesma fonte de verdade do instalador SQL canônico.
# schema-manifest.py valida que Jornada_Fase1_v3.70.sql está sincronizado com o
# manifesto e gera uma forma achatada, pois Install-JornadaProduction.ps1 executa
# batches GO diretamente via SqlClient e não interpreta diretivas sqlcmd :r.
$databaseDestination = Join-Path $output 'database'
New-Item -ItemType Directory -Force -Path $databaseDestination | Out-Null
$bundleDdl = Join-Path $databaseDestination 'Jornada_Fase1.sql'
$python3 = Resolve-Python3
$schemaManifestTool = Join-Path $solutionRoot 'scripts\schema-manifest.py'
& $python3.Exe @($python3.Prefix) $schemaManifestTool --check --flatten-output $bundleDdl
if ($LASTEXITCODE -ne 0) { throw 'Falha ao renderizar DDL canônico para o bundle Windows.' }

# Inclui o manifesto e seus fontes para proveniência/auditoria do bundle, sem manter
# uma lista paralela de migrações no builder.
$manifestSource = Join-Path $solutionRoot 'database\migrations\manifest.txt'
$migrationDestination = Join-Path $databaseDestination 'migrations'
New-Item -ItemType Directory -Force -Path $migrationDestination | Out-Null
Copy-Item -Force -Path $manifestSource -Destination (Join-Path $migrationDestination 'manifest.txt')
$manifestEntries = Get-Content -Encoding UTF8 $manifestSource |
    ForEach-Object { ($_ -split '#', 2)[0].Trim() } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
foreach ($entry in $manifestEntries) {
    $relative = $entry.Replace('/', '\')
    $source = Join-Path (Join-Path $solutionRoot 'database') $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Fonte do manifesto ausente: $entry" }
    $destination = Join-Path $databaseDestination $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -Force -LiteralPath $source -Destination $destination
}

$installDestination = Join-Path $output 'install\windows-production'
New-Item -ItemType Directory -Force -Path $installDestination | Out-Null
Copy-Item -Force -Path (Join-Path $PSScriptRoot '*.ps1') -Destination $installDestination
Copy-Item -Force -Path (Join-Path $PSScriptRoot '*.json') -Destination $installDestination
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'README.md')) {
    Copy-Item -Force -Path (Join-Path $PSScriptRoot 'README.md') -Destination $installDestination
}
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'CLUSTER.md')) {
    Copy-Item -Force -Path (Join-Path $PSScriptRoot 'CLUSTER.md') -Destination $installDestination
}

$openApiDestination = Join-Path $output 'openapi'
New-Item -ItemType Directory -Force -Path $openApiDestination | Out-Null
Copy-Item -Force -Path (Join-Path $solutionRoot 'openapi\jornada-v1.openapi.json') -Destination $openApiDestination

$manifestPath = Join-Path $output 'MANIFEST.sha256'
$lines = Get-ChildItem -Path $output -File -Recurse |
    Where-Object { $_.FullName -ne $manifestPath } |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($output.Length).TrimStart('\').Replace('\','/')
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
        "$hash  $relative"
    }
$lines | Set-Content -Encoding ascii -Path $manifestPath

Write-Host $output
