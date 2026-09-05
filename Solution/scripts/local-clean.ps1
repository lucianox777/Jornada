param()

$ErrorActionPreference = 'Stop'
$ScriptVersion = '2026.09.04-v4.03'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'
$ComposeFile = Join-Path $Root 'docker-compose.yml'

Write-Host '==================================================='
Write-Host ' Jornada - limpeza completa do ambiente local'
Write-Host " ScriptVersion: $ScriptVersion"
Write-Host '==================================================='
Write-Host "Raiz: $Root"

function Invoke-NativeBestEffort {
    param(
        [Parameter(Mandatory=$true)][string]$Command,
        [Parameter(Mandatory=$true)][string[]]$CommandArgs,
        [Parameter(Mandatory=$true)][string]$Description
    )

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $Command @CommandArgs
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($exitCode -ne 0) {
        Write-Warning "$Description retornou código $exitCode. A limpeza continuará."
        return $false
    }

    return $true
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

function Remove-DirectoryIfExists {
    param([Parameter(Mandatory=$true)][string]$Path)
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
        Write-Host "Removido: $Path"
    }
}

function Remove-FileIfExists {
    param([Parameter(Mandatory=$true)][string]$Path)
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Force
        Write-Host "Removido: $Path"
    }
}

function Remove-DirectoryBestEffort {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $true
    }

    try {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
        Write-Host "Removido: $Path"
        return $true
    }
    catch {
        Write-Warning "$Description não pôde ser removido completamente: $($_.Exception.Message)"
        Write-Warning 'Esse cache não é necessário para restore/build/test. A limpeza continuará.'
        return $false
    }
}

# Remove somente recursos Docker locais da Jornada.
# A imagem SQL Server permanece em cache e NÃO é removida.
if (Test-DockerEngine) {
    Write-Host ''
    Write-Host '=== Docker: removendo recursos locais da Jornada ==='

    Push-Location $Root
    try {
        if ((Test-Path -LiteralPath $ComposeFile) -and (Test-Path -LiteralPath $EnvFile)) {
            [void](Invoke-NativeBestEffort `
                -Command 'docker' `
                -CommandArgs @('compose','--env-file',$EnvFile,'down','-v','--remove-orphans') `
                -Description 'docker compose down')
        }

        # Fallback para execução interrompida, .env ausente ou Compose parcial.
        $containerIds = @(& docker ps -aq --filter 'label=com.docker.compose.project=jornada-local' 2>$null)
        if ($containerIds.Count -gt 0) {
            [void](Invoke-NativeBestEffort `
                -Command 'docker' `
                -CommandArgs (@('rm','-f') + $containerIds) `
                -Description 'remoção dos containers Compose da Jornada')
        }

        $namedContainer = @(& docker ps -aq --filter 'name=^/jornada-sqlserver-local$' 2>$null)
        if ($namedContainer.Count -gt 0) {
            [void](Invoke-NativeBestEffort `
                -Command 'docker' `
                -CommandArgs (@('rm','-f') + $namedContainer) `
                -Description 'remoção do container jornada-sqlserver-local')
        }

        $volumeIds = @(& docker volume ls -q --filter 'name=^jornada_sql_data$' 2>$null)
        if ($volumeIds.Count -gt 0) {
            [void](Invoke-NativeBestEffort `
                -Command 'docker' `
                -CommandArgs @('volume','rm','-f','jornada_sql_data') `
                -Description 'remoção do volume jornada_sql_data')
        }

        $networkIds = @(& docker network ls -q --filter 'name=^jornada-local_default$' 2>$null)
        if ($networkIds.Count -gt 0) {
            [void](Invoke-NativeBestEffort `
                -Command 'docker' `
                -CommandArgs @('network','rm','jornada-local_default') `
                -Description 'remoção da rede jornada-local_default')
        }
    }
    finally {
        Pop-Location
    }
}
elseif (Get-Command docker -ErrorAction SilentlyContinue) {
    Write-Warning 'Docker está instalado, mas o Engine não está acessível. A limpeza de arquivos locais continuará.'
}
else {
    Write-Warning 'Docker não foi encontrado. A limpeza de arquivos locais continuará.'
}

Write-Host ''
Write-Host '=== Arquivos e saídas locais ==='

Remove-FileIfExists -Path $EnvFile

foreach ($dir in @('.local','TestResults','artifacts','dist')) {
    Remove-DirectoryIfExists -Path (Join-Path $Root $dir)
}

# .vs é apenas cache local do Visual Studio e pode conter arquivos abertos por
# Visual Studio/Copilot/ServiceHub (por exemplo CopilotIndices\CodeChunks.db).
# A impossibilidade de apagá-lo NÃO invalida uma limpeza de build/test.
[void](Remove-DirectoryBestEffort `
    -Path (Join-Path $Root '.vs') `
    -Description 'Cache .vs do Visual Studio')

# TestResults pode existir abaixo de projetos.
Get-ChildItem -LiteralPath $Root -Directory -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq 'TestResults' } |
    Sort-Object { $_.FullName.Length } -Descending |
    ForEach-Object {
        if (Test-Path -LiteralPath $_.FullName) {
            Remove-Item -LiteralPath $_.FullName -Recurse -Force
            Write-Host "Removido: $($_.FullName)"
        }
    }

# bin/obj são removidos de propósito. O validador canônico executa restore
# locked explícito dos projetos de teste antes de recompilar.
Get-ChildItem -LiteralPath $Root -Directory -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @('bin','obj') } |
    Sort-Object { $_.FullName.Length } -Descending |
    ForEach-Object {
        if (Test-Path -LiteralPath $_.FullName) {
            Remove-Item -LiteralPath $_.FullName -Recurse -Force
            Write-Host "Removido: $($_.FullName)"
        }
    }

$BronzeDir = Join-Path $Root 'data\bronze'
if (Test-Path -LiteralPath $BronzeDir) {
    Get-ChildItem -LiteralPath $BronzeDir -Force |
        Where-Object { $_.Name -ne '.gitkeep' } |
        ForEach-Object {
            Remove-Item -LiteralPath $_.FullName -Recurse -Force
        }
    Write-Host "Limpo: $BronzeDir (preservado .gitkeep)"
}

# Remove apenas variantes temporárias criadas durante o diagnóstico.
# A versão canônica distribuída é scripts\local-validate-release.ps1.
foreach ($legacy in @(
    'local-validate-release-v2.ps1',
    'local-validate-release-r4.ps1'
)) {
    Remove-FileIfExists -Path (Join-Path $PSScriptRoot $legacy)
}

# Estado de seleção de banco de Integration não deve sobreviver a uma limpeza.
# DOCKER_API_VERSION é preservada porque pode ser override explícito do operador.
Remove-Item Env:JORNADA_TEST_SQL_CONNECTION -ErrorAction SilentlyContinue
Remove-Item Env:JORNADA_TEST_SQL_USE_EXISTING_DATABASE -ErrorAction SilentlyContinue

Write-Host ''
Write-Host '==================================================='
Write-Host 'LIMPEZA CONCLUÍDA'
Write-Host 'Imagem Docker do SQL Server preservada; local-validate-release.ps1 validará digest e engine.'
Write-Host 'Para validar: .\scripts\local-validate-release.ps1'
Write-Host '==================================================='
