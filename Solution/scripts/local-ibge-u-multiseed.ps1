param(
    [ValidateRange(10000, 5000000)]
    [int]$PairCount = 100000,

    [int[]]$Seeds = @(20260917, 20260918, 20260919, 20260920, 20260921)
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Single = Join-Path $PSScriptRoot 'local-ibge-u-bootstrap.ps1'
$SourceReport = Join-Path $Root '.local\calibrador-ibge-u\ibge-u-bootstrap.json'
$OutDir = Join-Path $Root '.local\calibrador-ibge-u\multiseed'
$SummaryPath = Join-Path $OutDir 'summary.json'

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$rows = @()
foreach ($seed in $Seeds) {
    Write-Host "# .\scripts\local-ibge-u-bootstrap.ps1 -PairCount $PairCount -Seed $seed" -ForegroundColor DarkGray
    & $Single -PairCount $PairCount -Seed $seed
    if ($LASTEXITCODE -ne 0) { throw "Monte Carlo seed=$seed falhou ($LASTEXITCODE)." }

    $report = Get-Content -Raw -Encoding UTF8 $SourceReport | ConvertFrom-Json
    Copy-Item -LiteralPath $SourceReport -Destination (Join-Path $OutDir "ibge-u-bootstrap-$seed.json") -Force

    foreach ($field in @('personName','motherName')) {
        foreach ($state in @('EXACT','HIGH','MEDIUM','LOW')) {
            $s = $report.$field.states.$state
            if ($null -eq $s) { continue }
            $probability = if ($field -eq 'personName') { [double]$s.monteCarloProbability } else { [double]$s.monteCarloProbabilityGivenPresent }
            $rows += [pscustomobject]@{ seed=$seed; field=$field; state=$state; probability=$probability; support=[long]$s.monteCarloSupport }
        }
    }
}

$summary = @()
foreach ($group in ($rows | Group-Object field,state)) {
    $values = @($group.Group | ForEach-Object { [double]$_.probability })
    $mean = ($values | Measure-Object -Average).Average
    $variance = if ($values.Count -gt 1) {
        (($values | ForEach-Object { ($_ - $mean) * ($_ - $mean) } | Measure-Object -Sum).Sum) / ($values.Count - 1)
    } else { 0.0 }
    $summary += [pscustomobject]@{
        field = $group.Group[0].field
        state = $group.Group[0].state
        runs = $values.Count
        mean = $mean
        standardDeviation = [Math]::Sqrt($variance)
        min = ($values | Measure-Object -Minimum).Minimum
        max = ($values | Measure-Object -Maximum).Maximum
    }
}

$result = [ordered]@{
    schemaVersion = 'JORNADA_IBGE_U_MULTISEED_DIAGNOSTIC_V1'
    purpose = 'DEV_MONTE_CARLO_VARIANCE_DIAGNOSTIC_NO_AUTOMATIC_PROMOTION'
    pairCount = $PairCount
    seeds = $Seeds
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    summary = $summary
    safeguards = @(
        'read-only: não cria, valida ou ativa modelo',
        'não altera threshold, margem ou parâmetros persistidos',
        'dispersão entre seeds é diagnóstico DEV, não homologação populacional'
    )
}
$result | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $SummaryPath

$summary | Sort-Object field,state | Format-Table field,state,runs,mean,standardDeviation,min,max -AutoSize
Write-Host "Relatório multi-seed: $SummaryPath"
Write-Host 'IBGE NOMINAL U MULTI-SEED DIAGNOSTIC: OK' -ForegroundColor Green
