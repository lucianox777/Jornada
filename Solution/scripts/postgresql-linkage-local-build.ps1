[CmdletBinding()]
param([switch]$RunIntegration)

$ErrorActionPreference = 'Stop'
$solution = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $solution '.local\postgresql-linkage-evidence'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$results = New-Object 'System.Collections.Generic.List[object]'
$previousLocation = Get-Location
$failure = $null
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Invoke-Logged {
    param([string]$Name, [string[]]$Arguments)
    $started = [DateTimeOffset]::UtcNow
    $log = Join-Path $evidence "$Name.log"
    $code = 1
    $previousPreference = $ErrorActionPreference
    try {
        # PowerShell 5.1 may surface native stderr as nonterminating errors.
        # The process exit code, not the presence of stderr, determines success.
        $ErrorActionPreference = 'Continue'
        $global:LASTEXITCODE = 0
        & dotnet @Arguments 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath $log -ErrorAction Stop
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
        $results.Add([pscustomobject]@{
            stage = $Name
            started_at = $started.ToString('o')
            finished_at = [DateTimeOffset]::UtcNow.ToString('o')
            exit_code = $code
            status = $(if ($code -eq 0) { 'passed' } else { 'failed' })
        })
    }
    if ($code -ne 0) {
        throw "Etapa $Name falhou (código $code). Consulte $log."
    }
}

try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'SDK .NET não encontrado. Use o Developer PowerShell com o SDK 8.0 instalado.'
    }
    Set-Location $solution
    $project = 'tests/Jornada.Tests/Jornada.Tests.csproj'
    Invoke-Logged environment @('--info')
    Invoke-Logged restore @('restore', $project, '--locked-mode', '-v:normal', "-bl:$evidence\restore.binlog;ProjectImports=None")
    Invoke-Logged build @('build', $project, '-c', 'Release', '--no-restore', '-warnaserror', '-v:normal', "-bl:$evidence\build.binlog;ProjectImports=None")
    Invoke-Logged unit @('test', $project, '-c', 'Release', '--no-build', '--no-restore', '--filter', 'FullyQualifiedName~FellegiSunterScoringTests', '--logger', 'trx;LogFileName=linkage-unit.trx', '--results-directory', $evidence)
    Invoke-Logged policy @('test', $project, '-c', 'Release', '--no-build', '--no-restore', '--filter', 'FullyQualifiedName~ProbabilisticLinkagePolicyTests', '--logger', 'trx;LogFileName=linkage-policy.trx', '--results-directory', $evidence)
    if ($RunIntegration) {
        if ($env:JORNADA_POSTGRESQL_LINKAGE_TESTS -ne '1' -or
            $env:JORNADA_POSTGRESQL_CONNECTION -notmatch '(?i)(?:^|;)\s*Database\s*=\s*JornadaPgLinkageTest\s*(?:;|$)') {
            throw 'Integração exige opt-in e conexão explícita ao banco descartável JornadaPgLinkageTest. O script não cria, limpa ou instala bancos.'
        }
        Invoke-Logged integration @('test', $project, '-c', 'Release', '--no-build', '--no-restore', '--filter', 'TestCategory=PostgreSqlLinkage', '--logger', 'trx;LogFileName=postgresql-linkage.trx', '--results-directory', $evidence)
    }
}
catch {
    $failure = $_.Exception.Message
}
finally {
    Set-Location $previousLocation
    $summary = [pscustomobject]@{
        schema_version = 1
        generated_at = [DateTimeOffset]::UtcNow.ToString('o')
        outcome = $(if ($failure) { 'incomplete_or_failed' } else { 'passed' })
        integration_requested = [bool]$RunIntegration
        failure = $failure
        checks = @($results.ToArray())
    }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $evidence 'local-summary.json') -Encoding UTF8
    Write-Host "Evidências: $evidence"
}
if ($failure) { throw $failure }
