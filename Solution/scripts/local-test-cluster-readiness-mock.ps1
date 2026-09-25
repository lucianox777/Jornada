# Regressão executável sob Windows PowerShell 5.1, sem Docker nem SQL reais.
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$envFile = Join-Path $Root '.env.example'
$global:JornadaReadinessMockExpectedSqlPassword = @(
    Get-Content -LiteralPath $envFile | Where-Object { $_ -match '^JORNADA_SQL_SA_PASSWORD=' }
)[0].Split('=', 2)[1].Trim('"')
$previousSqlcmdPassword = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD', 'Process')
$env:SQLCMDPASSWORD = 'PARENT_SCOPE_SENTINEL'
$global:JornadaReadinessMockSharedMarker = Join-Path ([IO.Path]::GetTempPath()) ('jornada-nas-mock-' + [Guid]::NewGuid().ToString('N'))
$global:JornadaReadinessMockNode2Database = 'JornadaLocal'
$global:JornadaReadinessMockSqlProbes = 0
$global:JornadaReadinessMockNasWrites = 0
$global:JornadaReadinessMockNasReads = 0

# Prevalece sobre o executável nativo somente neste processo de teste.
function docker {
    $argv = @($args)
    $global:LASTEXITCODE = 0
    if ($argv.Count -lt 2) { throw 'Mock Docker recebeu argumentos insuficientes.' }

    if ($argv[0] -eq 'compose') {
        if ($argv -contains 'ps') {
            switch ($argv[-1]) {
                'jornada-node1' { return 'mock-node1' }
                'jornada-node2' { return 'mock-node2' }
                default { throw 'Compose service desconhecido.' }
            }
        }
        if ($argv -contains 'exec') {
            $environmentIndex = [array]::IndexOf($argv, '-e')
            if ($environmentIndex -lt 0 -or $argv[$environmentIndex + 1] -cne 'SQLCMDPASSWORD') {
                throw 'Senha SQL deve ser herdada por nome, nunca incluída em argv do Docker.'
            }
            foreach ($arg in $argv) {
                if (([string]$arg).Contains($global:JornadaReadinessMockExpectedSqlPassword)) {
                    throw 'Senha SQL encontrada em argv do Docker.'
                }
            }
            if ($env:SQLCMDPASSWORD -cne $global:JornadaReadinessMockExpectedSqlPassword) {
                throw 'Senha SQL não chegou ao Docker por variável de ambiente.'
            }
            if ($argv -notcontains 'sqlserver' -or $argv -notcontains '-Q') {
                throw 'Preflight SQL não passou pelo SQL Server esperado.'
            }
            $sql = [string]$argv[-1]
            if (-not $sql.Contains('Jornada.EnvironmentProfile') -or
                -not $sql.Contains('configuration_bundle_version') -or
                -not $sql.Contains('SELECT COUNT_BIG(*)')) {
                throw 'Preflight SQL perdeu checagens somente leitura.'
            }
            $global:JornadaReadinessMockSqlProbes++
            return 'SQL DEV mock: OK'
        }
        throw 'Compose recebeu operação não permitida pelo preflight.'
    }

    if ($argv[0] -eq 'inspect') {
        $db = switch ($argv[1]) {
            'mock-node1' { 'JornadaLocal' }
            'mock-node2' { $global:JornadaReadinessMockNode2Database }
            default { throw 'Inspect de contêiner desconhecido.' }
        }
        $connection = "Server=sqlserver,1433;Database=$db;User Id=sa;Password=mock"
        # Docker inspect retorna um array JSON mesmo para um único contêiner.
        return (ConvertTo-Json -InputObject @(@{
            State = @{ Running = $true }
            Config = @{ Env = @(
                "ConnectionStrings__Jornada=$connection",
                "JORNADA_SQL_CONNECTION_STRING=$connection"
            ) }
        }) -Depth 5 -Compress)
    }

    if ($argv[0] -eq 'cp') {
        if ($argv.Count -ne 3) { throw 'docker cp exige origem e destino.' }
        $source = [string]$argv[1]
        $target = [string]$argv[2]
        $remotePattern = '^mock-node[12]:/data/bronze/\.jornada-readiness-[a-f0-9]{32}$'
        if ($source -match $remotePattern) {
            if (-not (Test-Path -LiteralPath $global:JornadaReadinessMockSharedMarker -PathType Leaf)) {
                throw 'NODE remoto tentou ler marcador ausente.'
            }
            Copy-Item -LiteralPath $global:JornadaReadinessMockSharedMarker -Destination $target -Force
            $global:JornadaReadinessMockNasReads++
            return
        }
        if ($target -match $remotePattern) {
            Copy-Item -LiteralPath $source -Destination $global:JornadaReadinessMockSharedMarker -Force
            $global:JornadaReadinessMockNasWrites++
            return
        }
        throw 'docker cp tentou acessar caminho fora do marcador NAS.'
    }

    if ($argv[0] -eq 'exec') {
        if ($argv.Count -ne 5 -or $argv[1] -notin @('mock-node1','mock-node2') -or
            $argv[2] -ne 'rm' -or $argv[3] -ne '-f' -or
            $argv[4] -notmatch '^/data/bronze/\.jornada-readiness-[a-f0-9]{32}$') {
            throw 'Cleanup do marcador fora do caminho temporário.'
        }
        Remove-Item -LiteralPath $global:JornadaReadinessMockSharedMarker -Force -ErrorAction SilentlyContinue
        return
    }

    throw 'Comando Docker inesperado no preflight mock.'
}

function Invoke-WebRequest {
    param([switch]$UseBasicParsing,[string]$Uri,[int]$TimeoutSec)
    if ($Uri -notin @('http://127.0.0.1:5080/health/ready',
                     'http://127.0.0.1:5180/health/ready')) {
        throw "Endpoint inesperado no mock: $Uri"
    }
    return [pscustomobject]@{ StatusCode = 200 }
}

try {
    & (Join-Path $Root 'scripts/local-cluster-readiness.ps1') -DatabaseName JornadaLocal -EnvFile $envFile
    if ($global:JornadaReadinessMockSqlProbes -ne 1 -or $global:JornadaReadinessMockNasWrites -ne 2 -or $global:JornadaReadinessMockNasReads -ne 2) {
        throw "Preflight completo incompleto: SQL=$global:JornadaReadinessMockSqlProbes NASW=$global:JornadaReadinessMockNasWrites NASR=$global:JornadaReadinessMockNasReads"
    }
    if (Test-Path -LiteralPath $global:JornadaReadinessMockSharedMarker) {
        throw 'O preflight deixou o marcador NAS sem cleanup.'
    }
    if ($env:SQLCMDPASSWORD -cne 'PARENT_SCOPE_SENTINEL') {
        throw 'O preflight não restaurou SQLCMDPASSWORD após sucesso.'
    }

    # NODE2 aponta para outro banco: rejeitar ANTES de consultar SQL ou escrever NAS.
    $global:JornadaReadinessMockNode2Database = 'OutroBanco'
    $blocked = $false
    try {
        & (Join-Path $Root 'scripts/local-cluster-readiness.ps1') -DatabaseName JornadaLocal -EnvFile $envFile
    }
    catch {
        if ($_.Exception.Message -notmatch 'jornada-node2 não está conectado ao banco original') { throw }
        $blocked = $true
    }
    if (-not $blocked -or $global:JornadaReadinessMockSqlProbes -ne 1 -or $global:JornadaReadinessMockNasWrites -ne 2) {
        throw 'Divergência de banco não interrompeu o preflight antes dos efeitos.'
    }
    if ($env:SQLCMDPASSWORD -cne 'PARENT_SCOPE_SENTINEL') {
        throw 'O preflight não preservou SQLCMDPASSWORD após falha antecipada.'
    }
    Write-Host 'WINDOWS POWERSHELL 5.1 CLUSTER READINESS MOCK: OK'
}
finally {
    Remove-Item -LiteralPath $global:JornadaReadinessMockSharedMarker -Force -ErrorAction SilentlyContinue
    if ($null -eq $previousSqlcmdPassword) {
        Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
    } else {
        $env:SQLCMDPASSWORD = $previousSqlcmdPassword
    }
    Remove-Variable JornadaReadinessMockExpectedSqlPassword -Scope Global -ErrorAction SilentlyContinue
}
