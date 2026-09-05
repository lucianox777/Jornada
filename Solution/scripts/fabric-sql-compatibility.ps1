param(
    [string]$ConnectionString
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Project = 'tests/Jornada.Integration.Tests/Jornada.Integration.Tests.csproj'
$EvidenceDir = Join-Path $Root '.local/fabric-sql-compatibility'
$LocalConnectionFile = Join-Path $EvidenceDir 'connection.txt'
$Trx = Join-Path $EvidenceDir 'fabric-integration.trx'
$Summary = Join-Path $EvidenceDir 'summary.json'
$DeviceCodeFile = Join-Path $EvidenceDir 'device-code.txt'

function Invoke-DotNetStep {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    Write-Host $Label
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Falha em: $Label (exit code $LASTEXITCODE)."
    }
}

function Get-FabricConnectionString {
    param([string]$ExplicitConnectionString)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitConnectionString)) {
        return $ExplicitConnectionString.Trim()
    }

    # Configuração local é opcional. O diretório .local/ é ignorado pelo Git e
    # não é distribuído no pacote de release.
    if (Test-Path -LiteralPath $LocalConnectionFile) {
        $local = (Get-Content -LiteralPath $LocalConnectionFile -Raw).Trim()
        if (-not [string]::IsNullOrWhiteSpace($local)) {
            return $local
        }
    }

    # Compatibilidade com automação/CI que injete a conexão externamente.
    if (-not [string]::IsNullOrWhiteSpace($env:JORNADA_FABRIC_SQL_CONNECTION)) {
        return $env:JORNADA_FABRIC_SQL_CONNECTION.Trim()
    }

    throw ("Connection string Fabric não encontrada. O Fabric não é requisito para a validação local. " +
           "Para homologação Fabric, informe -ConnectionString, defina JORNADA_FABRIC_SQL_CONNECTION " +
           "ou crie localmente $LocalConnectionFile.")
}

function Normalize-FabricConnectionString {
    param([Parameter(Mandatory = $true)][string]$Value)

    $normalized = $Value.Trim().TrimEnd(';')

    if ($normalized -notmatch '(?i)(?:^|;)\s*(?:Initial Catalog|Database)\s*=\s*([^;]+)') {
        throw 'A connection string Fabric deve informar Initial Catalog/Database.'
    }

    $databaseName = $Matches[1].Trim().Trim('"').Trim("'")
    if ($databaseName -notmatch '(?i)(Test|Dev|Local)') {
        throw "Por segurança, o banco Fabric de compatibilidade deve conter Test, Dev ou Local no nome. Banco: $databaseName"
    }

    # Device Code Flow é deliberado para o testhost: não depende de VisualStudioCredential,
    # Azure CLI nem de popup interativo dentro do processo de teste.
    if ($normalized -match '(?i)(?:^|;)\s*Authentication\s*=') {
        $normalized = [regex]::Replace(
            $normalized,
            '(?i)(^|;)\s*Authentication\s*=\s*[^;]*',
            '$1Authentication=Active Directory Device Code Flow')
    }
    else {
        $normalized += ';Authentication=Active Directory Device Code Flow'
    }

    # A Microsoft recomenda timeout suficiente para concluir o fluxo de código do dispositivo.
    if ($normalized -match '(?i)(?:^|;)\s*Connect Timeout\s*=') {
        $normalized = [regex]::Replace(
            $normalized,
            '(?i)(^|;)\s*Connect Timeout\s*=\s*[^;]*',
            '$1Connect Timeout=300')
    }
    else {
        $normalized += ';Connect Timeout=300'
    }

    return $normalized + ';'
}

function Write-TrxSummary {
    param(
        [Parameter(Mandatory = $true)][string]$TrxPath,
        [Parameter(Mandatory = $true)][string]$SummaryPath
    )

    if (-not (Test-Path -LiteralPath $TrxPath)) {
        throw "TRX não foi gerado: $TrxPath"
    }

    [xml]$xml = Get-Content -LiteralPath $TrxPath -Raw
    $resultSummary = $xml.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']")
    $counters = $xml.SelectSingleNode("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
    if ($null -eq $resultSummary -or $null -eq $counters) {
        throw 'TRX inválido: ResultSummary/Counters não encontrado.'
    }

    $summary = [ordered]@{
        target      = 'FABRIC_SQL_DATABASE'
        total       = [int]$counters.total
        executed    = [int]$counters.executed
        passed      = [int]$counters.passed
        failed      = [int]$counters.failed
        notExecuted = [int]$counters.notExecuted
        outcome     = [string]$resultSummary.outcome
        trx         = $TrxPath
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    }

    $summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $SummaryPath -Encoding UTF8

    Write-Host ("Fabric Integration: total={0}, passed={1}, failed={2}, notExecuted={3}" -f `
        $summary.total, $summary.passed, $summary.failed, $summary.notExecuted)

    if ($summary.total -lt 1 -or
        $summary.executed -ne $summary.total -or
        $summary.failed -ne 0 -or
        $summary.notExecuted -ne 0 -or
        $summary.passed -ne $summary.total) {
        throw 'A suíte Fabric não terminou com 100% dos testes executados e aprovados.'
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK 8 é necessário.'
}

New-Item -ItemType Directory -Force -Path $EvidenceDir | Out-Null
$fabricConnection = Normalize-FabricConnectionString (Get-FabricConnectionString $ConnectionString)

$previousFabricConnection = $env:JORNADA_FABRIC_SQL_CONNECTION
$previousConnection = $env:JORNADA_TEST_SQL_CONNECTION
$previousUseExisting = $env:JORNADA_TEST_SQL_USE_EXISTING_DATABASE
$previousResetExisting = $env:JORNADA_TEST_SQL_RESET_EXISTING_DATABASE
$previousTarget = $env:JORNADA_TEST_SQL_TARGET

# O usuário executa um único comando. Todas as variáveis internas são definidas aqui.
$env:JORNADA_FABRIC_SQL_CONNECTION = $fabricConnection
$env:JORNADA_TEST_SQL_CONNECTION = $fabricConnection
$env:JORNADA_TEST_SQL_USE_EXISTING_DATABASE = 'true'
$env:JORNADA_TEST_SQL_RESET_EXISTING_DATABASE = 'true'
$env:JORNADA_TEST_SQL_TARGET = 'FABRIC_SQL_DATABASE'

Push-Location $Root
try {
    Write-Host '==================================================='
    Write-Host 'JORNADA - SQL DATABASE IN MICROSOFT FABRIC'
    Write-Host 'Autenticação: Microsoft Entra Device Code Flow'
    Write-Host 'Banco Test: reset automático antes da suíte'
    Write-Host 'Python: NÃO utilizado'
    Write-Host '==================================================='

    # O projeto Integration é restaurado e compilado explicitamente para evitar
    # qualquer execução de DLL residual com --no-build.
    Invoke-DotNetStep -Label 'Fabric SQL: restore Solution --locked-mode' -Arguments @(
        'restore', 'Jornada.sln', '--locked-mode')
    Invoke-DotNetStep -Label 'Fabric SQL: restore Integration --locked-mode' -Arguments @(
        'restore', $Project, '--locked-mode')

    Invoke-DotNetStep -Label 'Fabric SQL: build Solution Release' -Arguments @(
        'build', 'Jornada.sln', '--configuration', 'Release', '--no-restore', '-warnaserror')
    Invoke-DotNetStep -Label 'Fabric SQL: build Integration Release' -Arguments @(
        'build', $Project, '--configuration', 'Release', '--no-restore', '-warnaserror')

    Remove-Item -LiteralPath $Trx -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $Summary -ErrorAction SilentlyContinue

    Write-Host 'Fabric SQL: Integration sem skips'
    Write-Host 'A autenticação Entra será exibida AQUI no PowerShell; não é necessário criar variáveis manualmente.'

    Remove-Item -LiteralPath $DeviceCodeFile -ErrorAction SilentlyContinue
    $previousDeviceCodeFile = $env:JORNADA_FABRIC_DEVICE_CODE_FILE
    $env:JORNADA_FABRIC_DEVICE_CODE_FILE = $DeviceCodeFile

    try {
        # O VSTest/NUnit captura stdout do testhost. Por isso o callback C# grava a mensagem
        # de Device Code em arquivo local e este processo pai a retransmite imediatamente
        # para o console, enquanto o testhost continua aguardando a autenticação.
        $testArguments = @(
            'test',
            $Project,
            '--configuration', 'Release',
            '--no-build',
            '--logger', "trx;LogFileName=$Trx"
        )

        $testProcess = Start-Process `
            -FilePath 'dotnet' `
            -ArgumentList $testArguments `
            -WorkingDirectory $Root `
            -NoNewWindow `
            -PassThru

        $deviceCodeShown = $false

        while (-not $testProcess.HasExited) {
            if (-not $deviceCodeShown -and (Test-Path -LiteralPath $DeviceCodeFile)) {
                $deviceMessage = (Get-Content -LiteralPath $DeviceCodeFile -Raw).Trim()
                if (-not [string]::IsNullOrWhiteSpace($deviceMessage)) {
                    Write-Host ''
                    Write-Host '==================================================='
                    Write-Host 'MICROSOFT ENTRA - DEVICE CODE'
                    Write-Host $deviceMessage
                    Write-Host 'Conclua o login com a identidade autorizada no banco Fabric de compatibilidade.'
                    Write-Host 'O teste continuará automaticamente após a autenticação.'
                    Write-Host '==================================================='
                    Write-Host ''

                    # Conveniência: abre a página indicada pelo próprio Microsoft Entra.
                    # Se a abertura automática falhar, a URL e o código continuam visíveis acima.
                    if ($deviceMessage -match '(https?://[^\s]+)') {
                        try {
                            Start-Process $Matches[1] | Out-Null
                        }
                        catch {
                            Write-Host "Não foi possível abrir o navegador automaticamente: $($_.Exception.Message)"
                        }
                    }

                    $deviceCodeShown = $true
                }
            }

            Start-Sleep -Milliseconds 250
            $testProcess.Refresh()
        }

        $testProcess.WaitForExit()
        # Em Windows PowerShell 5.1, Start-Process -PassThru pode deixar ExitCode
        # indisponível/null mesmo após HasExited=True. Refresh após WaitForExit reduz
        # esse comportamento; ainda assim, o TRX validado abaixo é a evidência
        # autoritativa da suíte quando ExitCode não estiver disponível.
        $testProcess.Refresh()
        try {
            $testExitCode = $testProcess.ExitCode
        }
        catch {
            $testExitCode = $null
        }
    }
    finally {
        if ($null -eq $previousDeviceCodeFile) {
            Remove-Item Env:JORNADA_FABRIC_DEVICE_CODE_FILE -ErrorAction SilentlyContinue
        }
        else {
            $env:JORNADA_FABRIC_DEVICE_CODE_FILE = $previousDeviceCodeFile
        }
    }

    # Primeiro valida o TRX: ele precisa comprovar 100% executado e aprovado.
    Write-TrxSummary -TrxPath $Trx -SummaryPath $Summary

    if ($null -ne $testExitCode) {
        if ([int]$testExitCode -ne 0) {
            throw "dotnet test retornou exit code $testExitCode. Consulte $Trx"
        }
    }
    else {
        Write-Host 'Aviso: ExitCode do processo dotnet não ficou disponível no Windows PowerShell; TRX 100% aprovado foi aceito como evidência autoritativa.'
    }

    Write-Host '==================================================='
    Write-Host 'FABRIC SQL COMPATIBILITY: OK'
    Write-Host "Evidência: $Trx"
    Write-Host "Resumo:    $Summary"
    Write-Host '==================================================='
}
finally {
    Pop-Location

    if ($null -eq $previousFabricConnection) { Remove-Item Env:JORNADA_FABRIC_SQL_CONNECTION -ErrorAction SilentlyContinue } else { $env:JORNADA_FABRIC_SQL_CONNECTION = $previousFabricConnection }
    if ($null -eq $previousConnection) { Remove-Item Env:JORNADA_TEST_SQL_CONNECTION -ErrorAction SilentlyContinue } else { $env:JORNADA_TEST_SQL_CONNECTION = $previousConnection }
    if ($null -eq $previousUseExisting) { Remove-Item Env:JORNADA_TEST_SQL_USE_EXISTING_DATABASE -ErrorAction SilentlyContinue } else { $env:JORNADA_TEST_SQL_USE_EXISTING_DATABASE = $previousUseExisting }
    if ($null -eq $previousResetExisting) { Remove-Item Env:JORNADA_TEST_SQL_RESET_EXISTING_DATABASE -ErrorAction SilentlyContinue } else { $env:JORNADA_TEST_SQL_RESET_EXISTING_DATABASE = $previousResetExisting }
    if ($null -eq $previousTarget) { Remove-Item Env:JORNADA_TEST_SQL_TARGET -ErrorAction SilentlyContinue } else { $env:JORNADA_TEST_SQL_TARGET = $previousTarget }
}
