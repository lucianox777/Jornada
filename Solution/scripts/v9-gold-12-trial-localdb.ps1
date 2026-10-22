#requires -Version 7.2
<#
.SYNOPSIS
  Manual TRAIN-only 3 orders x (current + 3 Pareto finalists) Gold physical replay.
.DESCRIPTION
  Requires a Windows machine with SQL Server LocalDB installed and a published
  Jornada.Linkage.Runner.dll. Each of the twelve attempts provisions a NEW
  LocalDB instance with the canonical schema and emits aggregate-only evidence.
  No cleanup, no operational database, no TEST or automatic model activation.
  Source files must be supplied explicitly and are validated again by the
  C# harness. Never set a connection to an existing JornadaLocal instance.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RunnerDll,
    [Parameter(Mandatory)][string]$DatabaseSchema,
    [Parameter(Mandatory)][string]$TrainSelectionEnvelope,
    [Parameter(Mandatory)][string]$FinalistValidation,
    [Parameter(Mandatory)][string]$GoldFittedSnapshot,
    [Parameter(Mandatory)][string]$OriginalTrainCsv,
    [Parameter(Mandatory)][string]$LowerTrainStudy,
    [Parameter(Mandatory)][string]$OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) { throw "O protocolo físico exige Windows/LocalDB." }
if (-not (Get-Command SqlLocalDB.exe -ErrorAction SilentlyContinue)) {
    throw "SqlLocalDB.exe não foi encontrado; não substituir LocalDB por banco operacional."
}

function AbsoluteInput([string]$Value) {
    return (Resolve-Path -LiteralPath $Value -ErrorAction Stop).ProviderPath
}
$runner = AbsoluteInput $RunnerDll
$schema = AbsoluteInput $DatabaseSchema
$selection = AbsoluteInput $TrainSelectionEnvelope
$validation = AbsoluteInput $FinalistValidation
$goldFit = AbsoluteInput $GoldFittedSnapshot
$csv = AbsoluteInput $OriginalTrainCsv
$trainStudy = AbsoluteInput $LowerTrainStudy
$root = [System.IO.Path]::GetFullPath($OutputRoot)

if (-not (Test-Path -LiteralPath $runner -PathType Leaf) -or
    [System.IO.Path]::GetExtension($runner) -ne '.dll') {
    throw "Runner precisa ser DLL C# publicada."
}
if (-not (Test-Path -LiteralPath (Join-Path $schema 'Jornada_Fase1.sql') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $schema 'migrations/manifest.txt') -PathType Leaf)) {
    throw "Schema canônico e manifesto obrigatórios."
}
if (Test-Path -LiteralPath $root) {
    throw "OutputRoot deve ser um caminho INÉDITO; não sobrescrever nem limpar ensaios."
}
$rawStudy = Get-Content -LiteralPath $trainStudy -Raw | ConvertFrom-Json -Depth 100
if ($rawStudy.TestUsed -ne $false -or $rawStudy.ExternalValidationUsed -ne $false -or
    $rawStudy.AutomaticCalibrationOrActivationAllowed -ne $false -or
    $rawStudy.OperationalModelCreated -ne $false -or
    $rawStudy.DuplicateConfidenceCertified -ne $false -or
    $rawStudy.LinkageFdrRecalibrated -ne $false -or
    $rawStudy.Status -ne 'TRAIN_DESCRIPTIVE_KNEE_SUGGESTION_ONLY_UNCERTIFIED_NO_PRESET_DUPLICATE_BUDGET') {
    throw "Somente sugestão Pareto manual, exclusivamente TRAIN, sem TEST/ativação."
}
$gold = Get-Content -LiteralPath $goldFit -Raw | ConvertFrom-Json -Depth 100
$goldSha = (Get-FileHash -LiteralPath $goldFit -Algorithm SHA256).Hash.ToLowerInvariant()
if ($rawStudy.FrozenSnapshotSha256 -notin @($goldSha, $gold.GoldParentSnapshotSha256)) {
    throw "Fit do estudo TRAIN não coincide com snapshot Gold nem com seu pai congelado."
}
if ($gold.CandidateGrouping.CandidateGrouping -ne 'GOLD' -or
    [string]::IsNullOrWhiteSpace($gold.GoldParentSnapshotSha256)) {
    throw "Esperada variante Gold do mesmo fit V9."
}
if (@($rawStudy.DescriptiveFinalistThresholds).Count -ne 3) {
    throw "TRAIN não apresentou três finalistas DISTINTOS; matriz 3x4 não deve inventar limiares."
}
$upper = [decimal]$gold.Threshold
$values = @($rawStudy.DescriptiveFinalistThresholds | ForEach-Object {
    [decimal]$_
})
if (@($values | Select-Object -Unique).Count -ne 3 -or
    @($values | Where-Object { $_ -le 0 -or $_ -ge $upper }).Count -ne 0 -or
    $values[0] -ne [decimal]$rawStudy.DescriptiveSuggestedLowerThreshold) {
    throw "Os três limiares devem ser os finalistas TRAIN, distintos e inferiores ao teto V9."
}

$policies = @(
    [pscustomobject]@{ Slot = 'CURRENT'; Floor = $null },
    [pscustomobject]@{ Slot = 'KNEE'; Floor = $values[0] },
    [pscustomobject]@{ Slot = 'CONSERVATIVE'; Floor = $values[1] },
    [pscustomobject]@{ Slot = 'LOWEST_PENDING'; Floor = $values[2] }
)
$orders = @(
    'BASELINE_G0_G1_G2_G3',
    'REVERSE_G3_G2_G1_G0',
    'INTERLEAVED_G1_G3_G0_G2'
)
[System.IO.Directory]::CreateDirectory($root) | Out-Null
$reports = [System.Collections.Generic.List[string]]::new()
$previousConnection = [Environment]::GetEnvironmentVariable('ConnectionStrings__Jornada', 'Process')
try {
    foreach ($policy in $policies) {
        foreach ($order in $orders) {
            $label = $policy.Slot + '_' + $order
            $databaseOutput = Join-Path $root ($label + '_database')
            $trialOutput = Join-Path $root ($label + '_report')

            # The creation command refuses reused output and provisions an
            # entirely new LocalDB INSTANCE. It never drops any existing DB.
            & dotnet $runner --v9-gold-harness-create $databaseOutput $schema
            if ($LASTEXITCODE -ne 0) { throw "Falha na criação LocalDB: $label" }
            $readyFile = Join-Path $databaseOutput 'schema-ready.json'
            if (-not (Test-Path -LiteralPath $readyFile -PathType Leaf)) {
                throw "Preparação não produziu schema-ready.json: $label"
            }
            $ready = Get-Content -LiteralPath $readyFile -Raw | ConvertFrom-Json
            $instance = [string]$ready.instance
            if ($ready.completed -ne $true -or $ready.database -ne 'JornadaHarness' -or
                $instance -cnotmatch '^JornadaV9Gold[0-9]{8}_[a-f0-9]{8}$') {
                throw "Manifesto da instância isolada inválido: $label"
            }
            $env:ConnectionStrings__Jornada = "Server=(localdb)\$instance;Database=JornadaHarness;Integrated Security=true;Encrypt=Optional"
            $argv = @('--v9-gold-harness', $selection, $validation, $goldFit, $csv, $trialOutput, $order)
            if ($null -ne $policy.Floor) {
                $floorInvariant = $policy.Floor.ToString([Globalization.CultureInfo]::InvariantCulture)
                $argv += ('LOWER_NONMATCH=' + $floorInvariant)
            }
            & dotnet $runner @argv
            if ($LASTEXITCODE -ne 0) { throw "Falha no Runner Gold: $label" }

            $reportPath = Join-Path $trialOutput 'gold-runner-study.json'
            if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
                throw "Relatório Gold ausente: $label"
            }
            $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json -Depth 100
            if ($report.goldThreeOrderTrial.allFourWavesPublished -ne $true -or
                $report.goldThreeOrderTrial.orderId -cne $order -or
                $report.goldThreeOrderTrial.testUsed -ne $false -or
                $report.goldThreeOrderTrial.activated -ne $false) {
                throw "Relatório não confirma quatro ondas TRAIN publicadas: $label"
            }
            if ($null -eq $policy.Floor) {
                if ($report.lowerNonMatchExperiment.policy -cne 'CURRENT' -or
                    $null -ne $report.lowerNonMatchExperiment.threshold) {
                    throw "Controle alterado indevidamente: $label"
                }
            } else {
                if ($report.lowerNonMatchExperiment.policy -cne 'EXPERIMENTAL_LOWER_TRAIN_ONLY' -or
                    [decimal]$report.lowerNonMatchExperiment.threshold -ne $policy.Floor) {
                    throw "Limiar ou política divergente: $label"
                }
            }
            $reports.Add($reportPath)
        }
    }
    if ($reports.Count -ne 12) { throw "Doze ensaios físicos eram obrigatórios." }
    $matrixFile = Join-Path $root 'gold-12-trial-matrix.json'
    $matrixArgs = @('--v9-gold-compare-matrix') + $reports.ToArray() + @($matrixFile)
    & dotnet $runner @matrixArgs
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $matrixFile -PathType Leaf)) {
        throw "Comparador C# recusou a matriz; nenhuma inferência autorizada."
    }
    Write-Output "Matriz TRAIN concluída, não certificada: $matrixFile"
}
finally {
    [Environment]::SetEnvironmentVariable('ConnectionStrings__Jornada', $previousConnection, 'Process')
    # Intencionalmente não resetar, apagar ou sobrescrever LocalDB e evidências.
}
