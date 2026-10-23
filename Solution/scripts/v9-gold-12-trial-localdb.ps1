#requires -Version 7.2
<#
.SYNOPSIS
  Manual TRAIN-only 3 orders x (CURRENT + 0..3 distinct Pareto finalists) Gold replay.
.DESCRIPTION
  Requires a Windows machine with SQL Server LocalDB installed and a published
  Jornada.Linkage.Runner.dll. Each attempt provisions a NEW
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
# Zero finalistas still gets the CURRENT baseline: missing study fields are
# NOT treated as zero. The existing Pareto Search normally returns >= 1
# candidate, but explicit empty output is supported for future no-suggestion.
if (-not $rawStudy.PSObject.Properties['DescriptiveFinalistThresholds']) {
    throw "Estudo TRAIN não declara DescriptiveFinalistThresholds."
}
if (-not $rawStudy.PSObject.Properties['DescriptiveSuggestedLowerThreshold']) {
    throw "Estudo TRAIN não declara DescriptiveSuggestedLowerThreshold."
}
$values = @($rawStudy.DescriptiveFinalistThresholds)
$k = $values.Count
if ($k -gt 3 -or $values -contains $null) {
    throw "Pareto TRAIN exige 0 a 3 limiares explicitamente declarados, não nulos."
}
$upper = [decimal]$gold.Threshold
$converted = @($values | ForEach-Object { [decimal]$_ })
if (@($converted | Select-Object -Unique).Count -ne $k -or
    @($converted | Where-Object { $_ -le 0 -or $_ -ge $upper }).Count -ne 0) {
    throw "Finalistas TRAIN devem ser distintos, positivos e inferiores ao limiar superior V9."
}
if ($k -gt 0) {
    if ($null -eq $rawStudy.DescriptiveSuggestedLowerThreshold -or
        $converted[0] -ne [decimal]$rawStudy.DescriptiveSuggestedLowerThreshold) {
        throw "O primeiro finalista não coincide com o joelho TRAIN declarado."
    }
}
elseif ($null -ne $rawStudy.DescriptiveSuggestedLowerThreshold) {
    throw "Estudo sem finalistas não pode declarar joelho."
}

$policies = [System.Collections.Generic.List[object]]::new()
$policies.Add([pscustomobject]@{ Slot = 'CURRENT'; Floor = $null })
for ($i = 0; $i -lt $k; $i++) {
    # Only with all three distinct can each position be interpreted as
    # knee / conservative / least-pending. Dedupe erases intermediate slots.
    $slot = if ($i -eq 0) { 'KNEE' }
        elseif ($k -eq 3 -and $i -eq 1) { 'CONSERVATIVE' }
        elseif ($k -eq 3 -and $i -eq 2) { 'LOWEST_PENDING' }
        else { 'FINALIST_' + ($i + 1) }
    $policies.Add([pscustomobject]@{ Slot = $slot; Floor = $converted[$i] })
}
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
    $expectedCount = 3 * ($k + 1)
    if ($reports.Count -ne $expectedCount) {
        throw "Número de trios de ordens divergente da quantidade de finalistas TRAIN."
    }
    # Legacy filename retained for consumers, but V2 carries actual k and N.
    $matrixFile = Join-Path $root 'gold-12-trial-matrix.json'
    $matrixArgs = @('--v9-gold-compare-matrix') + $reports.ToArray() + @($matrixFile)
    & dotnet $runner @matrixArgs
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $matrixFile -PathType Leaf)) {
        throw "Comparador C# recusou a matriz; nenhuma inferência autorizada."
    }
    $matrix = Get-Content -LiteralPath $matrixFile -Raw | ConvertFrom-Json -Depth 100
    if ([int]$matrix.FinalistCount -ne $k -or
        [int]$matrix.ExecutedTrialCount -ne $expectedCount -or
        @($matrix.Rows).Count -ne $expectedCount) {
        throw "Matriz não confirmou k finalistas e total de corridas físicas."
    }
    Write-Output "Matriz TRAIN $expectedCount corridas (k=$k), não certificada: $matrixFile"
}
finally {
    [Environment]::SetEnvironmentVariable('ConnectionStrings__Jornada', $previousConnection, 'Process')
    # Intencionalmente não resetar, apagar ou sobrescrever LocalDB e evidências.
}
