#requires -Version 7.2
<#
  Physical INDEPENDENT_HOLDOUT: frozen Gold V6 CURRENT vs KNEE (0.05),
  each at three pre-registered orders, with a NEW synthetic seed.
  Six NEW empty Windows LocalDB instances; no TEST, refit, DTO/Gold
  operational activation, migration change or per-identity export.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RunnerDll,
    [Parameter(Mandatory)][string]$SyntheticGeneratorDll,
    [Parameter(Mandatory)][string]$DatabaseSchema,
    [Parameter(Mandatory)][string]$FrozenCandidate,
    [Parameter(Mandatory)][string]$TrainingSelectionEnvelope,
    [Parameter(Mandatory)][string]$FinalistValidation,
    [Parameter(Mandatory)][string]$GoldFittedSnapshot,
    [Parameter(Mandatory)][string]$ReferenceRoot,
    [Parameter(Mandatory)][string]$BirthDailySource,
    [Parameter(Mandatory)][string]$OutputRoot,
    [ValidateSet('CURRENT','KNEE')][string]$Policy,
    [ValidateSet('BASELINE_G0_G1_G2_G3','REVERSE_G3_G2_G1_G0','INTERLEAVED_G1_G3_G0_G2')][string]$Order
)
if ([bool]$Policy -ne [bool]$Order) { throw 'Policy e Order devem ser informados juntos.' }
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if (-not $IsWindows -or -not (Get-Command SqlLocalDB.exe -ErrorAction SilentlyContinue)) {
    throw 'O holdout exige Windows + LocalDB real, nunca banco operacional.'
}
function ExistingPath([string]$Value) {
    return (Resolve-Path -LiteralPath $Value -ErrorAction Stop).ProviderPath
}
$runner = ExistingPath $RunnerDll
$synthetic = ExistingPath $SyntheticGeneratorDll
$schema = ExistingPath $DatabaseSchema
$candidatePath = ExistingPath $FrozenCandidate
$selection = ExistingPath $TrainingSelectionEnvelope
$validation = ExistingPath $FinalistValidation
$goldFit = ExistingPath $GoldFittedSnapshot
$reference = ExistingPath $ReferenceRoot
$birth = ExistingPath $BirthDailySource
$root = [IO.Path]::GetFullPath($OutputRoot)
if ([IO.Path]::GetExtension($runner) -ne '.dll' -or
    [IO.Path]::GetExtension($synthetic) -ne '.dll' -or
    (Test-Path -LiteralPath $root)) {
    throw 'Runner/generator DLL obrigatórios e diretório de resultado deve ser inédito.'
}
$candidate = Get-Content -LiteralPath $candidatePath -Raw | ConvertFrom-Json
$fitHash = (Get-FileHash -LiteralPath $goldFit -Algorithm SHA256).Hash.ToLowerInvariant()
if ($candidate.ContractVersion -cne 'GOLD_V6_FROZEN_KNEE_VALIDATION_CANDIDATE_V1' -or
    [decimal]$candidate.CandidateLowerThreshold -ne [decimal]'0.05' -or
    [uint64]$candidate.ProposedHoldoutSeed -ne [uint64]20261025001 -or
    $fitHash -ne '6d93690dccac2c92dc4d3113cbb9169dd19156c4f9b3d849ededfb4c54ae5f3f' -or
    $candidate.TestUsed -ne $false -or $candidate.Certified -ne $false -or
    $candidate.OperationallyActivated -ne $false) {
    throw 'Candidato externo não coincide com o Gold V6 pré-registrado.'
}
[IO.Directory]::CreateDirectory($root) | Out-Null
# No historical TRAIN/TEST is used to generate the new cohort. Reference files
# and generator rules are the pinned inputs used by the original synthetic study.
$cohort = Join-Path $root 'new-cohort-seed-20261025001'
& dotnet $synthetic generate --reference-root $reference `
    --population-profile demographic-primary --birth-daily-source $birth `
    --error-profile clean --people 30000 --seed 20261025001 --out $cohort
if ($LASTEXITCODE -ne 0) { throw 'Falha na geração sintética clean do novo holdout.' }
$csv = Join-Path $cohort 'observacoes.csv'
$csvSha = (Get-FileHash -LiteralPath $csv -Algorithm SHA256).Hash.ToLowerInvariant()
if ($csvSha -eq [string]$candidate.TrainingCorpusSha256) {
    throw 'A nova massa coincide em bytes com TRAIN; recusada.'
}
$policies = @(
    [pscustomobject]@{ Slot = 'CURRENT'; Floor = $null },
    [pscustomobject]@{ Slot = 'KNEE'; Floor = [decimal]'0.05' }
)
$orders = @('BASELINE_G0_G1_G2_G3','REVERSE_G3_G2_G1_G0','INTERLEAVED_G1_G3_G0_G2')
if ($Policy) {
    $policies = @($policies | Where-Object { $_.Slot -ceq $Policy })
    $orders = @($Order)
}
$expectedTrials = if ($Policy) { 1 } else { 6 }
$rows = [System.Collections.Generic.List[object]]::new()
$seenFingerprints = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$previousConnection = [Environment]::GetEnvironmentVariable('ConnectionStrings__Jornada', 'Process')
try {
    foreach ($policy in $policies) {
        foreach ($order in $orders) {
            $label = $policy.Slot + '_' + $order
            $dbOut = Join-Path $root ($label + '_database')
            $reportOut = Join-Path $root ($label + '_report')
            & dotnet $runner --v9-gold-harness-create $dbOut $schema
            if ($LASTEXITCODE -ne 0) { throw "Falha preparando LocalDB descartável: $label" }
            $ready = Get-Content -LiteralPath (Join-Path $dbOut 'schema-ready.json') -Raw | ConvertFrom-Json
            if ($ready.completed -ne $true -or $ready.database -cne 'JornadaHarness' -or
                $ready.instance -cnotmatch '^JornadaV9Gold[0-9]{8}_[a-f0-9]{8}$') {
                throw "LocalDB não pertence ao harness: $label"
            }
            $env:ConnectionStrings__Jornada = "Server=(localdb)\$($ready.instance);Database=JornadaHarness;Integrated Security=true;Encrypt=Optional"
            if ($rows.Count -eq 0) {
                # Always BEFORE looking at metrics. This verifies REAL generated
                # CSV/manifest/truth hashes and prepares immutable six-run plan.
                & dotnet $runner --v9-gold-holdout-preflight $candidatePath $cohort (Join-Path $root 'holdout-plan.json')
                if ($LASTEXITCODE -ne 0) { throw 'Manifesto de coorte nova/fit incompatível.' }
                $plan = Get-Content -LiteralPath (Join-Path $root 'holdout-plan.json') -Raw | ConvertFrom-Json
                if ($plan.RequiredPhysicalTrials -ne 6 -or
                    $plan.EvidenceScope -cne 'INDEPENDENT_HOLDOUT' -or
                    $plan.CsvSha256 -ine $csvSha -or
                    $plan.GoldFittedSnapshotSha256 -ine $fitHash) {
                    throw 'Pré-voo não congelou o mesmo novo corpus e fit.'
                }
            }
            & dotnet $runner --v9-gold-holdout-harness $candidatePath $cohort `
                $selection $validation $goldFit $reportOut $order $policy.Slot
            if ($LASTEXITCODE -ne 0) { throw "Ensaio físico abortado: $label" }
            $reportFile = Join-Path $reportOut 'gold-runner-study.json'
            $report = Get-Content -LiteralPath $reportFile -Raw | ConvertFrom-Json -Depth 100
            $trial = $report.goldThreeOrderTrial
            if ($trial.evidenceScope -cne 'INDEPENDENT_HOLDOUT' -or
                $trial.orderId -cne $order -or $trial.testUsed -ne $false -or
                $trial.activated -ne $false -or
                $trial.originalCorpusSha256 -ine $csvSha -or
                $trial.frozenSnapshotSha256 -ine $fitHash -or
                $report.independentHoldout.Seed -ne 20261025001 -or
                $report.independentHoldout.CohortFingerprintSha256 -ine $plan.CohortFingerprintSha256 -or
                $report.independentHoldout.IdentityNamespaceSha256 -ine $plan.IdentityNamespaceSha256 -or
                $report.refitted -ne $false -or $report.testUsed -ne $false -or
                $report.certified -ne $false -or $report.activated -ne $false) {
                throw "Proveniência/resultados da corrida não são holdout blind: $label"
            }
            if (-not $seenFingerprints.Add([string]$trial.isolatedRunFingerprintSha256)) {
                throw "Ensaio físico duplicado: $label"
            }
            if ($policy.Slot -eq 'CURRENT') {
                if ($report.lowerNonMatchExperiment.policy -cne 'CURRENT' -or
                    $null -ne $report.lowerNonMatchExperiment.threshold) {
                    throw 'Controle CURRENT não pode receber limiar KNEE.'
                }
            } elseif ($report.lowerNonMatchExperiment.policy -cne 'EXPERIMENTAL_LOWER_HOLDOUT_ONLY' -or
                [decimal]$report.lowerNonMatchExperiment.threshold -ne [decimal]'0.05') {
                throw 'KNEE não corresponde ao candidato V6 congelado.'
            }
            # The public matrix receives only per-error qualitative states,
            # rounded scores and truth-in-blocking YES/NO. No local row keys,
            # synthetic P-ID, names, dates, UUIDs or labels leave the runner.
            $forensic = $report.holdoutErrorForensics
            if ($null -eq $forensic -or $forensic.noPii -ne $true -or
                $forensic.exportedIds -ne $false -or
                $forensic.contract -cne 'GOLD_HOLDOUT_ERROR_FORENSICS_NO_IDENTIFIERS_V1' -or
                @($forensic.cases | Where-Object { $_.Failure -eq 'FALSE_CREATION' }).Count -ne
                    [long]$report.lowerNonMatchExperiment.finalFalseNewIdentityCreations) {
                throw "Perícia sem identificação não reconcilia as criações físicas: $label"
            }
            $rows.Add([pscustomobject]@{
                Policy = $policy.Slot; OrderId = $order
                ErrorForensicCases = @($forensic.cases)
                ErrorForensicFalseCreations = [long]$forensic.falseCreationCaseCount
                ErrorForensicFalseAcceptedEvents = [long]$forensic.falseAcceptedEventCaseCount
                BlockingMissWhenTruthRepresented = @($forensic.cases | Where-Object {
                    $_.TruthAmongBlockingCandidates -ceq 'NO'
                }).Count
                ScorerOrResolverWhenTrueCandidateRetrieved = @($forensic.cases | Where-Object {
                    $_.TruthAmongBlockingCandidates -ceq 'YES'
                }).Count
                NotYetRepresentedInPriorGold = @($forensic.cases | Where-Object {
                    $_.PriorGoldTruth -ceq 'NOT_REPRESENTED'
                }).Count
                Sources = [long]$report.goldEndToEnd.Observations
                TrueIdentities = [long]$report.goldEndToEnd.TrueIdentities
                Pending = [long]$report.goldEndToEnd.PendingSources
                Published = [long]$report.goldEndToEnd.PublishedSources
                FalseAcceptedLinks = [long]$report.publication.falseExistingLinks
                AcceptedLinkFdr = $report.publication.fdr
                FalseGoldPairs = [long]$report.goldEndToEnd.FalseGoldObservationPairs
                FalseNewIdentityCreations = [long]$report.lowerNonMatchExperiment.finalFalseNewIdentityCreations
                FalseLowerBandCreations = [long]$report.lowerNonMatchExperiment.finalFalseLowerBandCreations
                FragmentedIdentities = [long]$report.goldEndToEnd.FragmentedTrueIdentities
                ImpureReferences = [long]$report.goldEndToEnd.ImpureGoldReferences
                CoverageAwareBcubedPrecision = [double]$report.goldBcubed.CoverageAwarePrecision
                CoverageAwareBcubedRecall = [double]$report.goldBcubed.CoverageAwareRecall
                StablePartitionSha256 = [string]$trial.stablePartitionSha256
                RunFingerprintSha256 = [string]$trial.isolatedRunFingerprintSha256
                ElapsedMilliseconds = [long]$trial.elapsedMilliseconds
            })
            if ($rows[$rows.Count-1].Pending + $rows[$rows.Count-1].Published -ne
                $rows[$rows.Count-1].Sources) {
                throw 'Partição de fontes não reconcilia.'
            }
        }
    }
    if ($rows.Count -ne $expectedTrials -or $seenFingerprints.Count -ne $expectedTrials -or
        @($rows | Select-Object -ExpandProperty Sources -Unique).Count -ne 1 -or
        @($rows | Select-Object -ExpandProperty TrueIdentities -Unique).Count -ne 1) {
        throw 'Seis execuções físicas únicas da mesma coorte são obrigatórias.'
    }
    $worst = @()
    foreach ($policy in @('CURRENT','KNEE')) {
        $subset = @($rows | Where-Object { $_.Policy -ceq $policy })
        $worst += [pscustomobject]@{
            Policy = $policy; MaxPending = ($subset.Pending | Measure-Object -Maximum).Maximum
            MaxFalseAcceptedLinks = ($subset.FalseAcceptedLinks | Measure-Object -Maximum).Maximum
            MaxFalseGoldPairs = ($subset.FalseGoldPairs | Measure-Object -Maximum).Maximum
            MaxFalseNewIdentityCreations = ($subset.FalseNewIdentityCreations | Measure-Object -Maximum).Maximum
            MinCoverageAwareBcubedRecall = ($subset.CoverageAwareBcubedRecall | Measure-Object -Minimum).Minimum
        }
    }
    $matrix = [pscustomobject]@{
        Version = if ($expectedTrials -eq 1) { 'GOLD_INDEPENDENT_HOLDOUT_ONE_PHYSICAL_TRIAL_V1' } else { 'GOLD_INDEPENDENT_HOLDOUT_SIX_PHYSICAL_TRIALS_V1' }
        Scope = 'INDEPENDENT_HOLDOUT'
        Seed = [uint64]20261025001
        NewCorpusSha256 = $csvSha
        CohortFingerprintSha256 = $plan.CohortFingerprintSha256
        IdentityNamespaceSha256 = $plan.IdentityNamespaceSha256
        FrozenGoldFitSha256 = $fitHash
        FrozenTrainMatrixSha256 = [string]$candidate.SourceMatrixSha256
        PhysicalTrialCount = $rows.Count
        Rows = @($rows.ToArray())
        WorstByPolicy = @($worst)
        TestUsed = $false; IndependentlyCertified = $false
        OperationallyActivated = $false; Retuned = $false
    }
    $fileName = if ($expectedTrials -eq 1) { 'holdout-trial.json' } else { 'holdout-six-trial-matrix.json' }
    $matrix | ConvertTo-Json -Depth 30 |
        Set-Content -LiteralPath (Join-Path $root $fileName) -Encoding utf8 -NoNewline
    Write-Host "Independent holdout physical trials complete: $expectedTrials. Aggregate-only output: $fileName"
}
finally {
    [Environment]::SetEnvironmentVariable('ConnectionStrings__Jornada', $previousConnection, 'Process')
}
