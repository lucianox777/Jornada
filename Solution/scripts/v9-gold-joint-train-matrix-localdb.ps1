#requires -Version 7.2
<#
.SYNOPSIS
  Manually execute the preregistered V9 Gold TRAIN upper x lower x 3-order grid.
.DESCRIPTION
  Windows/LocalDB ONLY. Every cell gets a new, independently provisioned
  JornadaV9Gold instance and a new report directory. The existing C# Runner
  performs the actual Rank/Resolve, incremental Gold publication, B-cubed and
  receipt capture; the single C# calibrator aggregates the complete matrix.
  This script never reads TEST, promotes a model, touches HML/PROD, deletes
  databases, or certifies its own results. Invoke only after a plan is frozen.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RunnerDll,
    [Parameter(Mandatory)][string]$DatabaseSchema,
    [Parameter(Mandatory)][string]$TrainSelectionEnvelope,
    [Parameter(Mandatory)][string]$FinalistValidation,
    [Parameter(Mandatory)][string]$GoldFittedSnapshot,
    [Parameter(Mandatory)][string]$OriginalTrainCsv,
    [Parameter(Mandatory)][string]$PreregisteredPlan,
    [Parameter(Mandatory)][string]$OutputRoot
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$invariant = [Globalization.CultureInfo]::InvariantCulture

function InputFile([string]$value) {
    $path = (Resolve-Path -LiteralPath $value -ErrorAction Stop).ProviderPath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Expected input file: $value"
    }
    return $path
}
function FileSha([string]$path) {
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function AssertSha([string]$actual, [string]$expected, [string]$label) {
    if ($actual -cne $expected) { throw "Frozen TRAIN input SHA mismatch: $label" }
}
function RunRunner([string[]]$argv, [string]$label) {
    & dotnet $runner @argv
    if ($LASTEXITCODE -ne 0) { throw "C# Runner rejected $label (exit $LASTEXITCODE)" }
}

# No SQL or output side effects until the entire plan and frozen inputs are checked.
$runner = InputFile $RunnerDll
$schema = (Resolve-Path -LiteralPath $DatabaseSchema -ErrorAction Stop).ProviderPath
$selection = InputFile $TrainSelectionEnvelope
$validation = InputFile $FinalistValidation
$gold = InputFile $GoldFittedSnapshot
$csv = InputFile $OriginalTrainCsv
$planPath = InputFile $PreregisteredPlan
$root = [IO.Path]::GetFullPath($OutputRoot)
if (-not $IsWindows -or -not (Get-Command SqlLocalDB.exe -ErrorAction SilentlyContinue)) {
    throw 'Physical joint TRAIN requires Windows with SQL Server LocalDB.'
}
if ([IO.Path]::GetExtension($runner) -cne '.dll' -or
    -not (Test-Path -LiteralPath (Join-Path $schema 'Jornada_Fase1.sql') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $schema 'migrations/manifest.txt') -PathType Leaf)) {
    throw 'Published Runner DLL and canonical SQL schema/manifest are required.'
}
if (Test-Path -LiteralPath $root) {
    throw 'OutputRoot must be new: never overwrite or clean physical evidence.'
}
$planSha = FileSha $planPath
if ([IO.Path]::GetFileName($planPath) -cne ($planSha + '.joint-train-plan.json')) {
    throw 'The preregistered plan filename must be its exact SHA-256.'
}
$plan = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json -Depth 100
if ($plan.Version -cne 'V9_GOLD_JOINT_TRAIN_PREREGISTERED_MATRIX_V1' -or
    $plan.ManualCalibrationRequested -ne $true -or $plan.TestUsed -ne $false) {
    throw 'Only a frozen, manually requested, TRAIN-only joint plan is permitted.'
}
AssertSha (FileSha $selection) ([string]$plan.SelectionEnvelopeSha256) 'selection'
AssertSha (FileSha $gold) ([string]$plan.FrozenGoldSnapshotSha256) 'Gold variant'
AssertSha (FileSha $csv) ([string]$plan.OriginalTrainCsvSha256) 'original TRAIN CSV'
$uppers = @($plan.CandidateUpperThresholds)
$lowers = @($plan.CandidateLowerThresholds)
$orders = @(
    'BASELINE_G0_G1_G2_G3',
    'REVERSE_G3_G2_G1_G0',
    'INTERLEAVED_G1_G3_G0_G2'
)
if ($uppers.Count -lt 2 -or $lowers.Count -lt 2 -or
    $uppers.Count -gt 64 -or $lowers.Count -gt 64 -or
    ($uppers.Count * $lowers.Count) -gt 256) {
    throw 'Joint TRAIN grid size is invalid.'
}
$upperValues = @($uppers | ForEach-Object { [decimal]$_ })
$lowerValues = @($lowers | ForEach-Object { [decimal]$_ })
if (@($upperValues | Select-Object -Unique).Count -ne $upperValues.Count -or
    @($lowerValues | Select-Object -Unique).Count -ne $lowerValues.Count) {
    throw 'Duplicate thresholds in frozen plan.'
}
foreach ($upper in $upperValues) {
    foreach ($lower in $lowerValues) {
        if ($lower -le 0 -or $lower -ge $upper -or $upper -gt 1) {
            throw 'Every preregistered lower/upper pair must satisfy 0 < lower < upper <= 1.'
        }
    }
}

# Validate the ENTIRE preregistered plan with the C# canonical parser and
# fingerprint rules before creating any LocalDB instance or output directory.
# PowerShell's own structural checks are not a substitute for ReadPlan.
RunRunner -argv @('--v9-gold-joint-train-plan-preflight', $planPath) -label 'frozen TRAIN plan preflight'

# Preserve any caller connection and never accept it as a physical destination.
$previousConnection = [Environment]::GetEnvironmentVariable('ConnectionStrings__Jornada', 'Process')
$instances = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$reports = [Collections.Generic.List[string]]::new()
try {
    [IO.Directory]::CreateDirectory($root) | Out-Null
    for ($ui = 0; $ui -lt $upperValues.Count; $ui++) {
        for ($li = 0; $li -lt $lowerValues.Count; $li++) {
            for ($oi = 0; $oi -lt $orders.Count; $oi++) {
                $upper = $upperValues[$ui]
                $lower = $lowerValues[$li]
                $order = $orders[$oi]
                $label = ('U{0:D2}_L{1:D2}_O{2}' -f $ui, $li, $oi)
                $databaseOutput = Join-Path $root ($label + '_database')
                $trialOutput = Join-Path $root ($label + '_report')

                # Create-only instance: never reuse a database or reset prior evidence.
                RunRunner -argv @('--v9-gold-harness-create', $databaseOutput, $schema) -label "LocalDB $label"
                $readyFile = Join-Path $databaseOutput 'schema-ready.json'
                if (-not (Test-Path -LiteralPath $readyFile -PathType Leaf)) {
                    throw "Missing LocalDB readiness evidence for $label"
                }
                $ready = Get-Content -LiteralPath $readyFile -Raw | ConvertFrom-Json -Depth 100
                $instance = [string]$ready.instance
                if ($ready.completed -ne $true -or $ready.database -cne 'JornadaHarness' -or
                    $instance -cnotmatch '^JornadaV9Gold[0-9]{8}_[a-f0-9]{8}$' -or
                    -not $instances.Add($instance)) {
                    throw "LocalDB isolation or readiness failed for $label"
                }
                $env:ConnectionStrings__Jornada = "Server=(localdb)\$instance;Database=JornadaHarness;Integrated Security=true;Encrypt=Optional"
                $upperArg = 'UPPER_TRAIN=' + $upper.ToString($invariant)
                $lowerArg = 'LOWER_NONMATCH=' + $lower.ToString($invariant)
                RunRunner -argv @('--v9-gold-joint-train-trial', $selection, $validation,
                    $gold, $csv, $trialOutput, $order, $lowerArg, $upperArg, $planPath) -label "physical TRAIN $label"

                $reportPath = Join-Path $trialOutput 'gold-runner-study.json'
                if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
                    throw "Missing physical Gold receipt for $label"
                }
                $receipt = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json -Depth 100
                $trial = $receipt.jointTrainPhysicalTrial
                if ($null -eq $trial -or $trial.preregisteredPlanSha256 -cne $planSha -or
                    $trial.disposableLocalDbInstance -cne "(localdb)\$instance" -or
                    $trial.orderId -cne $order -or
                    [decimal]$trial.upperThreshold -ne $upper -or
                    [decimal]$trial.lowerThreshold -ne $lower -or
                    $receipt.testUsed -ne $false -or $receipt.activated -ne $false -or
                    $receipt.goldThreeOrderTrial.allFourWavesPublished -ne $true) {
                    throw "Physical receipt contradicts preregistered TRAIN cell $label"
                }
                $reports.Add($reportPath)
            }
        }
    }
    $expected = $upperValues.Count * $lowerValues.Count * $orders.Count
    if ($reports.Count -ne $expected -or $instances.Count -ne $expected) {
        throw 'Incomplete joint physical grid or reused LocalDB instance.'
    }
    $matrixPath = Join-Path $root 'joint-train-matrix.json'
    $matrixArgs = @('--v9-gold-joint-train-matrix-aggregate', $planPath) +
        $reports.ToArray() + @($matrixPath)
    RunRunner $matrixArgs 'C# joint TRAIN aggregate'
    if (-not (Test-Path -LiteralPath $matrixPath -PathType Leaf)) {
        throw 'C# calibrator did not produce a complete joint matrix.'
    }
    $matrix = Get-Content -LiteralPath $matrixPath -Raw | ConvertFrom-Json -Depth 100
    if ([int]$matrix.PhysicalCellCount -ne $expected -or
        $matrix.TestUsed -ne $false -or $matrix.IndependentlyCertified -ne $false -or
        $matrix.OperationallyActivated -ne $false) {
        throw 'Joint matrix result has invalid completeness or governance flags.'
    }
    Write-Output "Joint TRAIN physical cells=$expected; descriptive, NOT certified: $matrixPath"
}
finally {
    [Environment]::SetEnvironmentVariable('ConnectionStrings__Jornada', $previousConnection, 'Process')
    # Never delete, reset, or overwrite LocalDB instances or reports.
}
