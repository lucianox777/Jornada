param(
    [Parameter(Mandatory = $true)]
    [string]$ZipPath,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$resolved = (Resolve-Path -LiteralPath $ZipPath).Path
$env:JORNADA_SEHAB_ASIS_ZIP = $resolved

Write-Host "SEHAB as-is: $resolved"
dotnet test tests/Jornada.Tests/Jornada.Tests.csproj `
    -c $Configuration `
    --filter "TestCategory=ExternalRealData"
exit $LASTEXITCODE
