#requires -Version 7.2
<#
  Diagnostic TRAIN_SELECTION only; NEVER the sealed TEST or the independent
  holdout seed 20261025001. Physically compare frozen KNEE 0.05 against the
  same KNEE with a 19-cell pre-creation second retrieval in three orders.
  Six fresh, exclusive Windows LocalDB instances and the identical TRAIN
  corpus/fit; no refit, promotion, cleanup or operational database.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RunnerDll,
    [Parameter(Mandatory)][string]$DatabaseSchema,
    [Parameter(Mandatory)][string]$TrainSelectionEnvelope,
    [Parameter(Mandatory)][string]$FinalistValidation,
    [Parameter(Mandatory)][string]$GoldFittedSnapshot,
    [Parameter(Mandatory)][string]$OriginalTrainCsv,
    [Parameter(Mandatory)][string]$FrozenCandidate,
    [Parameter(Mandatory)][string]$OutputRoot,
    [ValidateSet('KNEE','KNEE_SECOND_SEARCH')][string]$Variant,
    [ValidateSet('BASELINE_G0_G1_G2_G3','REVERSE_G3_G2_G1_G0','INTERLEAVED_G1_G3_G0_G2')][string]$Order
)
if ([bool]$Variant -ne [bool]$Order) {
    throw 'Variant e Order devem ser informados juntos para uma corrida física isolada.'
}
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if (-not $IsWindows -or -not (Get-Command SqlLocalDB.exe -ErrorAction SilentlyContinue)) {
    throw 'Execução física exige Windows + SQL Server LocalDB instalado.'
}
function RequirePath([string]$Value) {
    return (Resolve-Path -LiteralPath $Value -ErrorAction Stop).ProviderPath
}
$runner = RequirePath $RunnerDll
$schema = RequirePath $DatabaseSchema
$selection = RequirePath $TrainSelectionEnvelope
$validation = RequirePath $FinalistValidation
$goldFit = RequirePath $GoldFittedSnapshot
$csv = RequirePath $OriginalTrainCsv
$candidatePath = RequirePath $FrozenCandidate
$root = [IO.Path]::GetFullPath($OutputRoot)
if ([IO.Path]::GetExtension($runner) -cne '.dll' -or
    -not (Test-Path -LiteralPath (Join-Path $schema 'migrations/manifest.txt')) -or
    (Test-Path -LiteralPath $root)) {
    throw 'Runner DLL, schema canônico e OutputRoot novo são obrigatórios.'
}
$candidate = Get-Content -LiteralPath $candidatePath -Raw | ConvertFrom-Json
$fitSha = (Get-FileHash -LiteralPath $goldFit -Algorithm SHA256).Hash.ToLowerInvariant()
$csvSha = (Get-FileHash -LiteralPath $csv -Algorithm SHA256).Hash.ToLowerInvariant()
if ($candidate.ContractVersion -cne 'GOLD_V6_FROZEN_KNEE_VALIDATION_CANDIDATE_V1' -or
    [decimal]$candidate.CandidateLowerThreshold -ne [decimal]'0.05' -or
    $fitSha -cne [string]$candidate.GoldFittedSnapshotSha256 -or
    $csvSha -cne [string]$candidate.TrainingCorpusSha256 -or
    $candidate.TestUsed -ne $false -or $candidate.Certified -ne $false -or
    $candidate.OperationallyActivated -ne $false -or
    $csvSha -cne '28bbb9f4c63c97c353e7f29cdbc77091a4773b2f4c596e46103edadd93635d17' -or
    $fitSha -cne '6d93690dccac2c92dc4d3113cbb9169dd19156c4f9b3d849ededfb4c54ae5f3f') {
    throw 'Joelho/fit/corpus devem ser os dados TRAIN V6 congelados; TEST e holdout recusados.'
}
$orders = @(
    'BASELINE_G0_G1_G2_G3',
    'REVERSE_G3_G2_G1_G0',
    'INTERLEAVED_G1_G3_G0_G2'
)
$variantNames = if ($Variant) { @($Variant) } else { @('KNEE', 'KNEE_SECOND_SEARCH') }
if ($Order) { $orders = @($Order) }
[IO.Directory]::CreateDirectory($root) | Out-Null
$rows = [Collections.Generic.List[object]]::new()
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$previousConnection = [Environment]::GetEnvironmentVariable('ConnectionStrings__Jornada', 'Process')
try {
    foreach ($variantName in $variantNames) {
        foreach ($order in $orders) {
            $slot = $variantName + '_' + $order
            $databaseOutput = Join-Path $root ($slot + '_database')
            $trialOutput = Join-Path $root ($slot + '_report')
            & dotnet $runner --v9-gold-harness-create $databaseOutput $schema
            if ($LASTEXITCODE -ne 0) { throw "Preparação LocalDB falhou: $slot" }
            $ready = Get-Content -LiteralPath (Join-Path $databaseOutput 'schema-ready.json') -Raw | ConvertFrom-Json
            if ($ready.completed -ne $true -or $ready.database -cne 'JornadaHarness' -or
                $ready.instance -cnotmatch '^JornadaV9Gold[0-9]{8}_[a-f0-9]{8}$') {
                throw "LocalDB exclusivo inválido: $slot"
            }
            $env:ConnectionStrings__Jornada = "Server=(localdb)\$($ready.instance);Database=JornadaHarness;Integrated Security=true;Encrypt=Optional"
            $argv = @('--v9-gold-harness', $selection, $validation, $goldFit, $csv,
                $trialOutput, $order, 'LOWER_NONMATCH=0.05')
            if ($variantName -ceq 'KNEE_SECOND_SEARCH') {
                $argv += 'SECOND_SEARCH=EXPANDED_19'
            }
            & dotnet $runner @argv
            if ($LASTEXITCODE -ne 0) { throw "Runner incremental falhou: $slot" }
            $report = Get-Content -LiteralPath (Join-Path $trialOutput 'gold-runner-study.json') -Raw |
                ConvertFrom-Json -Depth 100
            $trial = $report.goldThreeOrderTrial
            if ($trial.evidenceScope -cne 'TRAIN_SELECTION' -or
                $trial.orderId -cne $order -or
                $trial.originalCorpusSha256 -ine $csvSha -or
                $trial.frozenSnapshotSha256 -ine $fitSha -or
                $trial.testUsed -ne $false -or $trial.activated -ne $false -or
                $report.refitted -ne $false -or $report.certified -ne $false -or
                $report.lowerNonMatchExperiment.policy -cne 'EXPERIMENTAL_LOWER_TRAIN_ONLY' -or
                [decimal]$report.lowerNonMatchExperiment.threshold -ne [decimal]'0.05' -or
                $report.secondSearch.enabled -ne ($variantName -ceq 'KNEE_SECOND_SEARCH')) {
                throw "Proveniência de pesquisa mudou: $slot"
            }
            if (-not $seen.Add([string]$trial.isolatedRunFingerprintSha256)) {
                throw "Repetição de execução física: $slot"
            }
            $rows.Add([pscustomobject]@{
                policy = $variantName; order = $order
                runFingerprint = $trial.isolatedRunFingerprintSha256
                pending = [long]$report.goldEndToEnd.PendingSources
                falseCreations = [long]$report.lowerNonMatchExperiment.finalFalseNewIdentityCreations
                falseAccepted = [long]$report.publication.falseExistingLinks
                secondSearchInvoked = [long]$report.secondSearch.invoked
                secondSearchRecoveredAdditional = [long]$report.secondSearch.expandedWithAdditionalGold
                secondSearchAbstained = [long]$report.secondSearch.abstained
                seconds = [math]::Round([double]$trial.elapsedMilliseconds / 1000, 2)
            })
        }
    }
    if ($Variant) {
        if ($rows.Count -ne 1 -or $seen.Count -ne 1) { throw 'Corrida física isolada incompleta.' }
        [ordered]@{
            version = 'V9_GOLD_SECOND_SEARCH_TRAIN_ONE_PHYSICAL_TRIAL_V1'
            status = 'COMPLETE_DESCRIPTIVE_ONLY'
            scope = 'TRAIN_SELECTION'
            frozenFitSha256 = $fitSha
            originalTrainCsvSha256 = $csvSha
            lowerThreshold = [decimal]'0.05'
            testUsed = $false
            retuned = $false
            independentlyValidated = $false
            operationallyActivated = $false
            physicalTrialCount = 1
            row = $rows[0]
        } | ConvertTo-Json -Depth 8 |
            Set-Content -LiteralPath (Join-Path $root 'second-search-one-train.json') -Encoding utf8
        return
    }
    if ($rows.Count -ne 6 -or $seen.Count -ne 6) { throw 'Esperadas seis corridas físicas distintas.' }
    $paired = @(
        foreach ($order in $orders) {
            $baseline = @($rows | Where-Object { $_.policy -ceq 'KNEE' -and $_.order -ceq $order })
            $expanded = @($rows | Where-Object { $_.policy -ceq 'KNEE_SECOND_SEARCH' -and $_.order -ceq $order })
            if ($baseline.Count -ne 1 -or $expanded.Count -ne 1) { throw "Par incompleto: $order" }
            [pscustomobject]@{
                order = $order
                deltaPending = $expanded[0].pending - $baseline[0].pending
                deltaFalseCreations = $expanded[0].falseCreations - $baseline[0].falseCreations
                deltaFalseAccepted = $expanded[0].falseAccepted - $baseline[0].falseAccepted
                secondSearchInvoked = $expanded[0].secondSearchInvoked
                recoveredAdditional = $expanded[0].secondSearchRecoveredAdditional
                abstained = $expanded[0].secondSearchAbstained
            }
        }
    )
    [ordered]@{
        version = 'V9_GOLD_SECOND_SEARCH_TRAIN_PAIRED_LOCALDB_V1'
        status = 'COMPLETE_DESCRIPTIVE_ONLY'
        scope = 'TRAIN_SELECTION'
        frozenFitSha256 = $fitSha
        originalTrainCsvSha256 = $csvSha
        lowerThreshold = [decimal]'0.05'
        readSealedTest = $false
        retuned = $false
        independentlyValidated = $false
        operationallyActivated = $false
        physicalTrialCount = 6
        rows = @($rows.ToArray())
        differences = $paired
    } | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath (Join-Path $root 'second-search-paired-train.json') -Encoding utf8
}
finally {
    if ($null -eq $previousConnection) {
        Remove-Item Env:\ConnectionStrings__Jornada -ErrorAction SilentlyContinue
    } else {
        $env:ConnectionStrings__Jornada = $previousConnection
    }
}
