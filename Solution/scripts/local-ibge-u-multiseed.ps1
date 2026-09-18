param(
    [ValidateRange(10000, 5000000)]
    [int]$PairCount = 100000,

    [int[]]$Seeds = @(20260917, 20260918, 20260919, 20260920, 20260921),

    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Single = Join-Path $PSScriptRoot 'local-ibge-u-bootstrap.ps1'
$SourceReport = Join-Path $Root '.local\calibrador-ibge-u\ibge-u-bootstrap.json'
$OutDir = Join-Path $Root '.local\calibrador-ibge-u\multiseed'
$SummaryPath = Join-Path $OutDir 'summary.json'

function Add-ReportRows {
    param(
        [System.Collections.Generic.List[object]]$Rows,
        [object]$Report,
        [int]$Seed
    )

    foreach ($field in @('personName','motherName')) {
        foreach ($state in @('EXACT','HIGH','MEDIUM','LOW')) {
            $s = $Report.$field.states.$state
            if ($null -eq $s) { continue }
            $probability = if ($field -eq 'personName') {
                [double]$s.monteCarloProbability
            } else {
                [double]$s.monteCarloProbabilityGivenPresent
            }
            $Rows.Add([pscustomobject]@{
                seed = $Seed
                field = $field
                state = $state
                probability = $probability
                support = [long]$s.monteCarloSupport
            })
        }
    }
}

function Build-Summary {
    param([System.Collections.Generic.List[object]]$Rows)

    $summary = @()
    foreach ($group in ($Rows | Group-Object field,state)) {
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

    return @($summary)
}

if ($SelfTest) {
    $rows = [System.Collections.Generic.List[object]]::new()
    $report1 = [pscustomobject]@{
        personName = [pscustomobject]@{
            states = [pscustomobject]@{
                EXACT = [pscustomobject]@{ monteCarloProbability = 0.10; monteCarloSupport = 10 }
            }
        }
        motherName = [pscustomobject]@{
            states = [pscustomobject]@{
                EXACT = [pscustomobject]@{ monteCarloProbabilityGivenPresent = 0.20; monteCarloSupport = 20 }
            }
        }
    }
    $report2 = [pscustomobject]@{
        personName = [pscustomobject]@{
            states = [pscustomobject]@{
                EXACT = [pscustomobject]@{ monteCarloProbability = 0.30; monteCarloSupport = 30 }
            }
        }
        motherName = [pscustomobject]@{
            states = [pscustomobject]@{
                EXACT = [pscustomobject]@{ monteCarloProbabilityGivenPresent = 0.40; monteCarloSupport = 40 }
            }
        }
    }

    Add-ReportRows -Rows $rows -Report $report1 -Seed 1
    Add-ReportRows -Rows $rows -Report $report2 -Seed 2
    $summary = Build-Summary -Rows $rows

    $person = $summary | Where-Object { $_.field -eq 'personName' -and $_.state -eq 'EXACT' }
    $mother = $summary | Where-Object { $_.field -eq 'motherName' -and $_.state -eq 'EXACT' }

    if ($summary.Count -ne 2) { throw "Self-test: esperado summary.Count=2; obtido=$($summary.Count)." }
    if ($person.runs -ne 2 -or [Math]::Abs($person.mean - 0.20) -gt 1e-12 -or [Math]::Abs($person.min - 0.10) -gt 1e-12 -or [Math]::Abs($person.max - 0.30) -gt 1e-12) {
        throw 'Self-test: agregação personName/EXACT incorreta.'
    }
    if ([Math]::Abs($person.standardDeviation - [Math]::Sqrt(0.02)) -gt 1e-12) {
        throw 'Self-test: desvio-padrão amostral personName/EXACT incorreto.'
    }
    if ($mother.runs -ne 2 -or [Math]::Abs($mother.mean - 0.30) -gt 1e-12 -or [Math]::Abs($mother.min - 0.20) -gt 1e-12 -or [Math]::Abs($mother.max - 0.40) -gt 1e-12) {
        throw 'Self-test: agregação motherName/EXACT incorreta.'
    }

    Write-Host 'IBGE NOMINAL U MULTI-SEED SELF-TEST: OK' -ForegroundColor Green
    return
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$rows = [System.Collections.Generic.List[object]]::new()
foreach ($seed in $Seeds) {
    Write-Host "# .\scripts\local-ibge-u-bootstrap.ps1 -PairCount $PairCount -Seed $seed" -ForegroundColor DarkGray
    & $Single -PairCount $PairCount -Seed $seed
    if ($LASTEXITCODE -ne 0) { throw "Monte Carlo seed=$seed falhou ($LASTEXITCODE)." }

    $report = Get-Content -Raw -Encoding UTF8 $SourceReport | ConvertFrom-Json
    Copy-Item -LiteralPath $SourceReport -Destination (Join-Path $OutDir "ibge-u-bootstrap-$seed.json") -Force
    Add-ReportRows -Rows $rows -Report $report -Seed $seed
}

$summary = Build-Summary -Rows $rows

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
