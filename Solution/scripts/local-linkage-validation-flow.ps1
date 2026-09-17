param()

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Cluster = Join-Path $PSScriptRoot 'local-cluster.ps1'
$Validation = Join-Path $PSScriptRoot 'local-linkage-validation.ps1'
$EnvFile = Join-Path $Root '.env'

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Command,
        [Parameter(Mandatory = $true)][string]$Label
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Label falhou ($LASTEXITCODE)."
    }
}

function Get-SqlPassword {
    if (-not (Test-Path -LiteralPath $EnvFile)) {
        throw ".env ausente em $EnvFile. Execute o bootstrap local antes de continuar."
    }

    $line = Get-Content -LiteralPath $EnvFile | Where-Object { $_ -match '^JORNADA_SQL_SA_PASSWORD=' } | Select-Object -First 1
    if (-not $line) {
        throw 'JORNADA_SQL_SA_PASSWORD ausente no .env.'
    }

    return $line.Substring('JORNADA_SQL_SA_PASSWORD='.Length)
}

function Get-SqlScalar {
    param([Parameter(Mandatory = $true)][string]$Query)

    $password = Get-SqlPassword
    Write-Host "# docker compose --env-file $EnvFile exec -T -e 'SQLCMDPASSWORD=<redacted>' sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d JornadaLocal -W -h -1 -Q '<query>'"
    $output = & docker compose --env-file $EnvFile exec -T -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d JornadaLocal -W -h -1 -Q $Query
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd falhou ($LASTEXITCODE)."
    }

    return (($output | Where-Object { $_ -and $_ -notmatch '^[- ]+$' } | Select-Object -Last 1).Trim())
}

Write-Host '# .\scripts\local-cluster.ps1 -Action up'
Invoke-Checked -Label '.\scripts\local-cluster.ps1 -Action up' -Command { & $Cluster -Action up }

$activeModel = Get-SqlScalar "SET NOCOUNT ON; SELECT COUNT(*) FROM identidade.modelo_linkage WHERE status='ATIVO' AND ISNULL(amostra_metodo,'')<>'SEED_DEV_FIXO_NAO_TREINADO';"

if ([int]$activeModel -eq 0) {
    $validationRows = Get-SqlScalar "SET NOCOUNT ON; SELECT COUNT(*) FROM silver.pessoa_observacao WHERE codigo_pessoa_origem LIKE 'SCALE-VAL-%';"
    if ([int]$validationRows -ne 0) {
        throw 'Há corpus SCALE-VAL persistido, mas não existe modelo calibrado ATIVO. Não é seguro recalibrar sobre o corpus de validação. Use um ambiente limpo ou restaure o modelo esperado.'
    }

    Write-Host '# .\scripts\local-cluster.ps1 -Action calibrate'
    Invoke-Checked -Label '.\scripts\local-cluster.ps1 -Action calibrate' -Command { & $Cluster -Action calibrate }
} else {
    Write-Host 'Modelo calibrado ATIVO já existe; preservando-o para manter a validação independente.'
}

Write-Host '# .\scripts\local-linkage-validation.ps1'
Invoke-Checked -Label '.\scripts\local-linkage-validation.ps1' -Command { & $Validation }
