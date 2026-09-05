param()

$ErrorActionPreference = 'Stop'
$ScriptVersion = '2026.09.04-v4.03'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Solution = Join-Path $Root 'Jornada.sln'
$UnitProject = Join-Path $Root 'tests\Jornada.Tests\Jornada.Tests.csproj'
$IntegrationProject = Join-Path $Root 'tests\Jornada.Integration.Tests\Jornada.Integration.Tests.csproj'
$UnitDll = Join-Path $Root 'tests\Jornada.Tests\bin\Release\net8.0\Jornada.Tests.dll'
$IntegrationDll = Join-Path $Root 'tests\Jornada.Integration.Tests\bin\Release\net8.0\Jornada.Integration.Tests.dll'
$ComposeFile = Join-Path $Root 'docker-compose.yml'

function Invoke-Checked {
    param(
        [Parameter(Mandatory=$true)][string]$Command,
        [Parameter(Mandatory=$true)][string[]]$CommandArgs,
        [Parameter(Mandatory=$true)][string]$Step
    )

    Write-Host ''
    Write-Host "=== $Step ==="
    & $Command @CommandArgs

    if ($LASTEXITCODE -ne 0) {
        throw "$Step falhou (exit code $LASTEXITCODE)."
    }
}

function Assert-FileExists {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description não foi gerado: $Path"
    }

    $file = Get-Item -LiteralPath $Path
    Write-Host "$Description OK: $($file.FullName)"
    Write-Host "Tamanho: $($file.Length) bytes"
}

function Test-DockerEngine {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        return $false
    }

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & docker info --format '{{.ServerVersion}}' *> $null
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    return ($exitCode -eq 0)
}

function Get-CanonicalSqlServerImage {
    if (-not (Test-Path -LiteralPath $ComposeFile)) {
        throw "docker-compose.yml não encontrado: $ComposeFile"
    }

    $compose = Get-Content -LiteralPath $ComposeFile -Raw
    $match = [regex]::Match($compose, '(?m)^\s*image:\s*(?<image>[^\s#]+)\s*$')
    if (-not $match.Success) {
        throw 'Não foi possível localizar a imagem SQL Server em docker-compose.yml.'
    }

    $image = $match.Groups['image'].Value
    if ($image -notmatch '@sha256:[0-9a-fA-F]{64}$') {
        throw "A imagem SQL Server da release deve estar fixada por digest SHA-256: $image"
    }

    return $image
}

function Test-DockerImageExists {
    param([Parameter(Mandatory=$true)][string]$Image)

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & docker image inspect --format '{{.Id}}' $Image *> $null
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    return ($exitCode -eq 0)
}

function Invoke-DockerChecked {
    param(
        [Parameter(Mandatory=$true)][string[]]$DockerArgs,
        [Parameter(Mandatory=$true)][string]$Description
    )

    $output = @(& docker @DockerArgs 2>&1)
    $exitCode = $LASTEXITCODE
    foreach ($line in $output) {
        Write-Host $line
    }
    if ($exitCode -ne 0) {
        throw "$Description falhou (exit code $exitCode)."
    }
}

function Remove-SqlProbeContainer {
    param([Parameter(Mandatory=$true)][string]$Name)

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & docker rm -f $Name *> $null
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

function Test-SqlServerImageRuntime {
    param([Parameter(Mandatory=$true)][string]$Image)

    $probeName = 'jornada-sql-integrity-probe'
    $password = 'Jd!' + [Guid]::NewGuid().ToString('N') + '9aA'
    Remove-SqlProbeContainer -Name $probeName

    try {
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $containerId = (& docker run -d --name $probeName `
                -e 'ACCEPT_EULA=Y' `
                -e 'MSSQL_PID=Developer' `
                -e "MSSQL_SA_PASSWORD=$password" `
                $Image 2>&1 | Out-String).Trim()
            $runExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }

        if ($runExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
            Write-Warning "Probe SQL não conseguiu criar container a partir da imagem: $containerId"
            return $false
        }

        for ($attempt = 1; $attempt -le 60; $attempt++) {
            $previousErrorActionPreference = $ErrorActionPreference
            try {
                $ErrorActionPreference = 'Continue'
                & docker exec `
                    -e "SQLCMDPASSWORD=$password" `
                    $probeName `
                    /opt/mssql-tools18/bin/sqlcmd `
                    -S localhost -U sa -C -b `
                    -Q "SET NOCOUNT ON; SELECT 1; DBCC CHECKDB (N'master') WITH NO_INFOMSGS, ALL_ERRORMSGS;" `
                    *> $null
                $probeExitCode = $LASTEXITCODE
            }
            finally {
                $ErrorActionPreference = $previousErrorActionPreference
            }

            if ($probeExitCode -eq 0) {
                $version = (& docker exec `
                    -e "SQLCMDPASSWORD=$password" `
                    $probeName `
                    /opt/mssql-tools18/bin/sqlcmd `
                    -S localhost -U sa -C -b -h -1 -W `
                    -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128));" `
                    2>$null | Out-String).Trim()
                Write-Host "SQL Server engine: OK (ProductVersion=$version)"
                Write-Host "DBCC CHECKDB master: OK"
                return $true
            }

            $state = (& docker inspect --format '{{.State.Status}}' $probeName 2>$null | Out-String).Trim()
            if ($state -eq 'exited' -or $state -eq 'dead') {
                Write-Warning "Probe SQL encerrou antes de ficar operacional (state=$state)."
                return $false
            }

            Start-Sleep -Seconds 2
        }

        Write-Warning 'Probe SQL não respondeu dentro de 120 segundos.'
        return $false
    }
    finally {
        Remove-SqlProbeContainer -Name $probeName
    }
}

function Ensure-SqlServerImageAndRuntime {
    $image = Get-CanonicalSqlServerImage
    Write-Host "Imagem SQL requerida: $image"

    if (Test-DockerImageExists -Image $image) {
        Write-Host 'Imagem SQL: OK (digest fixado; cache local encontrado)'
    }
    else {
        Write-Host 'Imagem SQL não encontrada localmente. Executando pull pelo digest fixado...'
        Invoke-DockerChecked -DockerArgs @('pull', $image) -Description 'docker pull da imagem SQL requerida'
    }

    if (Test-SqlServerImageRuntime -Image $image) {
        return $image
    }

    Write-Warning 'A imagem existe, mas o SQL Server não passou no probe de engine/DBCC. Tentando recuperação única por re-pull.'

    $imageId = (& docker image inspect --format '{{.Id}}' $image 2>$null | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($imageId)) {
        throw 'Não foi possível resolver o ID local da imagem SQL para recuperação.'
    }

    $dependentContainers = @(& docker ps -aq --filter "ancestor=$imageId" 2>$null | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($dependentContainers.Count -gt 0) {
        throw 'A imagem SQL suspeita está sendo usada por outro container. Feche/remova esses containers antes da recuperação automática.'
    }

    Invoke-DockerChecked -DockerArgs @('image','rm',$imageId) -Description 'remoção segura da imagem SQL suspeita'
    Invoke-DockerChecked -DockerArgs @('pull',$image) -Description 'reinstalação da imagem SQL pelo digest fixado'

    if (-not (Test-SqlServerImageRuntime -Image $image)) {
        throw 'SQL Server continuou inválido após re-pull da imagem. Verifique Docker Desktop/Engine, armazenamento e logs do daemon.'
    }

    Write-Host 'Recuperação da imagem SQL: OK'
    return $image
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK não encontrado no PATH.'
}

foreach ($required in @($Solution, $UnitProject, $IntegrationProject)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Arquivo obrigatório não encontrado: $required"
    }
}

$slnText = Get-Content -LiteralPath $Solution -Raw
if ($slnText -notmatch 'Jornada\.Integration\.Tests') {
    throw "Jornada.sln não contém Jornada.Integration.Tests. Use a Solution completa desta release."
}

$previousSqlConnection = $env:JORNADA_TEST_SQL_CONNECTION
$previousUseExisting = $env:JORNADA_TEST_SQL_USE_EXISTING_DATABASE
$previousSqlImage = $env:JORNADA_TEST_SQL_IMAGE
$previousSqlTarget = $env:JORNADA_TEST_SQL_TARGET
$previousResetExisting = $env:JORNADA_TEST_SQL_RESET_EXISTING_DATABASE

Push-Location $Root
try {
    Write-Host '==================================================='
    Write-Host ' Jornada - validação local Release'
    Write-Host " ScriptVersion: $ScriptVersion"
    Write-Host '==================================================='
    Write-Host "Raiz: $Root"
    Write-Host "dotnet: $(& dotnet --version)"
    Write-Host 'Solution contém Jornada.Integration.Tests: OK'
    Write-Host 'Alvo obrigatório de desenvolvimento/release: SQL_SERVER_2022 local/Testcontainers'
    Write-Host 'Fabric: homologação adicional e condicional; não é requisito deste gate'

    Invoke-Checked `
        -Command 'dotnet' `
        -CommandArgs @('restore', $Solution, '--locked-mode') `
        -Step '1/9 Restore locked da Solution'

    # Garante project.assets.json após local-clean.ps1.
    Invoke-Checked `
        -Command 'dotnet' `
        -CommandArgs @('restore', $UnitProject, '--locked-mode') `
        -Step '2/9 Restore locked Jornada.Tests'

    Invoke-Checked `
        -Command 'dotnet' `
        -CommandArgs @('restore', $IntegrationProject, '--locked-mode') `
        -Step '3/9 Restore locked Jornada.Integration.Tests'

    Invoke-Checked `
        -Command 'dotnet' `
        -CommandArgs @(
            'build', $Solution,
            '--configuration', 'Release',
            '--no-restore',
            '-warnaserror'
        ) `
        -Step '4/9 Build Release da Solution'

    Invoke-Checked `
        -Command 'dotnet' `
        -CommandArgs @(
            'build', $UnitProject,
            '--configuration', 'Release',
            '--no-restore',
            '-warnaserror'
        ) `
        -Step '5/9 Build explícito Jornada.Tests'

    Assert-FileExists -Path $UnitDll -Description 'DLL Unit'

    Invoke-Checked `
        -Command 'dotnet' `
        -CommandArgs @(
            'test', $UnitProject,
            '--configuration', 'Release',
            '--no-build',
            '--no-restore'
        ) `
        -Step '6/9 Testes Unit'

    Write-Host ''
    Write-Host '=== 7/9 Verificando Docker + imagem SQL + engine ==='
    if (-not (Test-DockerEngine)) {
        throw 'Docker Engine não está em execução ou não está acessível. Inicie o Docker Desktop antes dos testes de integração.'
    }
    Write-Host 'Docker Engine: OK'
    $canonicalSqlImage = Ensure-SqlServerImageAndRuntime
    # A validação Release força a mesma imagem por digest usada pelo Compose.
    # Qualquer override do operador é restaurado no finally.
    $env:JORNADA_TEST_SQL_IMAGE = $canonicalSqlImage

    Invoke-Checked `
        -Command 'dotnet' `
        -CommandArgs @(
            'build', $IntegrationProject,
            '--configuration', 'Release',
            '--no-restore',
            '-warnaserror'
        ) `
        -Step '8/9 Build explícito Jornada.Integration.Tests'

    Assert-FileExists -Path $IntegrationDll -Description 'DLL Integration'

    # Força o caminho descartável Testcontainers. A fixture cria
    # JornadaIntegration_Test_<guid>, que satisfaz os guards Test/Dev/Local e o prefixo de isolamento.
    Remove-Item Env:JORNADA_TEST_SQL_CONNECTION -ErrorAction SilentlyContinue
    Remove-Item Env:JORNADA_TEST_SQL_USE_EXISTING_DATABASE -ErrorAction SilentlyContinue
    Remove-Item Env:JORNADA_TEST_SQL_RESET_EXISTING_DATABASE -ErrorAction SilentlyContinue
    $env:JORNADA_TEST_SQL_TARGET = 'SQL_SERVER_2022'

    Invoke-Checked `
        -Command 'dotnet' `
        -CommandArgs @(
            'test', $IntegrationProject,
            '--configuration', 'Release',
            '--no-build',
            '--no-restore'
        ) `
        -Step '9/9 Testes Integration'

    Write-Host ''
    Write-Host '==================================================='
    Write-Host 'VALIDAÇÃO LOCAL RELEASE CONCLUÍDA COM SUCESSO'
    Write-Host "ScriptVersion:        $ScriptVersion"
    Write-Host 'Restore Solution:     OK'
    Write-Host 'Restore Unit:         OK'
    Write-Host 'Restore Integration:  OK'
    Write-Host 'Build Solution:       OK'
    Write-Host 'Build Unit:           OK'
    Write-Host 'Testes Unit:          OK'
    Write-Host 'Docker:               OK'
    Write-Host 'Imagem SQL/digest:     OK'
    Write-Host 'SQL engine + CHECKDB: OK'
    Write-Host 'Build Integration:    OK'
    Write-Host 'Testes Integration:   OK'
    Write-Host '==================================================='
}
finally {
    if ([string]::IsNullOrWhiteSpace($previousSqlConnection)) {
        Remove-Item Env:JORNADA_TEST_SQL_CONNECTION -ErrorAction SilentlyContinue
    }
    else {
        $env:JORNADA_TEST_SQL_CONNECTION = $previousSqlConnection
    }

    if ([string]::IsNullOrWhiteSpace($previousUseExisting)) {
        Remove-Item Env:JORNADA_TEST_SQL_USE_EXISTING_DATABASE -ErrorAction SilentlyContinue
    }
    else {
        $env:JORNADA_TEST_SQL_USE_EXISTING_DATABASE = $previousUseExisting
    }

    if ([string]::IsNullOrWhiteSpace($previousSqlImage)) {
        Remove-Item Env:JORNADA_TEST_SQL_IMAGE -ErrorAction SilentlyContinue
    }
    else {
        $env:JORNADA_TEST_SQL_IMAGE = $previousSqlImage
    }

    if ([string]::IsNullOrWhiteSpace($previousResetExisting)) {
        Remove-Item Env:JORNADA_TEST_SQL_RESET_EXISTING_DATABASE -ErrorAction SilentlyContinue
    }
    else {
        $env:JORNADA_TEST_SQL_RESET_EXISTING_DATABASE = $previousResetExisting
    }

    if ([string]::IsNullOrWhiteSpace($previousSqlTarget)) {
        Remove-Item Env:JORNADA_TEST_SQL_TARGET -ErrorAction SilentlyContinue
    }
    else {
        $env:JORNADA_TEST_SQL_TARGET = $previousSqlTarget
    }

    Pop-Location
}
