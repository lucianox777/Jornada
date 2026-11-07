using System.Security.Cryptography;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Manual default V9 flow. Search and one-time finalist validation are separate commands.</summary>
public static class V9NestedCalibrationCli
{
    /// <summary>
    /// Runtime diagnostics only: no memory limit, GC forcing or model-selection input.
    /// The GC availability figure is the managed-GC budget, not free physical RAM.
    /// RSS is the whole process, including native allocations.
    /// </summary>
    public sealed record V9RamCapacityDiagnostic(
        long ResidentWorkingSetBytes,
        long PeakResidentWorkingSetBytes,
        long ManagedLiveBytesEstimate,
        long GcHeapSizeBytes,
        long GcCommittedBytes,
        long GcTotalAvailableMemoryBytes,
        long? GcMemoryLoadBytes,
        long? GcHighMemoryLoadThresholdBytes,
        bool? GcReportsHighMemoryLoad,
        string? Notice,
        string BatchTrace);

    /// <summary>
    /// Pure, deterministic classification of measurements already exposed by .NET.
    /// Does not invent a percentage threshold, bound workers, or alter execution.
    /// </summary>
    public static V9RamCapacityDiagnostic InspectRamCapacity(
        long residentBytes, long peakResidentBytes, long managedBytes,
        long gcHeapBytes, long gcCommittedBytes, long gcAvailableBytes,
        long gcMemoryLoadBytes, long gcHighMemoryLoadThresholdBytes)
    {
        if (new[] { residentBytes, peakResidentBytes, managedBytes, gcHeapBytes,
            gcCommittedBytes, gcAvailableBytes, gcMemoryLoadBytes,
            gcHighMemoryLoadThresholdBytes }.Any(x => x < 0))
            throw new InvalidDataException("Medição de memória negativa ou inválida no relatório V9.");

        bool? highLoad = gcMemoryLoadBytes > 0 && gcHighMemoryLoadThresholdBytes > 0
            ? gcMemoryLoadBytes >= gcHighMemoryLoadThresholdBytes : null;
        var notice = highLoad is true ? "RAM_CARGA_ALTA_INFORMADA_PELO_GC"
            : highLoad is null ? "RAM_PRESSAO_NAO_DISPONIVEL_PELA_API_GC" : null;
        return new(
            residentBytes, peakResidentBytes, managedBytes, gcHeapBytes,
            gcCommittedBytes, gcAvailableBytes,
            gcMemoryLoadBytes > 0 ? gcMemoryLoadBytes : null,
            gcHighMemoryLoadThresholdBytes > 0 ? gcHighMemoryLoadThresholdBytes : null,
            highLoad, notice, "memory-progress.jsonl");
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>
    /// One error per failed run: the last C# exception, including the stack trace.
    /// Diagnostic I/O must never replace the original failure (notably disk-full).
    /// </summary>
    public static void TryWriteLastError(V9CalibradorLastErrorLog? reservedLog, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        // stderr and the preallocated log are independent channels: a full
        // stderr sink must NOT prevent writing into already reserved blocks.
        try { Console.Error.WriteLine(exception.ToString()); }
        catch (Exception) { /* stderr may itself be on the full volume. */ }
        if (reservedLog is null)
        {
            try { Console.Error.WriteLine("calibrador.log indisponível: reserva de 10 KiB não foi criada."); }
            catch (Exception) { /* preserve the original error */ }
            return;
        }
        try { reservedLog.WriteLast(exception); }
        catch (Exception loggingError)
        {
            // Never replace the original error/exit status with a log I/O failure.
            try
            {
                Console.Error.WriteLine("Não foi possível atualizar calibrador.log: "
                    + loggingError.GetType().Name + ": " + loggingError.Message);
            }
            catch (Exception) { /* Even stderr may be unavailable on a failed process. */ }
        }
    }

    public static async Task RunAsync(string[] args)
    {
        string? output = null;
        V9CalibradorLastErrorLog? reservedLog = null;
        bool completed = false;
        try
        {
            if (args.Length < 1 || (args[0] == "--v9-calibrate" ? args.Length != 4
                : args[0] == "--v9-validate-finalists" ? args.Length != 5 : true))
                throw new ArgumentException("Uso: --v9-calibrate <plano> <CSV> <diretório novo>; --v9-validate-finalists <plano> <SHA.selection.json> <CSV novo> <diretório novo>");
            output = Path.GetFullPath(args[^1]);
            var plan = JsonSerializer.Deserialize<V9NestedCalibrationPlan>(await File.ReadAllBytesAsync(args[1]), Json)
                ?? throw new InvalidDataException("Plano V9 interno ausente.");
            V9NestedCalibrationCoordinator.ValidatePlan(plan);
            if (Directory.Exists(output) || File.Exists(output)) throw new InvalidOperationException("Resultados exigem diretório novo e imutável.");
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
                ?? throw new InvalidOperationException("Conexão do JornadaHarness descartável ausente.");
            if (new SqlConnectionStringBuilder(connectionString).InitialCatalog != "JornadaHarness")
                throw new InvalidOperationException("Calibração física aceita somente JornadaHarness descartável.");
            Directory.CreateDirectory(output);
            // Reserve real disk blocks before opening SQL or any expensive work.
            // If ENOSPC prevents the reservation, fail closed before calibration.
            reservedLog = V9CalibradorLastErrorLog.Reserve(output);
            await using (var ownership = new FileStream(Path.Combine(output, "execution.lock"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await WriteNewAsync(Path.Combine(output, "plan.json"), plan);
                try
                {
                    if (args[0] == "--v9-calibrate")
                        await SearchAsync(args[2], output, plan, connectionString);
                    else await ValidateAsync(args[2], args[3], output, plan, connectionString);
                }
                catch (Exception exception)
                {
                    try
                    {
                        await WriteNewAsync(Path.Combine(output, "interrupted.json"),
                            new { exceptionType = exception.GetType().Name,
                                completed = false, testUsed = false, activated = false });
                    }
                    catch (Exception receiptError)
                    {
                        // A redirected stderr can also be on the full disk.
                        // Never allow that secondary I/O failure to replace
                        // the original error caught by this block.
                        try
                        {
                            Console.Error.WriteLine("Não foi possível gravar interrupted.json: "
                                + receiptError.GetType().Name + ": " + receiptError.Message);
                        }
                        catch (Exception) { /* preserve original exception */ }
                    }
                    throw;
                }
            }
            // Only remove the 10 KiB blank reservation after *all* success I/O
            // and the ownership handle have closed without throwing.
            completed = true;
        }
        catch (Exception exception)
        {
            // Never mutate a pre-existing result directory rejected above.
            TryWriteLastError(reservedLog, exception);
            throw;
        }
        finally
        {
            if (reservedLog is not null)
            {
                try
                {
                    reservedLog.Dispose();
                    if (completed)
                        File.Delete(Path.Combine(output!, V9CalibradorLastErrorLog.FileName));
                }
                catch (Exception cleanupError)
                {
                    // The cleanup warning itself must not mask the original
                    // exception if stderr is redirected onto a full volume.
                    try
                    {
                        Console.Error.WriteLine("Falha ao fechar/remover reserva de log: "
                            + cleanupError.GetType().Name + ": " + cleanupError.Message);
                    }
                    catch (Exception) { /* preserve original exception */ }
                }
            }
        }
    }

    private static async Task SearchAsync(string source, string output, V9NestedCalibrationPlan plan, string connectionString)
    {
        await using var sourceStream = File.OpenRead(source);
        var sourceSha = Convert.ToHexString(await SHA256.HashDataAsync(sourceStream)).ToLowerInvariant();
        var manifestBytes = await File.ReadAllBytesAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!, "generation-manifest.json"));
        var declaration = plan.Calibration.ObservationCapDeclaration
            ?? throw new InvalidDataException("Fluxo padrão exige manifesto e cap da massa-base sintética fixados.");
        V9SyntheticObservationCapEvidence.ValidateManifest(manifestBytes, declaration, sourceSha, plan.Calibration.MaximumAcceptedPerBaseIdentity);
        using var producer = JsonDocument.Parse(manifestBytes);
        if (producer.RootElement.GetProperty("error_profile").GetString() != "clean"
            || producer.RootElement.GetProperty("population_profile").GetString() != "DEMOGRAPHIC_PRIMARY_V1")
            throw new InvalidDataException("Bootstrap padrão exige clean conservador provisório e produtor fixado.");
        var train = V9PhysicalCalibrationEvidenceCli.ReadNoCpfTrainValidation(source, 4,
            declaration.MaximumObservationsPerBaseIdentity, onlyPartition: "TRAIN");
        Func<IReadOnlyList<LinkageBlockingPass>, CancellationToken, Task<FrozenIbgeNominalTfV9?>>? referenceFactory = null;
        if (plan.Calibration.ModelOptions.Any(V9FellegiSunterAdaptiveCalibrationAdapter.HasPositiveTf))
            referenceFactory = async (passes, ct) =>
            {
                // The reference version is explicit; no mutable ATIVA fallback.
                await using var referenceSql = new SqlConnection(connectionString);
                await referenceSql.OpenAsync(ct);
                return await FrozenIbgeNominalTfV9.LoadAsync(referenceSql,
                    plan.PinnedIbgeReferenceVersionId!.Value,
                    V9PhysicalSqlAdaptiveCalibrationProbe.ComputeBlockingFingerprint(passes),
                    plan.PinnedIbgeBootstrap!, ct);
            };
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var result = await V9NestedCalibrationCoordinator.SearchAsync(async ct =>
        {
            var connection = new SqlConnection(connectionString);
            try { await connection.OpenAsync(ct); return connection; }
            catch { await connection.DisposeAsync(); throw; }
        }, train, plan, CancellationToken.None, progress =>
        {
            var line = JsonSerializer.Serialize(progress);
            File.AppendAllText(Path.Combine(output, "batch-progress.jsonl"), line + Environment.NewLine);
            Console.Error.WriteLine(line);
            // Memory is measured AFTER each completed batch; no manual GC and
            // no influence on search decisions, snapshots, or inferential counts.
            process.Refresh();
            var gc = GC.GetGCMemoryInfo();
            var memory = JsonSerializer.Serialize(new
            {
                schema = "V9_CALIBRATION_MEMORY_PER_BATCH_V1",
                progress.Batch, progress.TotalConfigurations,
                elapsedMilliseconds = elapsed.ElapsedMilliseconds,
                managedLiveBytesEstimate = GC.GetTotalMemory(forceFullCollection: false),
                gcHeapSizeBytes = gc.HeapSizeBytes,
                gcCommittedBytes = gc.TotalCommittedBytes,
                gcAvailableMemoryBytes = gc.TotalAvailableMemoryBytes,
                residentWorkingSetBytes = process.WorkingSet64,
                peakResidentWorkingSetBytes = process.PeakWorkingSet64,
                artifactScope = "TRAIN_SELECTION_ONLY_NO_TEST_NO_ACTIVATION"
            });
            File.AppendAllText(Path.Combine(output, "memory-progress.jsonl"), memory + Environment.NewLine);
            Console.Error.WriteLine(memory);
        }, frozenTfReferenceForRuleset: referenceFactory);
        foreach (var fit in result.Evaluations.Where(x => x.Candidate is not null).Select(x => x.Candidate!.Snapshot)
            .DistinctBy(x => x.ContentSha256))
            await V9FittedSnapshotArtifact.WriteNewAsync(Path.Combine(output, fit.ContentSha256 + ".fitted.json"), fit);
        var finalists = result.Evaluations.Where(x => x.Candidate is not null
            && result.FinalistSnapshotShas.Contains(x.Candidate.Snapshot.ContentSha256)).Select(x => x.Candidate!).ToArray();
        var externalDesign = finalists.Length == 0 ? null : V9StatisticalSamplePlan.CalculateFinalistFdrOnly(finalists.Length,
            plan.Calibration.MaximumAcceptedPerBaseIdentity, finalists.Min(x =>
                (x.Metric.TrueAcceptedSourceObservations + x.Metric.FalseAcceptedSourceObservations) / (double)x.Metric.PreBlockingBaseIdentities),
            plan.Calibration.RiskCeilings!);
        await WriteNewAsync(Path.Combine(output, "fresh-holdout-sample-plan.json"), new { externalDesign,
            historicalKnownComparedConfigurations = 14159502, historicalScope = "CONSUMED_HISTORICAL_VALIDATION_ONLY_NOT_FRESH_FINALIST_MULTIPLICITY",
            searchConfigurations = result.Evaluations.Count, externalValidationMultiplicity = result.FinalistSnapshotShas.Count,
            frozenTestMultiplicity = 1, certified = false });
        // Publish large identity-level evidence as a streamed canonical JSON
        // file; no full-sized UTF8 envelope is retained in RAM during hashing.
        var envelopeSha = await WriteContentAddressedSelectionAsync(output, new
        {
            result, externalDesign, externalValidationMultiplicity = result.FinalistSnapshotShas.Count,
            originalObservationsSha256 = sourceSha, producerManifestSha256 = Sha(manifestBytes),
            producerSeed = producer.RootElement.GetProperty("seed").GetUInt64(),
            snapshotValidationSlotPurpose = "TRAIN_SELECTION_NOT_EXTERNAL_VALIDATION",
            validationRule = "UP_TO_3_PRECOMMITTED_FITS_MAX_RECALL_WITH_CP_RATIO_UCB_WITHIN_EPSILON_NO_REFIT",
            statisticalCertification = false, operationalActivation = false
        });
        await WriteNewAsync(Path.Combine(output, "selection-reference.json"), new { envelopeSha256 = envelopeSha,
            result.FinalistSnapshotShas, result.StopReason, externalValidationUsed = false, testUsed = false, activated = false });
        await WriteNewAsync(Path.Combine(output, "training-split-manifest.json"), result.Split);
        await WriteNewAsync(Path.Combine(output, "gain-by-batch.json"), result.Progress);
        if (plan.MuEstimationMethods is not null)
            await WriteMuStatisticalAnalysisAsync(output, plan, result);
        Console.WriteLine(JsonSerializer.Serialize(new { envelopeSha256 = envelopeSha, evaluations = result.Evaluations.Count,
            finalists = result.FinalistSnapshotShas.Count, result.StopReason, externalValidationUsed = false, testUsed = false, activated = false }));
    }


    /// <summary>
    /// Full declared-vs-executed M/U audit; TRAIN_SELECTION only, never validation or certification.
    /// Rows include rejected options and the exact policy, not only winning finalists.
    /// </summary>
    private static async Task WriteMuStatisticalAnalysisAsync(string output,
        V9NestedCalibrationPlan plan, V9NestedCalibrationSearchResult result)
    {
        var declared = V9MuEstimationSearchSpace.Enumerate(plan.MuEstimationMethods!);
        var rows = result.Evaluations.Select(e => new
        {
            e.Batch, e.RequestSha256, e.OptionId, e.MuEstimationMethod,
            e.MuPolicy, e.ConflictMargin, e.EligibleForSelection,
            e.SharedSurnameCandidatePairs, e.CandidatePairsSha256,
            sharedSurnameMode = e.Candidate?.Snapshot.Payload.SharedSurnameMode,
            fitSnapshotSha256 = e.Candidate?.Snapshot.ContentSha256,
            smoothingAlpha = e.Candidate?.Support.SmoothingAlpha,
            // Each of the 81 mutually exclusive M/U states is independently
            // auditable, even for DEFAULT/V2 without a V3 JointSupport payload.
            observedSupportAudit = e.Candidate?.Support,
            candidatePriorTrainingAudit = e.Candidate?.PriorDiagnostic,
            fittedPairPrior = e.Candidate?.Snapshot.Payload.Parameters[LinkageParameterCatalog.PriorMatchProbability],
            estimatorJointSupport = e.Candidate?.Snapshot.Payload.JointSupport,
            blockingPasses = e.Candidate?.Snapshot.Payload.BlockingPasses,
            threshold = e.Candidate?.Snapshot.Payload.Threshold,
            trueAccepted = e.Candidate?.Metric.TrueAcceptedSourceObservations,
            falseAccepted = e.Candidate?.Metric.FalseAcceptedSourceObservations,
            eligibleSources = e.Candidate?.Metric.EligibleSourceObservationsBeforeBlocking,
            recall = e.Candidate?.Metric.GlobalSourceRecall,
            fdrObserved = e.Candidate?.Metric.FdrAfterResolve,
            conditionalClusterCpFdrUpper = e.Candidate?.Metric.ConditionalClusterCpFdrUpper,
            clusterIidIndependentlyValidated = e.Candidate?.Metric.ClusterIidIndependentlyValidated,
            conflicts = e.Candidate?.Metric.Conflicts,
            unresolved = e.Candidate?.Metric.Unresolved,
            // Diagnostic-only, aggregate reasons from the unchanged Resolve
            // kernel. No names, UUIDs, rankings or oracle labels are exported.
            allSelectionObservations = e.Candidate?.Metric.AllValidationObservations,
            resolveReasonCounts = e.Candidate?.Metric.ReasonsByResolveStatus,
            // Only aggregate bins and k-suppressed nominal cells are published;
            // the raw top-two source rows remain JsonIgnore in memory.
            sourceGapAttribution = e.Candidate?.Metric.SourceGapAttribution,
            p95Milliseconds = e.Candidate?.P95Milliseconds,
            e.Rejections,
            outcome = e.Candidate is null ? "REJECTED_NOT_MEASURED"
                : e.EligibleForSelection == false ? "MEASURED_DIAGNOSTIC_NOT_ELIGIBLE"
                : e.Rejections.Count > 0 ? "MEASURED_NOT_ELIGIBLE" : "MEASURED_ELIGIBLE"
        }).ToArray();
        // Match two surname assumptions only when the SAME SQL candidate-pair
        // SHA, M/U policy, margin, blocking and cohort have been measured.
        // This diagnostic is NEVER added to selection/finalists.
        const string sharedPrefix = "TF_SHARED_PL_ML_";
        const string bilateralPrefix = "TF_TWO_PL_ML_";
        var surnameComparisons = result.Evaluations.Where(e =>
                e.OptionId?.StartsWith(sharedPrefix, StringComparison.Ordinal) == true)
            .Select(shared =>
            {
                var expectedBilateralId = bilateralPrefix + shared.OptionId![sharedPrefix.Length..];
                V9NestedCandidateEvaluation[] paired = shared.Candidate is null || shared.CandidatePairsSha256 is null
                    ? [] : result.Evaluations.Where(other =>
                        other.OptionId == expectedBilateralId
                        && other.Batch == shared.Batch
                        && other.ConflictMargin == shared.ConflictMargin
                        && other.Candidate is not null
                        && other.CandidatePairsSha256 == shared.CandidatePairsSha256
                        && JsonSerializer.Serialize(other.MuPolicy) == JsonSerializer.Serialize(shared.MuPolicy)
                        && JsonSerializer.Serialize(other.Candidate!.Snapshot.Payload.BlockingPasses)
                            == JsonSerializer.Serialize(shared.Candidate.Snapshot.Payload.BlockingPasses))
                        .ToArray();
                if (paired.Length > 1)
                    throw new InvalidDataException("Ambiguous V3 surname paired frame; refuse to report spurious deltas.");
                var bilateral = paired.SingleOrDefault();
                if (bilateral is not null
                    && (shared.EligibleForSelection != false
                        // Master #1298: zero is recorded but never selectable.
                        || bilateral.EligibleForSelection != (shared.ConflictMargin > 0m)
                        || shared.SharedSurnameCandidatePairs is null
                        || shared.SharedSurnameCandidatePairs != bilateral.SharedSurnameCandidatePairs
                        || shared.Candidate!.Snapshot.Payload.SharedSurnameMode
                            != V9SharedSurnameModeV9.PersonSideOnlyWhenShared
                        || bilateral.Candidate!.Snapshot.Payload.SharedSurnameMode != V9SharedSurnameModeV9.Bilateral))
                    throw new InvalidDataException("V3 surname options disagree in affected-pair count or candidate eligibility.");
                return new
                {
                    sharedOptionId = shared.OptionId,
                    bilateralOptionId = expectedBilateralId,
                    shared.Batch, shared.ConflictMargin,
                    candidatePairsSha256 = shared.CandidatePairsSha256,
                    sharedSurnameCandidatePairs = shared.SharedSurnameCandidatePairs,
                    samePhysicalCandidateUniverse = bilateral is not null,
                    status = bilateral is null ? "NOT_PAIRED_NO_COMPARABLE_PHYSICAL_EVIDENCE"
                        : shared.SharedSurnameCandidatePairs == 0
                            ? "PAIRED_BUT_ZERO_SHARED_SURNAME_CANDIDATES_NONINFORMATIVE"
                            : "MEASURED_PAIRED_TRAIN_DIAGNOSTIC_ONLY",
                    experimentInformative = bilateral is not null && shared.SharedSurnameCandidatePairs > 0,
                    bilateralEligibleForSelection = bilateral?.EligibleForSelection,
                    sharedEligibleForSelection = shared.EligibleForSelection,
                    bilateralRecall = bilateral?.Candidate?.Metric.GlobalSourceRecall,
                    sharedRecall = shared.Candidate?.Metric.GlobalSourceRecall,
                    deltaRecall = bilateral is null ? (double?)null
                        : shared.Candidate!.Metric.GlobalSourceRecall -
                            bilateral.Candidate!.Metric.GlobalSourceRecall,
                    bilateralFdr = bilateral?.Candidate?.Metric.FdrAfterResolve,
                    sharedFdr = shared.Candidate?.Metric.FdrAfterResolve,
                    deltaFdr = bilateral?.Candidate?.Metric.FdrAfterResolve is { } bFdr
                        && shared.Candidate?.Metric.FdrAfterResolve is { } sFdr
                        ? sFdr - bFdr : (double?)null,
                    bilateralFalseAccepted = bilateral?.Candidate?.Metric.FalseAcceptedSourceObservations,
                    sharedFalseAccepted = shared.Candidate?.Metric.FalseAcceptedSourceObservations,
                    deltaFalseAccepted = bilateral is null ? (long?)null
                        : shared.Candidate!.Metric.FalseAcceptedSourceObservations -
                            bilateral.Candidate!.Metric.FalseAcceptedSourceObservations,
                    bilateralPending = bilateral is null ? (long?)null
                        : bilateral.Candidate!.Metric.Conflicts + bilateral.Candidate.Metric.Unresolved,
                    sharedPending = shared.Candidate is null ? (long?)null
                        : shared.Candidate.Metric.Conflicts + shared.Candidate.Metric.Unresolved,
                    deltaPending = bilateral is null ? (long?)null
                        : (shared.Candidate!.Metric.Conflicts + shared.Candidate.Metric.Unresolved)
                            - (bilateral.Candidate!.Metric.Conflicts + bilateral.Candidate.Metric.Unresolved),
                };
            }).ToArray();
        var cpuCapacity = V9ParallelWork.InspectCpuCapacity(plan.Calibration.MaximumParallelRuleSets);
        // This is a report-time sample plus the process RSS high-water mark.
        // The existing memory-progress.jsonl contains independent per-batch samples.
        // No re-reading of raw training data or forcing a GC collection.
        using var ramProcess = System.Diagnostics.Process.GetCurrentProcess();
        ramProcess.Refresh();
        var gcInfo = GC.GetGCMemoryInfo();
        var ramCapacity = InspectRamCapacity(
            ramProcess.WorkingSet64, ramProcess.PeakWorkingSet64,
            GC.GetTotalMemory(forceFullCollection: false), gcInfo.HeapSizeBytes,
            gcInfo.TotalCommittedBytes, gcInfo.TotalAvailableMemoryBytes,
            gcInfo.MemoryLoadBytes, gcInfo.HighMemoryLoadThresholdBytes);
        // Paired source-level V2/V3 evidence is reduced entirely in memory.
        // Only the aggregate transition counts enter the JSON report.
        // The per-source lists attached to the physical metric are JsonIgnore.
        var pairedSourceRank = result.Evaluations.Where(v2 =>
                v2.Candidate?.Metric.SourceRankInternal is not null
                && v2.MuEstimationMethod == V9MuEstimationSearchSpace.Default
                && v2.OptionId?.Contains("__MU_", StringComparison.Ordinal) == true)
            .SelectMany(v2 =>
            {
                var optionId = v2.OptionId ?? throw new InvalidDataException("Missing base option for paired Rank.");
                var root = optionId[..optionId.IndexOf("__MU_", StringComparison.Ordinal)];
                return result.Evaluations.Where(v3 =>
                    v3.Candidate?.Metric.SourceRankInternal is not null
                    && v3.MuEstimationMethod == V9MuEstimationSearchSpace.Classwise
                    && v3.OptionId?.StartsWith(root + "__MU_", StringComparison.Ordinal) == true
                    && v3.Batch == v2.Batch
                    && v3.ConflictMargin == v2.ConflictMargin
                    && JsonSerializer.Serialize(v3.Candidate!.Snapshot.Payload.BlockingPasses)
                        == JsonSerializer.Serialize(v2.Candidate!.Snapshot.Payload.BlockingPasses))
                    .Select(v3 =>
                    {
                        var left = v2.Candidate!;
                        var right = v3.Candidate!;
                        if (v2.CandidatePairsSha256 is null
                            || v2.CandidatePairsSha256 != v3.CandidatePairsSha256
                            || left.Snapshot.Payload.Parameters[LinkageParameterCatalog.PriorMatchProbability]
                                != right.Snapshot.Payload.Parameters[LinkageParameterCatalog.PriorMatchProbability]
                            || left.Snapshot.Payload.Threshold != right.Snapshot.Payload.Threshold
                            || left.Snapshot.Payload.TfWeight != right.Snapshot.Payload.TfWeight
                            || right.Snapshot.Payload.JointSupport is not { } support)
                            throw new InvalidDataException("V2/V3 source pairing must share SQL frame, prior, TF and threshold.");
                        var fallbackUCells = support.Cells
                            .Where(c => c.MethodU == V9JointSupportEstimation.Marginal)
                            .Select(c => $"{(int)c.Person}/{(int)c.Mother}")
                            .ToHashSet(StringComparer.Ordinal);
                        var mixedUCells = support.Cells
                            .Where(c => c.MethodU == V9JointSupportEstimation.MixedU)
                            .Select(c => $"{(int)c.Person}/{(int)c.Mother}")
                            .ToHashSet(StringComparer.Ordinal);
                        return new
                        {
                            v2RequestSha256 = v2.RequestSha256,
                            v3RequestSha256 = v3.RequestSha256,
                            rootOptionId = root,
                            v2.Batch, v2.ConflictMargin,
                            blockingPassCount = left.Snapshot.Payload.BlockingPasses.Count,
                            candidatePairsSha256 = v2.CandidatePairsSha256,
                            pair = V9PairedSourceRankAttribution.Compare(
                                left.Metric, right.Metric, fallbackUCells, mixedUCells)
                        };
                    });
            }).ToArray();
        var report = new
        {
            schema = "V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1",
            // Diagnostic only, never an input to fit, SHA, FDR or selection.
            cpuCapacity,
            ramCapacity,
            result.PlanSha256,
            originalMethodsAndParameterArrays = plan.MuEstimationMethods,
            declaredVariants = declared.Select(x => new { x.Method, x.Key, x.Policy }).ToArray(),
            declaredVariantCount = declared.Count,
            evaluations = rows.Length,
            measured = rows.Count(x => x.fitSnapshotSha256 is not null),
            rejectedOrIneligible = rows.Count(x => x.outcome != "MEASURED_ELIGIBLE"),
            completeRequestedSpaceOnlyWhenInitialRequestsExecuted = true,
            scope = "TRAIN_SELECTION_ONLY_NOT_INDEPENDENT_VALIDATION",
            conditionalFdrBoundsNotCertified = true,
            testUsed = false, activated = false,
            finalistSnapshotShas = result.FinalistSnapshotShas,
            result.StopReason,
            surnameDependenceComparisons = surnameComparisons,
            pairedSourceRankAttribution = pairedSourceRank,
            sharedSurnameAssumptionChosenForOperation = false,
            rows
        };
        await WriteNewAsync(Path.Combine(output, "mu-statistical-analysis.json"), report);
        var md = new System.Text.StringBuilder();
        md.AppendLine("# Análise estatística M/U — TRAIN_SELECTION");
        md.AppendLine();
        md.AppendLine("Plano SHA-256: `" + result.PlanSha256 + "`. Somente espaço declarado; não houve TEST, certificação nem ativação.");
        md.AppendLine("O arquivo `mu-statistical-analysis.json` guarda todas as avaliações, inclusive recusas e valores M/U exatos.");
        md.AppendLine();
        md.AppendLine("## Diagnóstico de CPUs — execução");
        md.AppendLine();
        md.AppendLine("MaximumParallelRuleSets no plano: `" + cpuCapacity.ConfiguredMaximumParallelRuleSets
            + "`; CPUs lógicas visíveis ao .NET: `"
            + (cpuCapacity.DotNetVisibleLogicalProcessors?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "indisponível")
            + "`; limite efetivo de workers: `" + cpuCapacity.EffectiveMaximumParallelRuleSets + "`.");
        if (cpuCapacity.ExplicitWorkerCountBelowDetectedProcessors)
            md.AppendLine("> **AVISO:** o paralelismo configurado é inferior à quantidade de CPUs lógicas detectadas pelo C#."
                + " A execução foi respeitada sem ajuste automático; verifique a configuração se desejar utilizar mais workers.");
        else if (cpuCapacity.DotNetVisibleLogicalProcessors is null)
            md.AppendLine("> **AVISO:** a configuração manual foi respeitada, mas a contagem de CPUs visíveis ao .NET não pôde ser informada.");
        md.AppendLine("O número de workers é um teto de tarefas simultâneas, não uma medição de utilização da CPU.");
        md.AppendLine();
        md.AppendLine("## Diagnóstico de RAM — execução");
        md.AppendLine();
        md.AppendLine("Memória residente atual do processo: `"
            + (ramCapacity.ResidentWorkingSetBytes / 1048576d).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
            + " MiB`; pico RSS observado desde o início do processo: `"
            + (ramCapacity.PeakResidentWorkingSetBytes / 1048576d).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
            + " MiB`.");
        md.AppendLine("Estimativa de objetos gerenciados vivos: `"
            + (ramCapacity.ManagedLiveBytesEstimate / 1048576d).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
            + " MiB`; heap do último GC: `"
            + (ramCapacity.GcHeapSizeBytes / 1048576d).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
            + " MiB`; memória comprometida pelo GC: `"
            + (ramCapacity.GcCommittedBytes / 1048576d).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
            + " MiB`.");
        md.AppendLine("Orçamento total informado ao GC: `"
            + (ramCapacity.GcTotalAvailableMemoryBytes / 1048576d).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
            + " MiB` (não representa RAM física livre do host)."
            + " Amostras por lote no arquivo já existente: `memory-progress.jsonl`.");
        if (ramCapacity.GcReportsHighMemoryLoad is true)
            md.AppendLine("> **AVISO RAM:** a API do .NET informou carga de memória no limiar alto do GC."
                + " Verifique RAM e outros processos/SQL no ambiente físico; a execução não foi limitada nem alterada.");
        else if (ramCapacity.GcReportsHighMemoryLoad is null)
            md.AppendLine("> **AVISO RAM:** a API do .NET não disponibilizou um par comparável de carga/limiar de memória."
                + " O pico RSS foi registrado, mas não é possível certificar ausência de pressão de RAM.");
        md.AppendLine("Essa amostra não certifica ausência de OOM durante a execução;"
            + " não é limite de RAM, previsão de falha nem resultado estatístico.");
        md.AppendLine();
        md.AppendLine("| Método | Cobertura | Min M | Min U | Cauda retida | Alpha | Threshold | Margem | TP | FP | Recall | FDR observada | Conflitos | Status | Snapshot |");
        md.AppendLine("|---|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|");
        foreach (var e in result.Evaluations)
        {
            var policy = e.MuPolicy;
            var metric = e.Candidate?.Metric;
            var status = e.Candidate is null ? "REJEITADA"
                : e.Rejections.Count > 0 ? "INELEGÍVEL" : "ELEGÍVEL";
            static string Number(object? x) => x?.ToString() ?? "—";
            md.AppendLine("| " + Number(e.MuEstimationMethod) + " | "
                + Number(policy?.ClassMassCoverage) + " | " + Number(policy?.MinimumM)
                + " | " + Number(policy?.MinimumU) + " | "
                + (policy is null ? "—" : policy.SupportedTailRule is null ? "não" : "sim")
                + " | " + Number(e.Candidate?.Support.SmoothingAlpha)
                + " | " + Number(metric?.Threshold) + " | " + Number(e.ConflictMargin)
                + " | " + Number(metric?.TrueAcceptedSourceObservations)
                + " | " + Number(metric?.FalseAcceptedSourceObservations)
                + " | " + Number(metric?.GlobalSourceRecall)
                + " | " + Number(metric?.FdrAfterResolve)
                + " | " + Number(metric?.Conflicts)
                + " | " + status + " | " + Number(e.Candidate?.Snapshot.ContentSha256) + " |");
        }
        md.AppendLine();
        md.AppendLine("FDR = 0 observada não implica risco estatístico zero. Os limites condicionais, " +
            "multiplicidade, dependência entre identidades e motivos das recusas estão no JSON; " +
            "sem VALIDATION independente não há autorização DT-15.");
        var file = Path.Combine(output, "mu-statistical-analysis.md");
        await File.WriteAllTextAsync(file, md.ToString());
    }

    /// <summary>
    /// Before consuming the one-time external VALIDATION receipt, ensure a positive-TF
    /// finalist is bound to the published IBGE digest pinned by the selection plan.
    /// The portable fit contains the selected marginal, not the entire materialized
    /// IBGE catalogue: this check does NOT claim independent full-catalogue attestation.
    /// </summary>
    public static void RequirePinnedFinalistReference(V9NestedCalibrationPlan plan, V9FittedSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        RequirePinnedSnapshotReference(plan.PinnedIbgeReferenceVersionId, plan.PinnedIbgePublishedSha256,
            plan.PinnedIbgeBootstrap, snapshot);
    }

    /// <summary>
    /// Same attestation for any frozen snapshot replay (finalist validation or
    /// frozen cross-seed transfer): each TF-positive unit must be bound to the
    /// pinned published IBGE digest and the bootstrap of the FITTING corpus.
    /// </summary>
    public static void RequirePinnedSnapshotReference(long? pinnedVersionId, string? pinnedPublishedSha256,
        NominalSyntheticIbgeBootstrapV9? pinnedBootstrap, V9FittedSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var activeUnits = snapshot.Payload.UnitTfCalibrations?
            .Where(x => x.State == V9SnapshotTfFitState.Active).ToArray();
        if (snapshot.Payload.TfWeight == 0m && activeUnits is not { Length: > 0 })
            return; // Explicit all-zero fit: no positive TF marginal to certify.
        // A per-unit model has no legacy TfCalibration by design. Require
        // each frozen reference, including ZeroChosen references when present,
        // to attest the same pinned IBGE publication before exposing holdout.
        var calibrations = snapshot.Payload.UnitTfCalibrations is { } units
            ? units.Where(x => x.Calibration is not null)
                .Select(x => x.Calibration!).ToArray()
            : snapshot.Payload.TfCalibration is { } legacy
                ? [legacy] : Array.Empty<V9SnapshotNominalUnitTfCalibration>();
        if (calibrations.Length == 0 || pinnedVersionId is null or <= 0
            || pinnedPublishedSha256 is null
            || pinnedPublishedSha256.Length != 64
            || !pinnedPublishedSha256.All(Uri.IsHexDigit)
            || pinnedBootstrap is null)
            throw new InvalidDataException("Finalista TF positivo sem versão, digest e bootstrap IBGE fixados.");
        foreach (var tf in calibrations)
        {
            var frame = tf.Reference.PopulationFrameFingerprint.Split(':');
            var surname = tf.Reference.Unit == NominalFrequencyUnitV9.LastContentSurname;
            var expectedSource = surname
                ? NominalFrequencySourceV9.IbgeSurnameAnyPositionProxy
                : NominalFrequencySourceV9.IbgePublishedMarginal;
            var expectedScope = tf.Side == CompositeNominalTfSideV9.Person
                ? "SP_3550308_TODOS" : surname ? "BR_TODOS" : "BR_FEMININO";
            if (tf.Reference.Source != expectedSource
                || tf.Reference.SyntheticBootstrap != pinnedBootstrap
                || frame.Length != 4 || frame[0] != "IBGE_PUBLISHED"
                || !string.Equals(frame[2], pinnedPublishedSha256,
                    StringComparison.OrdinalIgnoreCase)
                || frame[3] != expectedScope)
                throw new InvalidDataException(
                    "Finalista TF por unidade não corresponde à publicação IBGE fixada no plano.");
        }
    }

    private static async Task ValidateAsync(string selectionFile, string source, string output,
        V9NestedCalibrationPlan plan, string connectionString)
    {
        var bytes = await File.ReadAllBytesAsync(selectionFile);
        var selectionSha = Sha(bytes);
        if (Path.GetFileName(selectionFile) != selectionSha + ".selection.json")
            throw new InvalidDataException("Envelope diverge do SHA fixado no nome do arquivo.");
        using var envelope = JsonDocument.Parse(bytes);
        var root = envelope.RootElement;
        var result = root.GetProperty("result");
        if (result.GetProperty("Version").GetString() != V9NestedCalibrationCoordinator.Version
            || result.GetProperty("ExternalValidationUsed").GetBoolean() || result.GetProperty("TestUsed").GetBoolean()
            || result.GetProperty("PlanSha256").GetString() != V9InternalTrainingSplit.Hash(JsonSerializer.Serialize(plan)))
            throw new InvalidDataException("Envelope não corresponde à seleção interna e ao plano congelado.");
        var ids = result.GetProperty("FinalistSnapshotShas").EnumerateArray().Select(x => x.GetString()!).ToArray();
        if (ids.Length is < 1 or > 3 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new InvalidDataException("Sem um a três finalistas distintos: não abrir VALIDATION.");
        var original = root.GetProperty("originalObservationsSha256").GetString()!;
        var manifestBytes = await File.ReadAllBytesAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!, "generation-manifest.json"));
        await using var csv = File.OpenRead(source);
        var sourceSha = Convert.ToHexString(await SHA256.HashDataAsync(csv)).ToLowerInvariant();
        var fresh = V9FreshHoldoutProtocol.Inspect(manifestBytes, sourceSha, original,
            root.GetProperty("producerSeed").GetUInt64(), plan.Calibration.MaximumAcceptedPerBaseIdentity,
            isPlumbingFixture: plan.Purpose == "PLUMBING_FIXTURE_NOT_CERTIFICATION");
        var snapshots = new List<V9FittedSnapshot>();
        foreach (var id in ids)
        {
            if (id is not { Length: 64 } || !id.All(Uri.IsHexDigit)) throw new InvalidDataException("SHA de finalista inválido.");
            var snapshot = await V9FittedSnapshotArtifact.ReadPinnedAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(selectionFile))!, id + ".fitted.json"), id);
            RequirePinnedFinalistReference(plan, snapshot);
            snapshots.Add(snapshot);
        }
        var registry = Environment.GetEnvironmentVariable("JORNADA_V9_HOLDOUT_REGISTRY")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jornada", "V9", "holdout-exposures");
        await V9FreshHoldoutProtocol.ReserveOnceAsync(registry, fresh, selectionSha, ids, CancellationToken.None);
        // No reading of holdout attribute values before reserving this exact family.
        var validation = V9PhysicalCalibrationEvidenceCli.ReadNoCpfTrainValidation(source, 4,
            plan.Calibration.MaximumAcceptedPerBaseIdentity, onlyPartition: "VALIDATION")
            .Select(x => x with { BasePersonId = fresh.Namespace + x.BasePersonId }).ToArray();
        var measured = await V9ParallelWork.MapAsync(snapshots, plan.Calibration.MaximumParallelRuleSets, async (snapshot, ct) =>
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);
            return await V9PhysicalSqlAdaptiveCalibrationProbe.EvaluateFrozenHoldoutAsync(connection, validation,
                snapshot, plan.Calibration.Execution, plan.Calibration.MaximumAcceptedPerBaseIdentity, ids.Length, ct);
        }, CancellationToken.None);
        var best = SelectConditionalWinner(measured, plan.Calibration.SelectionPolicy,
            plan.Calibration.Budget.MaximumBlockingFalseDegreeP99);
        await WriteNewAsync(Path.Combine(output, "finalist-validation.json"), new { selectionEnvelopeSha256 = selectionSha,
            fresh, comparedConfigurations = ids.Length, measured,
            bestConditionalSnapshotSha256 = best?.SnapshotSha256, selectedApprovedSnapshotSha256 = (string?)null,
            status = best is null ? "NO_FINALIST_PASSES_DECLARED_CONDITIONAL_GATES" : "CONDITIONAL_ONLY_IID_NOT_INDEPENDENTLY_VALIDATED",
            identityClusterIidIndependentlyValidated = false, testUsed = false, refitted = false, certified = false, activated = false });
        Console.WriteLine(JsonSerializer.Serialize(new { finalists = ids.Length, conditionalWinner = best?.SnapshotSha256,
            refitted = false, certified = false, testUsed = false, activated = false }));
    }

    public static V9FrozenHoldoutEvaluation? SelectConditionalWinner(IReadOnlyList<V9FrozenHoldoutEvaluation> measured,
        V9BootstrapPareto2D.SelectionPolicy policy, double maximumFalseDegreeP99)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(policy);
        if (measured.Count is < 1 or > 3 || measured.Select(x => x.SnapshotSha256).Distinct(StringComparer.Ordinal).Count() != measured.Count
            || measured.Any(x => x.Refitted || x.Metric.SyntheticGoldOracleGrouping
                || x.Metric.BonferroniComparedConfigurations != measured.Count))
            throw new InvalidDataException("Escolha exige a mesma família de até três finalistas congelados, sem ORACLE/refit.");
        return measured.Where(x => x.Metric.ConditionalClusterCpFdrUpper.HasValue
            && x.Metric.ConditionalClusterCpFdrUpper <= policy.MaximumAcceptedFdrUpper
            && x.Metric.GlobalSourceRecall >= policy.MinimumGlobalRecall
            && x.Metric.PreBlockingBaseIdentities >= policy.MinimumIndependentIdentityGroups
            && x.P95Milliseconds.HasValue && x.P95Milliseconds <= policy.MaximumP95Milliseconds
            && x.FalseDegreeP99 <= maximumFalseDegreeP99)
            .OrderByDescending(x => x.Metric.GlobalSourceRecall).ThenBy(x => x.Metric.ConditionalClusterCpFdrUpper)
            .ThenBy(x => x.SnapshotSha256, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>
    /// Exact canonical JSON bytes are written once before hashing and published
    /// through same-directory atomic rename. The temp file is never a source of
    /// model parameters or a checkpoint; it does not persist mutable TRAIN state.
    /// </summary>
    public static async Task<string> WriteContentAddressedSelectionAsync<T>(string output, T envelope)
    {
        var pending = Path.Combine(output, ".selection.pending.json");
        await WriteNewAsync(pending, envelope);
        string digest;
        await using (var input = new FileStream(pending, FileMode.Open, FileAccess.Read, FileShare.Read))
            digest = Convert.ToHexString(await SHA256.HashDataAsync(input)).ToLowerInvariant();
        File.Move(pending, Path.Combine(output, digest + ".selection.json"));
        return digest;
    }

    private static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static async Task WriteNewAsync<T>(string path, T value)
    {
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(file, value, Json);
    }
}
