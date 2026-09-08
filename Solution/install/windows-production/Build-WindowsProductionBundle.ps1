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

$projects = [ordered]@{
    'Jornada.Api' = 'src\Jornada.Api\Jornada.Api.csproj'
    'Jornada.Resultado.Api' = 'src\Jornada.Resultado.Api\Jornada.Resultado.Api.csproj'
    'Jornada.Processor.Worker' = 'src\Jornada.Processor.Worker\Jornada.Processor.Worker.csproj'
    'Jornada.Operations.Maintenance.Worker' = 'src\Jornada.Operations.Maintenance.Worker\Jornada.Operations.Maintenance.Worker.csproj'
    'Jornada.Bronze.Maintenance.Worker' = 'src\Jornada.Bronze.Maintenance.Worker\Jornada.Bronze.Maintenance.Worker.csproj'
    'Jornada.Linkage.Parameters.Worker' = 'src\Jornada.Linkage.Parameters.Worker\Jornada.Linkage.Parameters.Worker.csproj'
    'Jornada.Linkage.Runner' = 'src\Jornada.Linkage.Runner\Jornada.Linkage.Runner.csproj'
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

$databaseDestination = Join-Path $output 'database'
New-Item -ItemType Directory -Force -Path $databaseDestination | Out-Null
$baselineSource = Join-Path $solutionRoot 'database\Jornada_Fase1.sql'
$anchorSource = Join-Path $solutionRoot 'database\migrations\20260907_Cpf_Ancora.sql'
$bundleDdl = Join-Path $databaseDestination 'Jornada_Fase1.sql'
Copy-Item -Force -Path $baselineSource -Destination $bundleDdl
# O instalador legado aplica um único arquivo. Como a solução ainda não foi publicada,
# o payload de Produção V1 compõe baseline + âncora CPF obrigatória no mesmo DDL instalável.
Add-Content -Encoding UTF8 -Path $bundleDdl -Value "`r`n-- Jornada V1: âncora CPF permanente obrigatória.`r`n"
Get-Content -Raw -Encoding UTF8 $anchorSource | Add-Content -Encoding UTF8 -Path $bundleDdl
$migrationDestination = Join-Path $databaseDestination 'migrations'
New-Item -ItemType Directory -Force -Path $migrationDestination | Out-Null
Copy-Item -Force -Path $anchorSource -Destination $migrationDestination

$installDestination = Join-Path $output 'install\windows-production'
New-Item -ItemType Directory -Force -Path $installDestination | Out-Null
Copy-Item -Force -Path (Join-Path $PSScriptRoot '*.ps1') -Destination $installDestination
Copy-Item -Force -Path (Join-Path $PSScriptRoot '*.json') -Destination $installDestination
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'README.md')) {
    Copy-Item -Force -Path (Join-Path $PSScriptRoot 'README.md') -Destination $installDestination
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
