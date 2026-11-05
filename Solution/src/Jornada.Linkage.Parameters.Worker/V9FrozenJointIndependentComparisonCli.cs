using System.Security.Cryptography;
using System.Text.Json;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Manual-only independent frozen V2 vs NEW V3 trial comparison.
/// This calls the existing physical SQL holdout engine twice on exactly the
/// same untouched VALIDATION cohort. It does NOT fit TF, m/u, blocking,
/// upper/lower thresholds; it does NOT execute the incremental Gold runner.
/// No TEST, model selection, DT-15 or production activation.
/// </summary>
public static class V9FrozenJointIndependentComparisonCli
{
    public const string Flag = "--v9-compare-frozen-v2-v3-independent";
    public const string Version = "V9_FROZEN_V2_V3_INDEPENDENT_BLIND_SQL_DESCRIPTIVE_V1";
    public const ulong PredeclaredSeed = 20261105001UL;
    public const string OriginalV2 = "8bdf08affbbcd88550cb055e42a063c1bfff1ffb968b55a3707be6179475d29f";
    public const string ExperimentalV3 = "8bf6d92db9efffa7cfdc26bc8d23ade01a6fc3cb430b5a5b0e9c425557cbdefc";
    public const string PolicySha = "3833d26a21691f2804fa9c52201a9c5e6f3603dc151d871453218debfc3dd319";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static string Sha(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>Exact existing source TF and blocking properties must survive.</summary>
    public static void ValidateFrozenPair(V9FittedSnapshot control, V9FittedSnapshot trial)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(trial);
        var a = control.Payload;
        var b = trial.Payload;
        if (a.SchemaVersion != V9FittedSnapshot.SchemaVersion
            || a.JointSupport is not null
            || b.SchemaVersion != V9FittedSnapshot.SupportSchemaVersion
            || b.JointSupport is null
            || a.CandidateGrouping?.CandidateGrouping != "BLIND_FS_COMPLETE_LINK_CANDIDATES"
            || b.CandidateGrouping?.CandidateGrouping != "BLIND_FS_COMPLETE_LINK_CANDIDATES"
            || a.GoldParentSnapshotSha256 is not null || b.GoldParentSnapshotSha256 is not null
            || a.JointCells.Count != 81 || b.JointCells.Count != 81
            || a.UnitTfCalibrations?.Count(x => x.State == V9SnapshotTfFitState.Active) != 1
            || b.UnitTfCalibrations?.Count(x => x.State == V9SnapshotTfFitState.Active) != 1
            || a.BlockingPasses.Count != 3 || b.BlockingPasses.Count != 3
            || a.Threshold != .95m || b.Threshold != a.Threshold
            || a.ConflictMargin != .5m || b.ConflictMargin != a.ConflictMargin
            || b.JointSupport.TrainCorpusSha256 != a.TrainCorpusSha256)
            throw new InvalidDataException("Comparador exige V2 e novo V3 BLIND do mesmo TRAIN, 3 passes e TF congelada.");
        var restored = b with
        {
            SchemaVersion = a.SchemaVersion,
            JointSupport = a.JointSupport,
            JointCells = a.JointCells,
            FitFingerprintSha256 = a.FitFingerprintSha256
        };
        if (JsonSerializer.Serialize(restored) != JsonSerializer.Serialize(a)
            || trial.ContentSha256 == control.ContentSha256
            || b.FitFingerprintSha256 == a.FitFingerprintSha256)
            throw new InvalidDataException("V3 alterou TF, passagens, referência, limiar, prior ou outra configuração congelada.");
    }

    private static void RequireExperimentalReceipt(JsonElement receipt,
        V9FrozenTransferExecutorPlan origin, V9FittedSnapshot v2, V9FittedSnapshot v3)
    {
        if (receipt.GetProperty("schema").GetString() != V9FrozenJointTrainingDerivationCli.Version
            || receipt.GetProperty("status").GetString() != "NEW_V3_TRAIN_ONLY_CANDIDATE_NOT_ORIGINAL_FIT"
            || receipt.GetProperty("sourceSnapshotSha256").GetString() != v2.ContentSha256
            || receipt.GetProperty("derivedTrialSnapshotSha256").GetString() != v3.ContentSha256
            || receipt.GetProperty("originalTrainStudySha256").GetString() != origin.SourceStudySha256
            || receipt.GetProperty("pinnedJointPolicySha256").GetString() != PolicySha
            || receipt.GetProperty("originalFitFingerprintSha256").GetString() != v2.Payload.FitFingerprintSha256
            || receipt.GetProperty("newFitFingerprintSha256").GetString() != v3.Payload.FitFingerprintSha256
            || !receipt.GetProperty("jointProbabilityParametersReestimated").GetBoolean()
            || receipt.GetProperty("tfRefitted").GetBoolean()
            || receipt.GetProperty("blockingReoptimized").GetBoolean()
            || receipt.GetProperty("upperRetuned").GetBoolean()
            || receipt.GetProperty("lowerRetuned").GetBoolean()
            || receipt.GetProperty("certified").GetBoolean()
            || receipt.GetProperty("selectedForUse").GetBoolean()
            || receipt.GetProperty("operationallyActivated").GetBoolean()
            || receipt.GetProperty("independentGoldValidated").GetBoolean()
            || receipt.GetProperty("dt15Authorized").GetBoolean())
            throw new InvalidDataException("Recibo experimental não comprova derivação TRAIN V3 separada e não autorizada.");
    }

    public const string PreflightFlag = "--v9-preflight-frozen-v2-v3-pair";
    private sealed record VerifiedPair(
        V9FrozenTransferExecutorPlan Executor,
        V9AdaptiveCalibrationPlan SourcePlan,
        V9SyntheticObservationCapDeclaration SourceCap,
        V9FittedSnapshot Original,
        V9FittedSnapshot Candidate);

    // Same exact SHA, IBGE and TRAIN provenance gate for preflight AND physical
    // evaluation. Do not allow physical-only shortcuts in later refactoring.
    private static async Task<VerifiedPair> VerifyPinnedPairAsync(
        string executorPath, string planPath, string originalPath,
        string trialPath, string receiptPath)
    {
        var executor = JsonSerializer.Deserialize<V9FrozenTransferExecutorPlan>(
            await File.ReadAllBytesAsync(executorPath))
            ?? throw new InvalidDataException("Executor de origem ausente.");
        V9FrozenTransferCli.ValidatePlan(executor);
        if (executor.SourceRunId != 41900357031 || executor.SourceCommitSha != "935f704edd2bceb6a125ecfe532ad5b71f9c89b9"
            || !executor.Arms.Any(x => x.Role == "PRIMARY_TREATMENT"
                && x.OptionId == V9FrozenTransferCli.TreatmentOption
                && x.SnapshotSha256 == OriginalV2 && x.ConflictMargin == .5m))
            throw new InvalidDataException("Origem TF-001 não é o ONE_MF primário pinado.");
        var planBytes = await File.ReadAllBytesAsync(planPath);
        if (Sha(planBytes) != executor.SourcePlanTfSha256)
            throw new InvalidDataException("Plano TF-001 original diverge do SHA.");
        var sourcePlan = JsonSerializer.Deserialize<V9AdaptiveCalibrationPlan>(planBytes)
            ?? throw new InvalidDataException("Plano de origem incompleto.");
        if (sourcePlan.PinnedIbgeBootstrap is not { GeneratorSeed: 42 } bootstrap
            || sourcePlan.ObservationCapDeclaration is not { } sourceCap
            || sourceCap.ObservationsSha256 != executor.SourceObservationsSha256)
            throw new InvalidDataException("Plano original sem referência/cap TRAIN verificável.");

        var original = await V9FittedSnapshotArtifact.ReadPinnedAsync(originalPath, OriginalV2);
        var candidate = await V9FittedSnapshotArtifact.ReadPinnedAsync(trialPath, ExperimentalV3);
        ValidateFrozenPair(original, candidate);
        V9NestedCalibrationCli.RequirePinnedSnapshotReference(
            sourcePlan.PinnedIbgeReferenceVersionId, sourcePlan.PinnedIbgePublishedSha256, bootstrap, original);
        V9NestedCalibrationCli.RequirePinnedSnapshotReference(
            sourcePlan.PinnedIbgeReferenceVersionId, sourcePlan.PinnedIbgePublishedSha256, bootstrap, candidate);
        using (var receipt = JsonDocument.Parse(await File.ReadAllBytesAsync(receiptPath)))
            RequireExperimentalReceipt(receipt.RootElement, executor, original, candidate);

        return new VerifiedPair(executor, sourcePlan, sourceCap, original, candidate);
    }

    /// <summary>
    /// Pure file + provenance audit of the ORIGINAL V2 and NEW V3 real fit
    /// bytes: no SQL, new corpus, TEST or operational model selection.
    /// </summary>
    public static async Task RunPreflightAsync(string[] args)
    {
        if (args.Length != 6 || args[0] != PreflightFlag)
            throw new ArgumentException(
                "Uso: --v9-preflight-frozen-v2-v3-pair <executor.json> <plan-tf.json> <V2.fitted.json> <V3.fitted.json> <derivation-receipt.json>.");
        var verified = await VerifyPinnedPairAsync(args[1], args[2], args[3], args[4], args[5]);
        var gold = V9FittedSnapshotArtifact.DescribeGoldTfDiagnosticReadiness(verified.Candidate);
        if (gold.GetProperty("status").GetString()
            != "READY_FOR_ISOLATED_GOLD_TF_DIAGNOSTIC_ONLY"
            || !gold.GetProperty("goldVariantPreservesFrozenFit").GetBoolean())
            throw new InvalidDataException("V3 congelado não passa na preparação Gold somente diagnóstica.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "V9_FROZEN_V2_V3_PAIR_PREFLIGHT_NO_SQL_V1",
            sourceRunId = verified.Executor.SourceRunId,
            originalV2Sha256 = verified.Original.ContentSha256,
            experimentalV3Sha256 = verified.Candidate.ContentSha256,
            sourceStudySha256 = verified.Executor.SourceStudySha256,
            pinnedJointPolicySha256 = PolicySha,
            originalBlockingPassCount = verified.Original.Payload.BlockingPasses.Count,
            derivedBlockingPassCount = verified.Candidate.Payload.BlockingPasses.Count,
            activeUnitTfCount = verified.Candidate.Payload.UnitTfCalibrations!
                .Count(x => x.State == V9SnapshotTfFitState.Active),
            goldDiagnosticReady = true, independentSqlExecuted = false,
            goldIncrementalExecuted = false, fitRefitted = false, certified = false,
            selectedForUse = false, operationallyActivated = false
        }, Json));
    }

    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 8 || args[0] != Flag)
            throw new ArgumentException(
                "Uso: --v9-compare-frozen-v2-v3-independent <executor.json> <plan-tf.json> <v2.fitted.json> <v3.fitted.json> <derivation-receipt.json> <observacoes.csv> <saída-nova>.");
        var verified = await VerifyPinnedPairAsync(args[1], args[2], args[3], args[4], args[5]);
        var executor = verified.Executor;
        var sourcePlan = verified.SourcePlan;
        var sourceCap = verified.SourceCap;
        var original = verified.Original;
        var candidate = verified.Candidate;

        // Fixed, previously unexposed generation seed; no change or adaptive
        // choice based on outcomes. No TEST rows are read.
        var csv = Path.GetFullPath(args[6]);
        var manifestBytes = await File.ReadAllBytesAsync(
            Path.Combine(Path.GetDirectoryName(csv)!, "generation-manifest.json"));
        var csvSha = Sha(await File.ReadAllBytesAsync(csv));
        using (var doc = JsonDocument.Parse(manifestBytes))
        {
            var m = doc.RootElement;
            if (m.GetProperty("seed").GetUInt64() != PredeclaredSeed
                || m.GetProperty("generator_version").GetString() != sourceCap.GeneratorVersion
                || m.GetProperty("error_profile").GetString() != "clean"
                || m.GetProperty("population_profile").GetString() != "DEMOGRAPHIC_PRIMARY_V1"
                || csvSha == executor.SourceObservationsSha256)
                throw new InvalidDataException("VALIDATION não é coorte nova da seed pré-declarada.");
        }
        var targetCap = sourceCap with { ManifestSha256 = Sha(manifestBytes), ObservationsSha256 = csvSha };
        V9SyntheticObservationCapEvidence.ValidateManifest(
            manifestBytes, targetCap, csvSha, sourcePlan.MaximumAcceptedPerBaseIdentity);

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
            ?? throw new InvalidOperationException("SQL descartável JornadaHarness obrigatório.");
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (builder.InitialCatalog != "JornadaHarness"
            || builder.DataSource is not ("127.0.0.1,1433" or "localhost,1433")
            || builder.IntegratedSecurity)
            throw new InvalidOperationException("Comparação física permitida apenas em SQL Docker descartável 127.0.0.1:1433/JornadaHarness.");
        var output = Path.GetFullPath(args[7]);
        if (Directory.Exists(output) || File.Exists(output))
            throw new InvalidOperationException("Saída de avaliação física precisa ser inédita.");
        Directory.CreateDirectory(output);
        try
        {
            var observations = V9PhysicalCalibrationEvidenceCli.ReadNoCpfTrainValidation(csv, 4,
                sourcePlan.MaximumAcceptedPerBaseIdentity, onlyPartition: "VALIDATION");
            var execution = sourcePlan.Execution with { EndToEndRepetitions = 20 };
            var evaluations = new List<V9FrozenHoldoutEvaluation>();
            foreach (var fit in new[] { original, candidate })
            {
                await using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync();
                var measured = await V9PhysicalSqlAdaptiveCalibrationProbe.EvaluateFrozenHoldoutAsync(
                    connection, observations, fit, execution,
                    sourcePlan.MaximumAcceptedPerBaseIdentity, 2, CancellationToken.None);
                if (measured.Refitted || measured.SnapshotSha256 != fit.ContentSha256)
                    throw new InvalidDataException("Avaliação física modificou o fit congelado.");
                evaluations.Add(measured);
            }
            var old = evaluations[0].Metric;
            var next = evaluations[1].Metric;
            var receipt = new
            {
                schema = Version, originalV2Sha256 = original.ContentSha256,
                experimentalV3Sha256 = candidate.ContentSha256, sourceStudySha256 = executor.SourceStudySha256,
                validationSeed = PredeclaredSeed,
                validationCsvSha256 = csvSha, validationManifestSha256 = Sha(manifestBytes),
                validationObservations = observations.Length,
                validationBaseIdentities = observations.Select(o => o.BasePersonId)
                    .Distinct(StringComparer.Ordinal).Count(),
                comparisonMultiplicity = 2,
                original = evaluations[0], v3Trial = evaluations[1],
                deltaAcceptedTrueSources = next.TrueAcceptedSourceObservations - old.TrueAcceptedSourceObservations,
                deltaAcceptedFalseSources = next.FalseAcceptedSourceObservations - old.FalseAcceptedSourceObservations,
                deltaGlobalSourceRecallPp = 100.0 * (next.GlobalSourceRecall - old.GlobalSourceRecall),
                physicalSqlExecuted = true, blindGroupingOnly = true,
                originalParametersUntouched = true, tfRefitted = false,
                jointParametersRefittedDuringEvaluation = false,
                independentGoldFourWavesThreeOrdersExecuted = false,
                testUsed = false, certified = false, selectedForUse = false,
                operationallyActivated = false, dt15Authorized = false,
                status = "NEW_V3_FROZEN_INDEPENDENT_BLIND_SQL_DESCRIPTIVE_NO_GOLD_CERTIFICATION"
            };
            await File.WriteAllTextAsync(Path.Combine(output, "v3-independent-comparison.json"),
                JsonSerializer.Serialize(receipt, Json));
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                receipt.status, receipt.originalV2Sha256, receipt.experimentalV3Sha256,
                receipt.validationSeed, receipt.deltaAcceptedTrueSources,
                receipt.deltaAcceptedFalseSources, receipt.deltaGlobalSourceRecallPp,
                receipt.certified, receipt.independentGoldFourWavesThreeOrdersExecuted
            }));
        }
        catch (Exception e)
        {
            await File.WriteAllTextAsync(Path.Combine(output, "interrupted.json"),
                JsonSerializer.Serialize(new { completed = false, errorType = e.GetType().Name,
                    certified = false, testUsed = false, activated = false }));
            throw;
        }
    }
}
