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

    /// <summary>
    /// Descriptive wall-clock stages, never part of scientific model selection.
    /// SQL/blocking/fits may overlap internally and are deliberately measured as one phase.
    /// Output persistence after report assembly is reported separately by runtime-diagnostics.json.
    /// </summary>
    public sealed record V9TimedStage(string StageId, string Description, string Status,
        long? ElapsedMilliseconds, string Scope);
    public sealed record V9StageDurationSummary(string Schema, long TotalMeasuredMilliseconds,
        string TotalScope, bool SequentialStages, string UnmeasuredScope,
        IReadOnlyList<V9TimedStage> Stages);

    public static V9StageDurationSummary DescribeStageDurations(long inputPreparationMs,
        long physicalCalibrationMs, long evidenceExportBeforeReportMs, long? reportAssemblyMs)
    {
        if (inputPreparationMs < 0 || physicalCalibrationMs < 0 || evidenceExportBeforeReportMs < 0
            || reportAssemblyMs is < 0)
            throw new InvalidDataException("Etapa temporal negativa no relatório V9.");
        return new("V9_STAGE_DURATION_DIAGNOSTIC_V1",
            checked(inputPreparationMs + physicalCalibrationMs + evidenceExportBeforeReportMs
                + (reportAssemblyMs ?? 0)),
            "ENTRADA_ATE_RELATORIO_EM_MEMORIA; tempo total wall-clock final em runtime-diagnostics.json",
            true, "Persistência do relatório JSON/Markdown, runtime-diagnostics.json, SQL e temporários externos não desmembrados.",
            new V9TimedStage[]
            {
                new("input_preparation", "Hash e validação do corpus/manifesto; leitura TRAIN sem CPF",
                    "MEASURED", inputPreparationMs, "IN_MEMORY_AND_FILE_READ"),
                new("physical_sql_blocking_fit_selection",
                    "Operação C# composta: SQL/blocking, M/U/TF, avaliação e seleção (subetapas internas não isoláveis)",
                    "MEASURED_COMPOSITE", physicalCalibrationMs, "TRAIN_SELECTION_NO_TEST"),
                new("evidence_export_before_analysis",
                    "Fits, recibos, seleção, split e ganhos gravados antes da análise estatística",
                    "MEASURED", evidenceExportBeforeReportMs, "LOCAL_NEW_OUTPUT_DIRECTORY"),
                new("statistical_report_assembly",
                    "Montagem das estruturas do relatório estatístico, antes da serialização final",
                    reportAssemblyMs is null ? "NOT_MEASURED" : "MEASURED", reportAssemblyMs,
                    "IN_MEMORY_JSON_AND_MARKDOWN")
            });
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>
    /// One safe error code per failed run; exception content is private and
    /// must not reach stderr or artifacts, even under disk-full conditions.
    /// Diagnostic I/O must never replace the original failure.
    /// </summary>
    public static void TryWriteLastError(V9CalibradorLastErrorLog? reservedLog, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        // stderr and the preallocated log are independent channels: a full
        // stderr sink must NOT prevent writing into already reserved blocks.
        try { Console.Error.WriteLine(V9CalibradorLastErrorLog.DetailedErrorCode(exception)); }
        catch (Exception) { /* stderr may itself be on the full volume. */ }
        // Full exception only in private DEV/HML diagnostics; reserved log stays safe.
        V9WorkerDevelopmentDiagnostics.WriteException(exception);
        if (reservedLog is null)
        {
            // The primary safe code is already public. No free-text log notice.
            return;
        }
        try { reservedLog.WriteLast(exception); }
        catch (Exception)
        {
            // Never replace the original error/exit status with a log I/O failure.
            try
            {
                Console.Error.WriteLine("V9_CALIBRATOR_LOG:WRITE_FAILED");
            }
            catch (Exception) { /* Even stderr may be unavailable on a failed process. */ }
        }
    }

    public static async Task RunAsync(string[] args)
    {
        string? output = null;
        V9CalibradorLastErrorLog? reservedLog = null;
        bool completed = false;
        // Fixed allowlisted phase names only: never serialize exceptions, SQL or input rows.
        string safeStage = "CLI_ARGUMENTS_AND_PLAN";
        try
        {
            if (args.Length < 1 || (args[0] == "--v9-calibrate" ? args.Length != 4
                : args[0] == "--v9-validate-finalists" ? args.Length != 5 : true))
                throw new ArgumentException("Uso: --v9-calibrate <plano> <CSV> <diretório novo>; --v9-validate-finalists <plano> <SHA.selection.json> <CSV novo> <diretório novo>");
            output = Path.GetFullPath(args[^1]);
            var originalPlanBytes = await File.ReadAllBytesAsync(args[1]);
            V9OptionalCompatibilityParameters.ValidateRawInput(originalPlanBytes);
            var plan = JsonSerializer.Deserialize<V9NestedCalibrationPlan>(originalPlanBytes, Json)
                ?? throw new InvalidDataException("Plano V9 interno ausente.");
            V9NestedCalibrationCoordinator.ValidatePlan(plan);
            safeStage = "PLAN_VALIDATED_AND_OUTPUT_ADMISSION";
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
                    {
                        // Resource budgeting belongs to --v9-calibrate itself,
                        // not an optional CI/Docker workflow for 100k alone.
                        var resources = V9CalibrationResourceGovernor.Capture(
                            plan.Calibration.MaximumParallelRuleSets, output);
                        await WriteNewAsync(Path.Combine(output, "calibration-resource-budget.json"), resources);
                        await using var diskSampler = new V9CalibrationDiskSampler(output,
                            TimeSpan.FromMilliseconds(500));
                        await SearchAsync(args[2], output, plan, connectionString, originalPlanBytes,
                            diskSampler, resources, phase => safeStage = phase);
                    }
                    else await ValidateAsync(args[2], args[3], output, plan, connectionString);
                }
                catch (Exception exception)
                {
                    try
                    {
                        await WriteNewAsync(Path.Combine(output, "interrupted.json"),
                            new { exceptionType = exception.GetType().Name, safeFailureStage = safeStage,
                                safeFailureCategory = V9CalibradorSafeFailureCategory.Classify(exception),
                                safeFailureReasonCode = V9CalibradorSafeFailureCategory.PhysicalSqlProbeReason(exception),
                                completed = false, testUsed = false, activated = false });
                    }
                    catch (Exception)
                    {
                        // A redirected stderr can also be on the full disk.
                        // Never allow that secondary I/O failure to replace
                        // the original error caught by this block.
                        try
                        {
                            Console.Error.WriteLine("V9_CALIBRATOR_RECEIPT:WRITE_FAILED");
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
            // Safe finite phase code is independent of the exception message,
            // which may include credentials or private paths and stays sealed.
            // Before the destination directory is established, CLI arity must
            // emit only a single fixed code (A2 real-process canary).
            if (output is not null)
            {
                try { Console.Error.WriteLine("V9_CALIBRATOR_SAFE_STAGE:" + safeStage); }
                catch (Exception) { /* stderr may be unavailable under ENOSPC. */ }
            }
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
                catch (Exception)
                {
                    // The cleanup warning itself must not mask the original
                    // exception if stderr is redirected onto a full volume.
                    try
                    {
                        Console.Error.WriteLine("V9_CALIBRATOR_LOG:CLEANUP_FAILED");
                    }
                    catch (Exception) { /* preserve original exception */ }
                }
            }
        }
    }

    private static async Task SearchAsync(string source, string output, V9NestedCalibrationPlan plan,
        string connectionString, byte[] originalPlanBytes, V9CalibrationDiskSampler diskSampler,
        V9CalibrationExecutionResourceBudget resources, Action<string> updateSafeStage)
    {
        ArgumentNullException.ThrowIfNull(updateSafeStage);
        var sqlPoolMaximum = new SqlConnectionStringBuilder(connectionString).MaxPoolSize;
        var resourceScheduler = new V9CalibrationRuntimeScheduler(resources, output, sqlPoolMaximum,
            sourcePath: source);
        updateSafeStage("TRAIN_HASH_AND_MANIFEST");
        var totalWallClock = System.Diagnostics.Stopwatch.StartNew();
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
        updateSafeStage("TRAIN_CSV_LOAD");
        var train = V9PhysicalCalibrationEvidenceCli.ReadNoCpfTrainValidation(source, 4,
            declaration.MaximumObservationsPerBaseIdentity, onlyPartition: "TRAIN");
        var inputPreparationMs = totalWallClock.ElapsedMilliseconds;
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
        updateSafeStage("TRAIN_SPLIT_AND_PHYSICAL_SQL_FIT");
        // One-shot startup probe: persist, close the file, then emit and flush
        // one console line. No Python coupling or periodic heartbeat.
        var startupLogPath = Path.Combine(output, "startup-progress.jsonl");
        var startupLine = JsonSerializer.Serialize(new
        {
            schema = "V9_CALIBRATION_STARTUP_V1",
            status = "STARTED",
            timestampUtc = DateTimeOffset.UtcNow,
            artifactScope = "TRAIN_SELECTION_ONLY_NO_TEST_NO_ACTIVATION"
        });
        File.AppendAllText(startupLogPath, startupLine + Environment.NewLine);
        Console.Out.WriteLine("[V9 START] C# calibrator started; startup-progress.jsonl persisted and closed");
        Console.Out.Flush();
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
            diskSampler.ObserveNow(); // diagnosis only; no changes to selection.
        }, frozenTfReferenceForRuleset: referenceFactory,
            executionMaximumParallelRuleSets: resources.EffectiveParallelism,
            reportSafeStage: updateSafeStage,
            resourceScheduler: resourceScheduler,
            reportHypothesis: null);
        // Aggregate-only operations evidence; never includes connection strings,
        // identities, output rows, or raw SQL diagnostics.
        await WriteNewAsync(Path.Combine(output, "calibration-runtime-scheduling.json"),
            resourceScheduler.Receipts);
        updateSafeStage("FIT_EVIDENCE_EXPORT");
        var calibrationFinishedMs = elapsed.ElapsedMilliseconds;
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
        V9StageDurationSummary? stageDurations = null;
        var evidenceExportBeforeAnalysisMs = elapsed.ElapsedMilliseconds - calibrationFinishedMs;
        diskSampler.ObserveNow();
        if (plan.MuEstimationMethods is not null)
            stageDurations = await WriteMuStatisticalAnalysisAsync(output, plan, result, originalPlanBytes,
                sourceSha, Sha(manifestBytes), producer.RootElement.GetProperty("seed").GetUInt64(),
                inputPreparationMs, calibrationFinishedMs, evidenceExportBeforeAnalysisMs,
                diskSampler.Snapshot(), resources);
        process.Refresh();
        var searchExportFinishedMs = elapsed.ElapsedMilliseconds;
        await WriteNewAsync(Path.Combine(output, "runtime-diagnostics.json"), new
        {
            schema = "V9_LOCAL_RUNTIME_DIAGNOSTICS_V1",
            operation = "TRAIN_SELECTION",
            totalMilliseconds = searchExportFinishedMs,
            totalWallClockIncludingInputPreparationMilliseconds = totalWallClock.ElapsedMilliseconds,
            stageDurations,
            diskHighWaterOutputAtRuntime = diskSampler.Snapshot(),
            calibrationExecutionResourceBudget = resources,
            phasesMilliseconds = new
            {
                physicalCalibration = calibrationFinishedMs,
                evidenceExport = searchExportFinishedMs - calibrationFinishedMs
            },
            visibleLogicalProcessors = Environment.ProcessorCount,
            configuredParallelism = plan.Calibration.MaximumParallelRuleSets,
            effectiveParallelism = resources.EffectiveParallelism,
            peakWorkingSetBytes = process.PeakWorkingSet64,
            operatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            dotNetVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            machineOrRunner = Environment.MachineName,
            temporalComparability = "TEMPO INFORMATIVO: sem comparabilidade entre máquinas, SOs ou runners e sujeito à carga concorrente no mesmo host.",
            distribution = "LOCAL: uma máquina; tarefas C#/.NET escalonadas pelo SO.",
            scientificSelectionUsesElapsedTime = false,
            testUsed = false, activated = false
        });
        Console.WriteLine(JsonSerializer.Serialize(new { envelopeSha256 = envelopeSha, evaluations = result.Evaluations.Count,
            finalists = result.FinalistSnapshotShas.Count, result.StopReason, externalValidationUsed = false, testUsed = false, activated = false }));
    }


    /// <summary>Identical in JSON and human report: measured witnesses are never selectable.</summary>
    public static string HumanReadableEvaluationStatus(bool measured, bool? eligible,
        int rejectionCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rejectionCount);
        return !measured ? "REJEITADA" : eligible == false
            ? "DIAGNÓSTICO_NÃO_SELECIONÁVEL"
            : rejectionCount > 0 ? "INELEGÍVEL" : "ELEGÍVEL";
    }

    /// <summary>
    /// Full declared-vs-executed M/U audit; TRAIN_SELECTION only, never validation or certification.
    /// Rows include rejected options and the exact policy, not only winning finalists.
    /// </summary>
    private static async Task<V9StageDurationSummary> WriteMuStatisticalAnalysisAsync(string output,
        V9NestedCalibrationPlan plan, V9NestedCalibrationSearchResult result,
        byte[] originalPlanBytes, string sourceObservationsSha256,
        string producerManifestSha256, ulong producerSeed,
        long inputPreparationMs, long physicalCalibrationMs, long evidenceExportBeforeAnalysisMs,
        V9DiskHighWaterDiagnostic diskHighWater, V9CalibrationExecutionResourceBudget resources)
    {
        var reportAssemblyTimer = System.Diagnostics.Stopwatch.StartNew();
        var declared = V9MuEstimationSearchSpace.Enumerate(plan.MuEstimationMethods!);
        var perOptionParameterOrigins = V9OptionalCompatibilityParameters.Describe(
            originalPlanBytes, plan, result.Evaluations);
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
        var md = new System.Text.StringBuilder();
        md.AppendLine("# Análise estatística M/U — TRAIN_SELECTION");
        md.AppendLine();
        md.AppendLine("Plano SHA-256: `" + result.PlanSha256 + "`. Somente espaço declarado; não houve TEST, certificação nem ativação.");
        md.AppendLine("O arquivo `mu-statistical-analysis.json` guarda todas as avaliações, inclusive recusas e valores M/U exatos.");
        md.AppendLine("A rastreabilidade de corpus, seed e partição interna está em digests; a comparação histórica é somente analítica e não reabre seleção.");
        md.AppendLine("WITNESS/ANCHOR, lambda=0 e controle de 19 passes são DIAGNÓSTICO_NÃO_SELECIONÁVEL mesmo quando fisicamente medidos.");
        md.AppendLine("Cada opção inclui `perOptionParameterOrigins` com `FROM_PLAN` e `CODE_DEFAULT` e os valores efetivos reconhecidos; nenhum default histórico foi alterado.");
        md.AppendLine();
        md.AppendLine("## Diagnóstico de CPUs — execução");
        md.AppendLine();
        md.AppendLine("MaximumParallelRuleSets no plano: `" + cpuCapacity.ConfiguredMaximumParallelRuleSets
            + "`; CPUs lógicas visíveis ao .NET: `"
            + (cpuCapacity.DotNetVisibleLogicalProcessors?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "indisponível")
            + "`; teto teórico por CPUs: `" + cpuCapacity.EffectiveMaximumParallelRuleSets
            + "`; limite real aplicado pelo orçamento de RAM/disco: `" + resources.EffectiveParallelism
            + "` (modo " + resources.SchedulingMode + ").");
        if (cpuCapacity.ExplicitWorkerCountBelowDetectedProcessors)
            md.AppendLine("> **AVISO:** o paralelismo configurado é inferior à quantidade de CPUs lógicas detectadas pelo C#."
                + " O valor explícito do plano foi respeitado sem alteração automática.");
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
                + " Consulte calibration-resource-budget.json: o escalonamento AUTO foi definido na entrada, sem mudar a ciência.");
        else if (ramCapacity.GcReportsHighMemoryLoad is null)
            md.AppendLine("> **AVISO RAM:** a API do .NET não disponibilizou um par comparável de carga/limiar de memória."
                + " O pico RSS foi registrado, mas não é possível certificar ausência de pressão de RAM.");
        md.AppendLine("Essa amostra não certifica ausência de OOM durante a execução;"
            + " não é limite de RAM, previsão de falha nem resultado estatístico.");
        md.AppendLine();
        md.AppendLine("## Diagnóstico de disco — pico lógico amostrado (somente saídas do Calibrador)");
        md.AppendLine();
        if (diskHighWater.PeakLogicalBytes is { } peak)
        {
            md.AppendLine("Pico de tamanho lógico acumulado dos arquivos sob o diretório de saída: `"
                + (peak / 1048576d).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                + " MiB`; linha de base: `"
                + (diskHighWater.BaselineLogicalBytes.GetValueOrDefault() / 1048576d)
                    .ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                + " MiB`; amostra atual: `"
                + (diskHighWater.CurrentLogicalBytes.GetValueOrDefault() / 1048576d)
                    .ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                + " MiB`. Estado: `" + diskHighWater.Status + "`.");
        }
        else md.AppendLine("Pico de arquivo de saída: **NÃO MEDIDO**; indisponibilidade de telemetria.");
        md.AppendLine("Escopo: `" + diskHighWater.MeasurementScope
            + "`; amostragem a cada `" + diskHighWater.SamplingIntervalMilliseconds
            + " ms` e ao fim de lotes. Sucessos: `" + diskHighWater.SuccessfulSamples
            + "`; falhas: `" + diskHighWater.FailedSamples + "`.");
        md.AppendLine("**NÃO MEDIDO:** espaço físico alocado (blocos), SQL Server/container, temporários externos,"
            + " input do corpus e ocupação/livre do volume compartilhado. Arquivos efêmeros entre amostras"
            + " podem escapar do pico. Não atribuir a este número consumo de todo o host;"
            + " sem interferência em ajuste, FDR, ranking ou ativação.");
        md.AppendLine();
        md.AppendLine("| Método | Cobertura | Min M | Min U | Cauda retida | Alpha | Threshold | Margem | TP | FP | Recall | FDR observada | Conflitos | Status | Snapshot |");
        md.AppendLine("|---|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|");
        foreach (var e in result.Evaluations)
        {
            var policy = e.MuPolicy;
            var metric = e.Candidate?.Metric;
            var status = HumanReadableEvaluationStatus(e.Candidate is not null,
                e.EligibleForSelection, e.Rejections.Count);
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
        // The assembly clock ends BEFORE the final JSON/Markdown persistence, which
        // cannot be measured in its own immutable output without a second rewrite.
        // This boundary is explicit; the runtime report separately measures the whole job.
        var stageDurations = DescribeStageDurations(inputPreparationMs, physicalCalibrationMs,
            evidenceExportBeforeAnalysisMs, reportAssemblyTimer.ElapsedMilliseconds);
        md.AppendLine();
        md.AppendLine("## Duração por etapa — medição de relógio monotônico (Stopwatch)");
        md.AppendLine();
        md.AppendLine("| Etapa | Duração (ms) | Estado | Escopo |");
        md.AppendLine("|---|---:|---|---|");
        foreach (var stage in stageDurations.Stages)
            md.AppendLine("| " + stage.StageId + " | " +
                (stage.ElapsedMilliseconds?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "NÃO MEDIDO")
                + " | " + stage.Status + " | " + stage.Scope + " |");
        md.AppendLine("**Total medido até montagem do relatório:** " +
            stageDurations.TotalMeasuredMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " ms. **Não é a duração final ponta a ponta**: consulte `runtime-diagnostics.json` para total de"
            + " entrada até persistência; o tempo de gravação do próprio relatório fica fora desta soma.");
        md.AppendLine("As subetapas SQL/blocking, M/U/TF e seleção são internas à fase composta e"
            + " não têm tempos independentes: **NÃO MEDIDO** individualmente;"
            + " execuções paralelas não são somadas duas vezes."
            + " Tempos não alimentam fitting, FDR, ranking ou ativação.");
        var report = new
        {
            schema = "V9_MU_STATISTICAL_ANALYSIS_TRAIN_SELECTION_V1",
            // Diagnostic only, never an input to fit, SHA, FDR or selection.
            cpuCapacity,
            calibrationExecutionResourceBudget = resources,
            ramCapacity,
            stageDurations,
            diskHighWater,
            result.PlanSha256,
            // Hash-only lineage for the mandatory analytic-only V2 comparison.
            // No identities, source rows, alternative plan or model selection.
            sourceObservationsSha256, producerManifestSha256, producerSeed,
            trainOriginalSha256 = result.Split.OriginalTrainSha256,
            trainSplitSeed = result.Split.Seed,
            trainSplitAssignmentsSha256 = result.Split.AssignmentSha256,
            originalMethodsAndParameterArrays = plan.MuEstimationMethods,
            perOptionParameterOrigins,
            declaredVariants = declared.Select(x => new { x.Method, x.Key, x.Policy }).ToArray(),
            declaredVariantCount = declared.Count,
            evaluations = rows.Length,
            measured = rows.Count(x => x.fitSnapshotSha256 is not null),
            publishedTfCoverageInsufficient = rows.Count(x =>
                x.Rejections.Contains(V9CalibradorSafeFailureCategory.PublishedTfCoverageInsufficient)),
            scientificallyNonEvaluable = rows.Count(x =>
                x.fitSnapshotSha256 is null
                && x.Rejections.Contains(V9CalibradorSafeFailureCategory.PublishedTfCoverageInsufficient)),
            rejectedOrIneligible = rows.Count(x => x.outcome != "MEASURED_ELIGIBLE"),
            completeRequestedSpaceOnlyWhenInitialRequestsExecuted = true,
            scope = "TRAIN_SELECTION_ONLY_NOT_INDEPENDENT_VALIDATION",
            conditionalFdrBoundsNotCertified = true,
            testUsed = false, activated = false,
            finalistSnapshotShas = result.FinalistSnapshotShas,
            predeclaredCandidateCpMultiplicity = result.PredeclaredCandidateCpMultiplicity,
            duplicateCandidatePolicySelections = result.DuplicatePolicySelections,
            result.StopReason,
            surnameDependenceComparisons = surnameComparisons,
            pairedSourceRankAttribution = pairedSourceRank,
            sharedSurnameAssumptionChosenForOperation = false,
            rows
        };
        await WriteNewAsync(Path.Combine(output, "mu-statistical-analysis.json"), report);
        var file = Path.Combine(output, "mu-statistical-analysis.md");
        await File.WriteAllTextAsync(file, md.ToString());
        return stageDurations;
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

    // Read only validation admission fields; skip multi-gigabyte evaluations.
    private sealed class V9ValidationSelectionEnvelope
    {
        public V9ValidationSelectionResult? result { get; set; }
        public string? originalObservationsSha256 { get; set; }
        public ulong? producerSeed { get; set; }
    }

    private sealed class V9ValidationSelectionResult
    {
        public string? Version { get; set; }
        public bool? ExternalValidationUsed { get; set; }
        public bool? TestUsed { get; set; }
        public string? PlanSha256 { get; set; }
        public string[]? FinalistSnapshotShas { get; set; }
    }

    private static async Task ValidateAsync(string selectionFile, string source, string output,
        V9NestedCalibrationPlan plan, string connectionString)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        // Hash exact original bytes without loading the selection envelope into RAM.
        string selectionSha;
        await using (var hashStream = File.OpenRead(selectionFile))
            selectionSha = Convert.ToHexString(await SHA256.HashDataAsync(hashStream)).ToLowerInvariant();
        if (Path.GetFileName(selectionFile) != selectionSha + ".selection.json")
            throw new InvalidDataException("Envelope diverge do SHA fixado no nome do arquivo.");
        V9ValidationSelectionEnvelope? envelope;
        await using (var selectionStream = File.OpenRead(selectionFile))
            envelope = await JsonSerializer.DeserializeAsync<V9ValidationSelectionEnvelope>(selectionStream);
        var result = envelope?.result;
        if (result is null || result.Version != V9NestedCalibrationCoordinator.Version
            || result.ExternalValidationUsed != false || result.TestUsed != false
            || result.PlanSha256 != V9InternalTrainingSplit.Hash(JsonSerializer.Serialize(plan)))
            throw new InvalidDataException("Envelope não corresponde à seleção interna e ao plano congelado.");
        var ids = result.FinalistSnapshotShas;
        if (ids is null || ids.Length is < 1 or > 3
            || ids.Any(id => string.IsNullOrWhiteSpace(id))
            || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new InvalidDataException("Sem um a três finalistas distintos: não abrir VALIDATION.");
        var original = envelope!.originalObservationsSha256
            ?? throw new InvalidDataException("Envelope sem SHA das observações originais.");
        var manifestBytes = await File.ReadAllBytesAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!, "generation-manifest.json"));
        await using var csv = File.OpenRead(source);
        var sourceSha = Convert.ToHexString(await SHA256.HashDataAsync(csv)).ToLowerInvariant();
        var fresh = V9FreshHoldoutProtocol.Inspect(manifestBytes, sourceSha, original,
            envelope.producerSeed ?? throw new InvalidDataException("Envelope sem seed de produção."), plan.Calibration.MaximumAcceptedPerBaseIdentity,
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
        var physicalEvaluationFinishedMs = elapsed.ElapsedMilliseconds;
        // Model choice is scientific and independent of variable runtime P95.
        // Keep the stricter P95 check separately for operational eligibility.
        var scientific = SelectScientificWinner(measured, plan.Calibration.SelectionPolicy,
            plan.Calibration.Budget.MaximumBlockingFalseDegreeP99);
        var best = SelectConditionalWinner(measured, plan.Calibration.SelectionPolicy,
            plan.Calibration.Budget.MaximumBlockingFalseDegreeP99);
        // A successful scientific selection must itself emit the exact FINAL
        // operational JSON bytes consumed by the versioned V9 production-side
        // SQL loader, regardless of whether the optional TEST is requested.
        // The exported payload has *identical bytes* to payload_fitted in SQL;
        // no model is certified, activated or promoted by this export.
        V9ValidatedOperationalModelFile.Receipt? operationalFile = null;
        if (scientific is not null)
        {
            var chosen = snapshots.Single(x => x.ContentSha256 == scientific.SnapshotSha256);
            operationalFile = await V9ValidatedOperationalModelFile.ExportAsync(
                chosen, Path.Combine(output, "validated-operational-model"));
            if (operationalFile.WinnerSnapshotSha256 != scientific.SnapshotSha256)
                throw new InvalidDataException("VALIDATION: arquivo operacional não corresponde ao único vencedor científico.");
            await WriteNewAsync(Path.Combine(output, "selected-operational-model-reference.json"), new
            {
                schema = operationalFile.Schema,
                fileName = operationalFile.FileName,
                winnerSnapshotSha256 = operationalFile.WinnerSnapshotSha256,
                operationalModelContract = operationalFile.OperationalModelContract,
                snapshotSchemaVersion = operationalFile.SnapshotSchemaVersion,
                operationalModelId = operationalFile.OperationalModelId,
                fitFingerprintSha256 = operationalFile.FitFingerprintSha256,
                projectionFingerprintSha256 = operationalFile.ProjectionFingerprintSha256,
                jointCells = operationalFile.JointCellCount,
                tfSlots = operationalFile.TfSlotCount,
                blockingPasses = operationalFile.BlockingPassCount,
                byteExactRoundTrip = operationalFile.ByteExactRoundTrip,
                refitted = false, testUsed = false, certified = false,
                activated = false, published = false
            });
        }
        var finalValidationResult = new { selectionEnvelopeSha256 = selectionSha,
            fresh, comparedConfigurations = ids.Length, measured,
            scientificProposedSnapshotSha256 = scientific?.SnapshotSha256,
            bestConditionalSnapshotSha256 = best?.SnapshotSha256,
            // Selection by independent VALIDATION is distinct from certification and activation.
            selectedValidationSnapshotSha256 = scientific?.SnapshotSha256,
            selectedValidationOperationalModelFileName = operationalFile?.FileName,
            selectedValidationOperationalModelContract = operationalFile?.OperationalModelContract,
            selectedValidationOperationalModelId = operationalFile?.OperationalModelId,
            selectedApprovedSnapshotSha256 = (string?)null,
            status = scientific is null ? "NO_FINALIST_PASSES_DECLARED_SCIENTIFIC_GATES" : "VALIDATION_SCIENTIFIC_WINNER_SELECTED_NOT_CERTIFIED",
            identityClusterIidIndependentlyValidated = false, testUsed = false, refitted = false, certified = false, activated = false };
        // Preserve the existing fixed filename for legacy consumers. Publish a
        // content-addressed copy of the exact same final bytes for governance.
        var finalValidationBytes = JsonSerializer.SerializeToUtf8Bytes(finalValidationResult, Json);
        var finalValidationName = V9CalibrationJsonArtifactName.Create(finalValidationBytes, DateTimeOffset.UtcNow);
        await using (var legacyFile = new FileStream(Path.Combine(output, "finalist-validation.json"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await legacyFile.WriteAsync(finalValidationBytes);
        await using (var namedFile = new FileStream(Path.Combine(output, finalValidationName),
            FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await namedFile.WriteAsync(finalValidationBytes);
        var storedBytes = await V9CalibrationJsonArtifactName.ReadReceiptVerifiedAsync(
            output, finalValidationName, Sha(finalValidationBytes));
        if (!V9CalibrationJsonArtifactName.Verify(finalValidationName, storedBytes)
            || !storedBytes.AsSpan().SequenceEqual(finalValidationBytes))
            throw new InvalidDataException("Resultado V9: divergência entre SHA no nome e bytes persistidos.");
        // Provenance receipt, deliberately not a model-binding or promotion authorization.
        // The original content hash is independently verifiable against the named file.
        await WriteNewAsync(Path.Combine(output, "finalist-validation-artifact-reference.json"), new
        {
            schema = "JORNADA_V9_FINALIST_VALIDATION_ARTIFACT_REFERENCE_V1",
            artifactFileName = finalValidationName,
            artifactSha256 = Sha(finalValidationBytes),
            sourceSelectionEnvelopeSha256 = selectionSha,
            legacyFileName = "finalist-validation.json",
            modelId = (string?)null,
            status = "EXTERNAL_VALIDATION_NOT_CERTIFIED_NOT_ACTIVATED"
        });
        process.Refresh();
        var validationExportFinishedMs = elapsed.ElapsedMilliseconds;
        await WriteNewAsync(Path.Combine(output, "runtime-diagnostics.json"), new
        {
            schema = "V9_LOCAL_RUNTIME_DIAGNOSTICS_V1",
            operation = "EXTERNAL_VALIDATION",
            totalMilliseconds = validationExportFinishedMs,
            phasesMilliseconds = new
            {
                physicalEvaluation = physicalEvaluationFinishedMs,
                evidenceExport = validationExportFinishedMs - physicalEvaluationFinishedMs
            },
            visibleLogicalProcessors = Environment.ProcessorCount,
            configuredParallelism = plan.Calibration.MaximumParallelRuleSets,
            effectiveParallelism = V9ParallelWork.ResolveMaximumParallelism(plan.Calibration.MaximumParallelRuleSets),
            peakWorkingSetBytes = process.PeakWorkingSet64,
            operatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            dotNetVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            machineOrRunner = Environment.MachineName,
            temporalComparability = "TEMPO INFORMATIVO: sem comparabilidade entre máquinas, SOs ou runners e sujeito à carga concorrente no mesmo host.",
            distribution = "LOCAL: uma máquina; tarefas C#/.NET escalonadas pelo SO.",
            scientificSelectionUsesElapsedTime = false,
            testUsed = false, activated = false
        });
        Console.WriteLine(JsonSerializer.Serialize(new { finalists = ids.Length, scientificProposal = scientific?.SnapshotSha256,
            conditionalWinner = best?.SnapshotSha256,
            refitted = false, certified = false, testUsed = false, activated = false }));
    }

    /// <summary>
    /// Deterministic SCIENTIFIC proposal independent of execution time and
    /// operational P95. The return value is not an activation authorization.
    /// </summary>
    public static V9FrozenHoldoutEvaluation? SelectScientificWinner(
        IReadOnlyList<V9FrozenHoldoutEvaluation> measured,
        V9BootstrapPareto2D.SelectionPolicy policy, double maximumFalseDegreeP99) =>
        SelectWinner(measured, policy, maximumFalseDegreeP99, requireOperationalLatency: false);

    /// <summary>
    /// A separate conditional OPERABILITY gate; P95 can prohibit deployment,
    /// but must never influence SelectScientificWinner.
    /// </summary>
    public static V9FrozenHoldoutEvaluation? SelectConditionalWinner(
        IReadOnlyList<V9FrozenHoldoutEvaluation> measured,
        V9BootstrapPareto2D.SelectionPolicy policy, double maximumFalseDegreeP99) =>
        SelectWinner(measured, policy, maximumFalseDegreeP99, requireOperationalLatency: true);

    private static V9FrozenHoldoutEvaluation? SelectWinner(
        IReadOnlyList<V9FrozenHoldoutEvaluation> measured,
        V9BootstrapPareto2D.SelectionPolicy policy, double maximumFalseDegreeP99,
        bool requireOperationalLatency)
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
            && (!requireOperationalLatency || (x.P95Milliseconds.HasValue && x.P95Milliseconds <= policy.MaximumP95Milliseconds))
            && x.FalseDegreeP99 <= maximumFalseDegreeP99)
            .OrderBy(x => x.Metric.FalseAcceptedSourceObservations)
            .ThenByDescending(x => x.Metric.GlobalSourceRecall)
            .ThenBy(x => x.Metric.ConditionalClusterCpFdrUpper)
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
