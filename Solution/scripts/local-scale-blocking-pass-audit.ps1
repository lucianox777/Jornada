param(
    [int]$LabelCount = 500
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'

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
        if ($LASTEXITCODE -eq 0) { return @{ Exe = $command.Source; Prefix = $prefix } }
    }
    throw 'Python 3 não encontrado (tentados: python3, python, py -3).'
}

function Get-EnvValues {
    if (-not (Test-Path -LiteralPath $EnvFile)) { throw '.env não encontrado.' }
    $vars = @{}
    Get-Content -LiteralPath $EnvFile | ForEach-Object {
        $line = $_.Trim()
        if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
            $parts = $line.Split('=', 2)
            $vars[$parts[0].Trim()] = $parts[1]
        }
    }
    return $vars
}

if ($LabelCount -le 0) { throw 'LabelCount deve ser > 0.' }
foreach ($command in @('docker', 'dotnet')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { throw "Comando '$command' não encontrado no PATH." }
}

$Python3 = Resolve-Python3
$vars = Get-EnvValues
$password = $vars['JORNADA_SQL_SA_PASSWORD']
$port = if ($vars['JORNADA_SQL_PORT']) { $vars['JORNADA_SQL_PORT'] } else { '14333' }
$db = if ($vars['JORNADA_SQL_DATABASE']) { $vars['JORNADA_SQL_DATABASE'] } else { 'JornadaLocal' }
if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD não definido em .env.' }
$conn = "Server=localhost,$port;Database=$db;User Id=sa;Password=$password;TrustServerCertificate=true;Encrypt=false"
$outDir = Join-Path $Root '.local/performance'
New-Item -ItemType Directory -Force $outDir | Out-Null
$labelsPath = Join-Path $outDir 'scale-blocking-pass-labels.csv'
$auditPath = Join-Path $outDir 'scale-blocking-pass-audit.json'
$summaryPath = Join-Path $outDir 'scale-blocking-pass-fanout-summary.json'

function Invoke-SqlLines([string]$Query) {
    Push-Location $Root
    try {
        $lines = @(& docker compose --env-file .env exec -T -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d $db -W -h -1 -y 0 -w 65535 -Q "SET NOCOUNT ON; $Query")
        if ($LASTEXITCODE -ne 0) { throw 'sqlcmd falhou.' }
        return @($lines | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }
    finally { Pop-Location }
}

$goldPopulationRaw = Invoke-SqlLines 'SELECT COUNT_BIG(*) FROM gold.pessoa;'
if ($goldPopulationRaw.Count -ne 1) { throw 'Não foi possível determinar população Gold SCALE.' }
$goldPopulation = [int64]$goldPopulationRaw[0]
if ($goldPopulation -le 0) { throw 'População Gold SCALE deve ser > 0.' }

$labelQuery = @"
WITH pend AS (
    SELECT TOP ($LabelCount)
           po.pessoa_observacao_id,
           po.codigo_pessoa_origem
    FROM silver.pessoa_observacao po
    WHERE po.cpf IS NULL
      AND po.codigo_pessoa_origem LIKE N'SCALE-PEND-%'
    ORDER BY po.codigo_pessoa_origem, po.pessoa_observacao_id
)
SELECT CONCAT(
    p.pessoa_observacao_id, N',', CONVERT(nvarchar(36), tv.pessoa_uuid)
)
FROM pend p
JOIN silver.pessoa_observacao truth_po
  ON truth_po.codigo_pessoa_origem = REPLACE(p.codigo_pessoa_origem, N'SCALE-PEND-', N'SCALE-SEHAB-')
JOIN ref.gestor truth_g
  ON truth_g.gestor_id = truth_po.gestor_id
 AND truth_g.codigo = N'SEHAB'
JOIN identidade.v_vinculo_corrente tv
  ON tv.pessoa_observacao_id = truth_po.pessoa_observacao_id
 AND tv.status = N'RESOLVIDO'
 AND tv.pessoa_uuid IS NOT NULL
ORDER BY p.codigo_pessoa_origem, p.pessoa_observacao_id;
"@
$labelRows = Invoke-SqlLines $labelQuery
if ($labelRows.Count -ne $LabelCount) {
    throw "Amostra SCALE incompleta para auditoria por passe: esperados=$LabelCount; obtidos=$($labelRows.Count)."
}
$labelContent = (@('pessoa_observacao_id,pessoa_uuid_verdade') + $labelRows) -join [Environment]::NewLine
[IO.File]::WriteAllText($labelsPath, $labelContent + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

$oldConnection = $env:ConnectionStrings__Jornada
try {
    $env:ConnectionStrings__Jornada = $conn
    Push-Location $Root
    try {
        Write-Host ">>> dotnet run --project src/Jornada.Linkage.Runner --configuration Release --no-build -- --blocking-pass-audit-labels $labelsPath --blocking-pass-audit-output $auditPath"
        dotnet run --project src/Jornada.Linkage.Runner --configuration Release --no-build -- `
            --blocking-pass-audit-labels $labelsPath `
            --blocking-pass-audit-output $auditPath `
            --ProbabilisticLinkage:CommandTimeoutSeconds 300
        if ($LASTEXITCODE -ne 0) { throw "BlockingPassAuditCommand falhou ($LASTEXITCODE)." }

        $gateArgs = @($Python3.Prefix) + @(
            'scripts/blocking_pass_fanout_gate.py',
            $auditPath,
            '--gold-population', "$goldPopulation",
            '--summary', $summaryPath
        )
        if ($env:JORNADA_BLOCKING_MAX_POPULATION_FRACTION) {
            $gateArgs += @('--max-population-fraction', $env:JORNADA_BLOCKING_MAX_POPULATION_FRACTION)
        }
        if ($env:JORNADA_BLOCKING_MAX_P95_CANDIDATES) {
            $gateArgs += @('--max-p95-candidates', $env:JORNADA_BLOCKING_MAX_P95_CANDIDATES)
        }
        Write-Host ">>> $($Python3.Exe) $($gateArgs -join ' ')"
        & $Python3.Exe @gateArgs
        if ($LASTEXITCODE -ne 0) { throw "blocking_pass_fanout_gate.py falhou ($LASTEXITCODE)." }
    }
    finally { Pop-Location }
}
finally {
    $env:ConnectionStrings__Jornada = $oldConnection
}

Write-Host "SCALE BLOCKING PASS AUDIT: OK (labels=$LabelCount; goldPopulation=$goldPopulation)" -ForegroundColor Green
Write-Host "Audit: $auditPath"
Write-Host "Gate:  $summaryPath"
