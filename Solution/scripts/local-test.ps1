Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$DefaultEnvFile = Join-Path $Root '.env'
$EnvFile = if ([string]::IsNullOrWhiteSpace($env:JORNADA_LOCAL_ENV_FILE)) { $DefaultEnvFile } else { [IO.Path]::GetFullPath($env:JORNADA_LOCAL_ENV_FILE) }

function Invoke-NativeStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    Write-Host ''
    Write-Host "--- $Name ---"
    $global:LASTEXITCODE = 0
    & $Action
    $code = $LASTEXITCODE
    if ($code -ne 0) { throw "$Name falhou ($code)." }
}

# Scripts PowerShell internos propagam exceções diretamente. Não usamos LASTEXITCODE para
# inferir sucesso deles porque esse valor pode refletir o último executável nativo chamado
# internamente, mesmo quando o script tratou a condição e terminou com sucesso.
& (Join-Path $PSScriptRoot 'local-db.ps1') -Action up

$vars = @{}
Get-Content $EnvFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
        $parts = $line.Split('=',2)
        $vars[$parts[0].Trim()] = $parts[1]
    }
}
$port = if ($vars['JORNADA_SQL_PORT']) { $vars['JORNADA_SQL_PORT'] } else { '14333' }
$db = if ($vars['JORNADA_SQL_DATABASE']) { $vars['JORNADA_SQL_DATABASE'] } else { 'JornadaLocal' }
$password = $vars['JORNADA_SQL_SA_PASSWORD']
if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD não definido.' }
$env:JORNADA_TEST_SQL_CONNECTION = "Server=localhost,$port;Database=$db;User Id=sa;Password=$password;TrustServerCertificate=true;Encrypt=false"

Push-Location $Root
try {
    if (-not (Get-Command python -ErrorAction SilentlyContinue)) { throw 'Python 3 é necessário para os gates locais.' }

    Invoke-NativeStep 'OpenAPI contract gate' {
        python scripts/openapi-contract-gate.py
    }

    Invoke-NativeStep 'Technical closure gate' {
        python scripts/technical-closure-gate.py
    }

    Write-Host ''
    Write-Host '--- SQL runtime smoke 3.70 ---'
    & (Join-Path $PSScriptRoot 'local-sql-runtime-smoke.ps1')

    Invoke-NativeStep 'dotnet restore' {
        dotnet restore Jornada.sln
    }

    Invoke-NativeStep 'dotnet build Release' {
        dotnet build Jornada.sln --configuration Release --no-restore -warnaserror
    }

    # Espelha o gate unitário do CI: este assembly também contém fixtures Integration
    # que exigem ambientes dedicados e não pertencem ao core local.
    Invoke-NativeStep 'Unit/non-integration tests' {
        dotnet test tests/Jornada.Tests/Jornada.Tests.csproj --configuration Release --no-build --filter 'TestCategory!=Integration'
    }

    Invoke-NativeStep 'Integration tests' {
        dotnet test tests/Jornada.Integration.Tests/Jornada.Integration.Tests.csproj --configuration Release --no-build
    }

    Write-Host ''
    Write-Host 'LOCAL CORE TEST: OK'
}
finally {
    Pop-Location
}
