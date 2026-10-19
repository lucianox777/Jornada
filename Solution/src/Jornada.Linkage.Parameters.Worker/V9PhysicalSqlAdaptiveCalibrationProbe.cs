using System.Diagnostics;
using System.Globalization;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record V9PhysicalCalibrationObservation(
    int ObservationId,
    string BasePersonId,
    string Partition,
    string Gestor,
    string? PersonName,
    string? MotherName,
    DateOnly? BirthDate);


public sealed record V9PhysicalFinalResolveMetric(
    string OptionId, decimal Threshold, decimal TfWeight,
    long EligibleDirectedTrueLinksBeforeBlocking,
    long RecoveredDirectedTrueLinksBySql,
    long TrueAcceptedDirectedLinks,
    long FalseAcceptedDirectedLinks,
    long Conflicts, long Unresolved,
    double BlockingRecall, double GlobalRecall, double? FdrAfterResolve,
    double? HoeffdingUpperBySourceBaseIdentity,
    int PreBlockingBaseIdentities,
    int PredeclaredMaximumAcceptedPerBaseIdentity, double Alpha,
    bool FinalRankResolveEvaluated, bool OperationallyCertified);

/// <summary>
/// One Rank/Resolve decision per SOURCE observation. Source eligibility is
/// determined BEFORE SQL blocking (at least one true cross-manager partner).
/// Oracle grouping uses SYNTHETIC truth labels solely to model ideal Gold
/// consolidation and cannot certify actual entity clustering.
/// </summary>
public sealed record V9PhysicalSourceResolveMetric(
    string OptionId, decimal Threshold, decimal TfWeight, decimal ConflictMargin,
    string CandidateGrouping, int AllValidationObservations,
    long EligibleSourceObservationsBeforeBlocking,
    long EligibleSourceObservationsWithTrueSqlCandidate,
    long TrueAcceptedSourceObservations, long FalseAcceptedSourceObservations,
    long Conflicts, long Unresolved,
    double BlockingSourceRecall, double GlobalSourceRecall,
    double? FdrAfterResolve, double? HoeffdingUpperBySourceBaseIdentity,
    int PreBlockingBaseIdentities, IReadOnlyDictionary<string, long> ReasonsByResolveStatus,
    bool SyntheticGoldOracleGrouping, bool OperationallyCertified,
    int ImpureBlindGroups,
    double? ConditionalClusterCpFdrUpper = null,
    double? ClopperPearsonUpperAnyFpByIdentity = null,
    int BonferroniComparedConfigurations = 0,
    bool ClusterIidIndependentlyValidated = false);

public sealed record V9PhysicalCalibrationRunResult(
    string Version,
    string BlockingFingerprintSha256,
    long PreflightPairIncidences,
    long PhysicalDistinctCandidatePairs,
    long TrainCandidatePairs,
    long ValidationCandidatePairs,
    int TrainDistinctBasePeople,
    int ValidationDistinctBasePeople,
    AdaptiveCalibrationStudyReport CalibrationReport,
    long TrainEligibleTruePairsBeforeBlocking = 0,
    long ValidationEligibleTruePairsBeforeBlocking = 0,
    long TrainTruePairsRecoveredBySql = 0,
    long ValidationTruePairsRecoveredBySql = 0,
    V9PhysicalSqlMeasuredTimings? PhysicalSqlTimings = null,
    IReadOnlyList<V9PhysicalSqlOptionGlobalCoverage>? ValidationGlobalCoverage = null,
    V9PhysicalSqlIdentityCohortCoverage? TrainIdentityCohortCoverage = null,
    V9PhysicalSqlIdentityCohortCoverage? ValidationIdentityCohortCoverage = null,
    V9PhysicalSqlCostQuantiles.Summary? RepeatedSqlMaterializationQuantiles = null,
    IReadOnlyList<V9PhysicalFinalResolveMetric>? FinalResolveValidation = null,
    IReadOnlyList<V9PhysicalSourceResolveMetric>? SourceResolveValidation = null,
    V9PostResolveSourceSelectionReport? PostResolveSelection = null);

/// <summary>
/// Cohort is defined BEFORE SQL blocking and includes every base identity,
/// including singleton identities and those with no matching signatures.
/// It is not an independent sampling/certification guarantee.
/// </summary>
public sealed record V9PhysicalSqlIdentityCohortCoverage(
    string Partition,
    int BaseIdentitiesBeforeBlocking,
    int BaseIdentitiesRepresentedBySql,
    int BaseIdentitiesWithoutSqlCandidates,
    double BaseIdentityCandidateCoverage);

/// <summary>
/// Every numerator here is a thresholded FS candidate-pair decision, NOT a
/// conflict-resolved final identity link. In particular, GlobalAcceptedRecall
/// uses ALL cross-manager true pairs before blocking, rather than M_sql.
/// This diagnostic has no TEST, Rank/Resolve, TF or operational certification.
/// </summary>
public sealed record V9PhysicalSqlOptionGlobalCoverage(
    string ModelFamily,
    string OptionId,
    long EligibleTruePairsBeforeBlocking,
    long TruePairsRecoveredBySql,
    long FalsePairsRecoveredBySql,
    long TruePairsAcceptedByThreshold,
    long FalsePairsAcceptedByThreshold,
    double BlockingTruePairRecall,
    double ConditionalAcceptedPairRecall,
    double GlobalAcceptedPairRecall,
    double? ThresholdAcceptedPairFdr,
    bool MeetsGlobalRecallBudget,
    bool FinalRankResolveEvaluated);

/// <summary>
/// Observed wall-clock duration of each SQL phase for one physical probe run.
/// The values are not repeated-run latency percentiles and must not be used
/// as a P95/P99 SLA or as evidence of production readiness.
/// </summary>
public sealed record V9PhysicalSqlMeasuredTimings(
    double CreateTablesMilliseconds,
    double BulkObservationsMilliseconds,
    double BulkSignaturesMilliseconds,
    double MaterializePairsMilliseconds,
    double ReadPairsMilliseconds);

/// <summary>
/// Calibration-only physical SQL bridge. It accepts TRAIN/VALIDATION observations only,
/// projects the frozen blocking passes with the canonical C# projector, materializes the
/// complete deduplicated candidate union in JornadaHarness SQL Server (no TOP N), then
/// feeds exactly those physical pairs to the V9 Fellegi-Sunter adaptive adapter.
/// TEST input is rejected before any SQL command.
/// </summary>
public static class V9PhysicalSqlAdaptiveCalibrationProbe
{
    public const string Version = "V9_PHYSICAL_SQL_ADAPTIVE_FS_TRAIN_VALIDATION_V2";
    public const int HardPairIncidenceCap = 1_000_000;

    private sealed record SignatureRow(
        int ObservationId, string Partition, string PassId, string Signature);

    public static async Task<V9PhysicalCalibrationRunResult> RunAsync(
        SqlConnection connection,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> observations,
        IReadOnlyCollection<LinkageBlockingPass> passes,
        IReadOnlyCollection<V9FsAdaptiveOptionConfiguration> options,
        AdaptiveCalibrationPreviewBudget budget,
        string studyFingerprintSha256,
        CancellationToken cancellationToken,
        FrozenIbgeNominalTfV9? frozenTfReference = null,
        int repeatedSqlMaterializationRuns = 0,
        bool evaluateFinalResolve = false,
        decimal finalConflictMargin = .5m,
        int predeclaredMaximumAcceptedPerBaseIdentity = 16,
        IReadOnlyCollection<decimal>? finalConflictMargins = null,
        bool includeFsBlindGrouping = false,
        decimal minimumFsGroupingPosterior = .99m)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(passes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(budget);
        RequireSha(studyFingerprintSha256, nameof(studyFingerprintSha256));
        // Default is zero: no implicit extra SQL work in existing probes.
        // When explicitly requested, each run repeats the exact SQL UNION
        // materialization and rechecks the distinct-pair cardinality.
        if (repeatedSqlMaterializationRuns != 0
            && repeatedSqlMaterializationRuns is < 20 or > 100)
            throw new ArgumentOutOfRangeException(nameof(repeatedSqlMaterializationRuns),
                "Repetição de SQL exige 20..100 execuções físicas ou zero para desabilitar.");

        if (observations.Count < 4)
            throw new ArgumentException("Frame físico exige observações TRAIN/VALIDATION suficientes.");
        if (observations.Any(x => x is null
                || x.ObservationId <= 0
                || string.IsNullOrWhiteSpace(x.BasePersonId)
                || string.IsNullOrWhiteSpace(x.Gestor)
                || x.Partition is not ("TRAIN" or "VALIDATION")))
            throw new InvalidDataException(
                "Probe físico aceita exclusivamente TRAIN/VALIDATION; TEST ou linha inválida é proibido.");
        if (observations.Select(x => x.ObservationId).Distinct().Count() != observations.Count)
            throw new InvalidDataException("ObservationId duplicado no frame físico.");
        if (!observations.Any(x => x.Partition == "TRAIN")
            || !observations.Any(x => x.Partition == "VALIDATION"))
            throw new InvalidDataException("TRAIN e VALIDATION são obrigatórios.");

        var trainBases = observations.Where(x => x.Partition == "TRAIN")
            .Select(x => x.BasePersonId).ToHashSet(StringComparer.Ordinal);
        var validationBases = observations.Where(x => x.Partition == "VALIDATION")
            .Select(x => x.BasePersonId).ToHashSet(StringComparer.Ordinal);
        if (trainBases.Overlaps(validationBases))
            throw new InvalidDataException("Vazamento de identidade entre TRAIN e VALIDATION.");

        // Count cross-manager true observation-pairs BEFORE any blocking pass.
        // This denominator includes M pairs missed by every SQL rule. Comparing
        // against only M_sql would silently inflate blocking recall.
        var trainEligibleM = CountEligibleTruePairsAcrossManagers(observations, "TRAIN");
        var validationEligibleM = CountEligibleTruePairsAcrossManagers(observations, "VALIDATION");

        var canonicalPasses = passes
            .Select(p => LinkageBlockingPass.Create(p.PassId, p.Fields))
            .OrderBy(p => p.PassId, StringComparer.Ordinal)
            .ToArray();
        if (canonicalPasses.Length is < 1 or > 64)
            throw new ArgumentException("Ruleset físico precisa de 1..64 passes.");
        var allowed = BlockingCandidateFeatureCatalog.RequiredCalibratorCandidates
            .ToHashSet(StringComparer.Ordinal);
        if (canonicalPasses.SelectMany(x => x.Fields).Any(x => !allowed.Contains(x)))
            throw new InvalidDataException("Ruleset contém feature fora do catálogo do Calibrador.");
        var blockingFingerprint = BlockingFingerprint(canonicalPasses);
        // A positive TF option can only run against the SAME pinned physical
        // SQL blocking frame and preloaded immutable reference. Do not query
        // whichever reference happens to be active at evaluation time.
        if (options.Any(x => x is not null && x.TfWeight > 0m)
            && (frozenTfReference is null
                || frozenTfReference.BlockingFrameFingerprint != blockingFingerprint))
            throw new InvalidDataException(
                "TF positiva exige referência IBGE publicada/fixada no mesmo frame SQL.");
        var signatures = BuildSignatures(observations, canonicalPasses);
        var preflight = CountPairIncidences(signatures, observations);
        if (preflight > HardPairIncidenceCap)
            throw new InvalidDataException(
                $"Censo físico recusado antes do join: {preflight} incidências excedem {HardPairIncidenceCap}.");

        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Conexão SQL precisa estar aberta.");

        await RequireHarnessAsync(connection, cancellationToken);

        // Measure actual physical SQL operations; do not present five operation
        // samples as repeated-run P95/P99 or as production certification.
        var timer = Stopwatch.StartNew();
        await CreateTablesAsync(connection, cancellationToken);
        var createMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        await BulkObservationsAsync(connection, observations, cancellationToken);
        var observationsMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        await BulkSignaturesAsync(connection, signatures, cancellationToken);
        var signaturesMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        var physicalPairs = await MaterializePairsAsync(connection, cancellationToken);
        var materializeMs = timer.Elapsed.TotalMilliseconds;
        if (physicalPairs > HardPairIncidenceCap)
            throw new InvalidDataException("União SQL distinta excedeu o hard cap após materialização.");

        timer.Restart();
        var rows = await ReadCandidatePairsAsync(connection, cancellationToken);
        var readMs = timer.Elapsed.TotalMilliseconds;
        var measuredSql = new V9PhysicalSqlMeasuredTimings(
            createMs, observationsMs, signaturesMs, materializeMs, readMs);
        if (rows.Count != physicalPairs)
            throw new InvalidDataException("Contagem da união SQL diverge da leitura do frame.");

        // Repeat only AFTER verifying the first materialization and BEFORE
        // calibrating/reading TEST. The existing RepeatPhysicalMaterialization
        // path uses the exact same SQL on the same ephemeral JornadaHarness
        // frame and checks the number of distinct pairs after each run.
        // This is a diagnostic per-operation P95/P99, NOT Rank/Resolve latency.
        V9PhysicalSqlCostQuantiles.Summary? repeatedQuantiles = null;
        if (repeatedSqlMaterializationRuns != 0)
        {
            var samples = await RepeatPhysicalMaterializationAsync(
                connection, repeatedSqlMaterializationRuns, physicalPairs,
                cancellationToken);
            repeatedQuantiles = V9PhysicalSqlCostQuantiles.FromMeasuredMilliseconds(samples);
        }

        var trainRows = rows.Where(x => x.Partition == "TRAIN").ToArray();
        var validationRows = rows.Where(x => x.Partition == "VALIDATION").ToArray();
        if (trainRows.Length == 0 || validationRows.Length == 0
            || trainRows.All(x => x.IsTruePair) || trainRows.All(x => !x.IsTruePair)
            || validationRows.All(x => x.IsTruePair) || validationRows.All(x => !x.IsTruePair))
            throw new InvalidDataException("TRAIN/VALIDATION físicos precisam conter M e U.");

        var trainRecoveredM = trainRows.LongCount(x => x.IsTruePair);
        var validationRecoveredM = validationRows.LongCount(x => x.IsTruePair);
        if (trainRecoveredM > trainEligibleM || validationRecoveredM > validationEligibleM)
            throw new InvalidDataException(
                "M recuperado no SQL excede universo elegível pré-blocking: denominador inconsistente.");

        var trainCorpusSha = CorpusFingerprint("TRAIN", observations, blockingFingerprint);
        var validationCorpusSha = CorpusFingerprint("VALIDATION", observations, blockingFingerprint);
        var trainIdentitySha = IdentityFingerprint("TRAIN", trainBases);
        var validationIdentitySha = IdentityFingerprint("VALIDATION", validationBases);

        var frame = new V9FsAdaptiveCandidateFrame(
            trainCorpusSha,
            validationCorpusSha,
            trainIdentitySha,
            validationIdentitySha,
            rows,
            trainBases.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            validationBases.OrderBy(x => x, StringComparer.Ordinal).ToArray());

        var optionList = options.ToArray();
        var adapter = new V9FellegiSunterAdaptiveCalibrationAdapter(
            frame, optionList, frozenTfReference);
        var modelOptions = optionList.Select(x => new AdaptiveCalibrationModelOption(
            V9FellegiSunterAdaptiveCalibrationAdapter.Family,
            V9FellegiSunterAdaptiveCalibrationAdapter.Version,
            x.OptionId,
            x.FingerprintSha256)).ToArray();

        var trainPairBases = PairBaseSet(trainRows);
        var validationPairBases = PairBaseSet(validationRows);
        var trainIdentityCoverage = SummarizeIdentityCohort("TRAIN", trainBases, trainRows);
        var validationIdentityCoverage =
            SummarizeIdentityCohort("VALIDATION", validationBases, validationRows);
        if (trainPairBases.Count != trainIdentityCoverage.BaseIdentitiesRepresentedBySql
            || validationPairBases.Count != validationIdentityCoverage.BaseIdentitiesRepresentedBySql)
            throw new InvalidDataException("Cobertura de identidades SQL não reconcilia.");
        if (trainPairBases.Overlaps(validationPairBases))
            throw new InvalidDataException("União física contaminou identidades entre splits.");

        var request = new AdaptiveCalibrationStudyRequest(
            studyFingerprintSha256,
            new AdaptiveCalibrationSlice(
                "TRAIN", trainCorpusSha, trainIdentitySha,
                trainBases.Count, true, false),
            new AdaptiveCalibrationSlice(
                "VALIDATION", validationCorpusSha, validationIdentitySha,
                validationBases.Count, true, false),
            modelOptions,
            ManuallyRequestedCalibration: true,
            budget);

        var report = AdaptiveCalibrationModelSearch.Evaluate(request, [adapter]);
        var globalCoverage = CalculateValidationGlobalCoverage(
            validationEligibleM,
            validationRecoveredM,
            validationRows.LongCount(x => !x.IsTruePair),
            report.Options,
            budget.MinimumGlobalAcceptedRecall);

        // The family-agnostic report is conditional on SQL-recovered M.
        // On physical SQL only, never suggest a configuration whose global
        // TP/eligible-M recall fails the same explicit budget.
        var physicalReport = EnforcePhysicalGlobalRecallForSuggestion(report, globalCoverage);
        IReadOnlyList<V9PhysicalFinalResolveMetric>? finalResolve = null;
        IReadOnlyList<V9PhysicalSourceResolveMetric>? sourceResolve = null;
        V9PostResolveSourceSelectionReport? postResolveSelection = null;
        if (evaluateFinalResolve)
        {
            var physicalEdges = await ReadValidationPairsAsync(connection, cancellationToken);
            if (physicalEdges.Count != validationRows.Length)
                throw new InvalidDataException("Arestas SQL de VALIDATION divergem do censo original.");
            finalResolve = adapter.EvaluatePostResolveValidation(
                observations, physicalEdges, validationEligibleM, validationRecoveredM,
                finalConflictMargin, predeclaredMaximumAcceptedPerBaseIdentity);
            sourceResolve = adapter.EvaluateSourceUnitResolveValidation(
                observations, physicalEdges, finalConflictMargins ?? [finalConflictMargin],
                predeclaredMaximumAcceptedPerBaseIdentity,
                includeFsBlindGrouping, minimumFsGroupingPosterior);
            postResolveSelection = V9PostResolveSourceDiagnosticSelector.Evaluate(
                sourceResolve, budget, minimumConflictMargin: .5m);
            // Pairwise FS threshold ranking is NEVER a valid suggestion
            // once final source-identity Rank->Resolve is requested.
            physicalReport = physicalReport with
            {
                Status = "PAIRWISE_SUGGESTION_SUPPRESSED_USE_POST_RESOLVE_GATES",
                SuggestedModelFamily = null,
                SuggestedOptionId = null
            };
        }

        return new V9PhysicalCalibrationRunResult(
            Version,
            blockingFingerprint,
            preflight,
            physicalPairs,
            trainRows.LongLength,
            validationRows.LongLength,
            trainPairBases.Count,
            validationPairBases.Count,
            physicalReport,
            trainEligibleM,
            validationEligibleM,
            trainRecoveredM,
            validationRecoveredM,
            measuredSql,
            globalCoverage,
            trainIdentityCoverage,
            validationIdentityCoverage,
            repeatedQuantiles,
            finalResolve,
            sourceResolve,
            postResolveSelection);
    }

    /// <summary>
    /// Calculate physically grounded global recall separately from the
    /// coordinator's conditional SQL-candidate recall. A passing screen
    /// here never authorizes promotion or substitutes for Rank/Resolve.
    /// </summary>
    public static IReadOnlyList<V9PhysicalSqlOptionGlobalCoverage> CalculateValidationGlobalCoverage(
        long eligibleTruePairs,
        long recoveredTruePairs,
        long recoveredFalsePairs,
        IReadOnlyList<AdaptiveCalibrationOptionResult> options,
        double minimumGlobalRecall)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (eligibleTruePairs <= 0 || recoveredTruePairs < 0
            || recoveredTruePairs > eligibleTruePairs || recoveredFalsePairs < 0
            || !double.IsFinite(minimumGlobalRecall) || minimumGlobalRecall is < 0 or > 1)
            throw new InvalidDataException("Universo físico ou orçamento global inválido.");
        if (options.Count == 0 || options.Any(x => x is null))
            throw new InvalidDataException("Opções FS ausentes do censo físico.");
        if (options.Select(x => (x.ModelFamily, x.OptionId)).Distinct().Count() != options.Count)
            throw new InvalidDataException("Opções FS duplicadas no censo físico.");

        var measured = new List<V9PhysicalSqlOptionGlobalCoverage>(options.Count);
        foreach (var option in options)
        {
            var validation = option.Validation;
            if (validation is null || validation.TruePairsWithoutCpf != recoveredTruePairs
                || validation.BlockingFalseCandidatePairs != recoveredFalsePairs
                || validation.TrueAcceptedLinks < 0
                || validation.TrueAcceptedLinks > recoveredTruePairs
                || validation.FalseAcceptedLinks < 0
                || validation.FalseAcceptedLinks > recoveredFalsePairs
                || validation.Abstentions < 0
                || validation.Abstentions > recoveredTruePairs - validation.TrueAcceptedLinks)
                throw new InvalidDataException(
                    "TP/FP/abstenção FS não reconciliam com o frame SQL físico.");

            var accepted = checked(validation.TrueAcceptedLinks + validation.FalseAcceptedLinks);
            var globalRecall = (double)validation.TrueAcceptedLinks / eligibleTruePairs;
            measured.Add(new V9PhysicalSqlOptionGlobalCoverage(
                option.ModelFamily, option.OptionId,
                eligibleTruePairs, recoveredTruePairs, recoveredFalsePairs,
                validation.TrueAcceptedLinks, validation.FalseAcceptedLinks,
                (double)recoveredTruePairs / eligibleTruePairs,
                recoveredTruePairs == 0 ? 0d
                    : (double)validation.TrueAcceptedLinks / recoveredTruePairs,
                globalRecall,
                accepted == 0 ? null : (double)validation.FalseAcceptedLinks / accepted,
                MeetsGlobalRecallBudget: globalRecall >= minimumGlobalRecall,
                FinalRankResolveEvaluated: false));
        }
        return Array.AsReadOnly(measured.ToArray());
    }

    /// <summary>
    /// Necessary physical SQL safeguard: a conditional recommendation from the
    /// model search must not survive a failing global eligible-M denominator.
    /// This remains diagnostic: the rank/resolve final links are not evaluated.
    /// </summary>
    public static AdaptiveCalibrationStudyReport EnforcePhysicalGlobalRecallForSuggestion(
        AdaptiveCalibrationStudyReport report,
        IReadOnlyList<V9PhysicalSqlOptionGlobalCoverage> coverage)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(coverage);
        if (report.Options is null || coverage.Count != report.Options.Count)
            throw new InvalidDataException("Relatório/medidas físicas não possuem a mesma cardinalidade.");

        var keys = new HashSet<(string, string)>();
        foreach (var x in coverage)
        {
            if (x is null || string.IsNullOrWhiteSpace(x.ModelFamily)
                || string.IsNullOrWhiteSpace(x.OptionId)
                || !keys.Add((x.ModelFamily, x.OptionId)))
                throw new InvalidDataException("Opção física ausente ou duplicada.");
        }
        if (report.Options.Any(x => x is null
            || !keys.Contains((x.ModelFamily, x.OptionId))))
            throw new InvalidDataException("Opções do relatório diferem do SQL físico.");

        if (report.SuggestedOptionId is null && report.SuggestedModelFamily is null)
            return report;
        if (string.IsNullOrWhiteSpace(report.SuggestedOptionId)
            || string.IsNullOrWhiteSpace(report.SuggestedModelFamily))
            throw new InvalidDataException("Sugestão parcial no coordenador.");
        var matching = coverage.SingleOrDefault(x =>
            x.ModelFamily == report.SuggestedModelFamily
            && x.OptionId == report.SuggestedOptionId);
        if (matching is null)
            throw new InvalidDataException("Sugestão não pertence às opções do SQL físico.");

        return matching.MeetsGlobalRecallBudget ? report : report with
        {
            Status = "NO_MODEL_MEETS_PHYSICAL_GLOBAL_RECALL_BUDGET",
            SuggestedModelFamily = null,
            SuggestedOptionId = null
        };
    }

    /// <summary>
    /// Keep the entire input cohort in the diagnostic denominator: a group
    /// absent from SQL is not an independently measured negative or proof
    /// of no risk; it must not be silently dropped from subsequent UCB work.
    /// </summary>
    public static V9PhysicalSqlIdentityCohortCoverage SummarizeIdentityCohort(
        string partition,
        IReadOnlyCollection<string> inputBaseIdentities,
        IReadOnlyCollection<V9FsAdaptiveCandidatePair> physicalPairs)
    {
        ArgumentNullException.ThrowIfNull(inputBaseIdentities);
        ArgumentNullException.ThrowIfNull(physicalPairs);
        if (partition is not ("TRAIN" or "VALIDATION"))
            throw new ArgumentException("Partição inválida no censo de identidades.", nameof(partition));
        var input = inputBaseIdentities.ToHashSet(StringComparer.Ordinal);
        if (input.Count == 0 || input.Count != inputBaseIdentities.Count
            || input.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Universo pré-blocking ausente, duplicado ou inválido.");
        var represented = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in physicalPairs)
        {
            if (row is null || row.Partition != partition
                || !input.Contains(row.LeftIdentityGroupId)
                || !input.Contains(row.RightIdentityGroupId))
                throw new InvalidDataException("Candidato SQL fora da partição/universo de identidade-base.");
            represented.Add(row.LeftIdentityGroupId);
            represented.Add(row.RightIdentityGroupId);
        }
        return new V9PhysicalSqlIdentityCohortCoverage(
            partition, input.Count, represented.Count, input.Count - represented.Count,
            (double)represented.Count / input.Count);
    }

    /// <summary>
    /// Number of true observation pairs across distinct gestores in the specified
    /// split, before blocking. One pair is counted once (unoriented), regardless
    /// of how many SQL passes could later retrieve it. No CPF/TF is inferred.
    /// </summary>
    public static long CountEligibleTruePairsAcrossManagers(
        IEnumerable<V9PhysicalCalibrationObservation> observations, string partition)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (partition is not ("TRAIN" or "VALIDATION"))
            throw new ArgumentException("Partição inválida no denominador M.", nameof(partition));

        long eligible = 0;
        foreach (var baseGroup in observations.Where(x => x.Partition == partition)
                     .GroupBy(x => x.BasePersonId, StringComparer.Ordinal))
        {
            long priorGestores = 0;
            foreach (var count in baseGroup.GroupBy(x => x.Gestor, StringComparer.Ordinal)
                         .Select(x => x.LongCount()))
            {
                // Each observation from a new gestor can form a pair with
                // every previously seen gestor; same-gestor pairs are ineligible.
                eligible = checked(eligible + checked(priorGestores * count));
                priorGestores = checked(priorGestores + count);
            }
        }
        return eligible;
    }

    private static List<SignatureRow> BuildSignatures(
        IReadOnlyCollection<V9PhysicalCalibrationObservation> observations,
        IReadOnlyCollection<LinkageBlockingPass> passes)
    {
        var result = new List<SignatureRow>();
        foreach (var observation in observations)
        {
            var byFeature = BlockingProjectionKeyProjector
                .Project(observation.PersonName, observation.MotherName, observation.BirthDate)
                .GroupBy(x => x.Feature, StringComparer.Ordinal)
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(v => v.Value).Distinct(StringComparer.Ordinal)
                        .OrderBy(v => v, StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal);

            foreach (var pass in passes)
            {
                if (pass.Fields.Any(f => !byFeature.TryGetValue(f, out var values) || values.Length == 0))
                    continue;

                foreach (var values in Cartesian(pass.Fields, byFeature))
                {
                    var canonical = pass.PassId + "\u001e" + string.Join("\u001f", values);
                    if (result.Count >= HardPairIncidenceCap)
                        throw new InvalidDataException(
                            "Projeção de assinaturas excedeu o limite físico antes da materialização SQL.");
                    result.Add(new SignatureRow(
                        observation.ObservationId,
                        observation.Partition,
                        pass.PassId,
                        Sha(canonical)));
                }
            }
        }

        return result
            .Distinct()
            .OrderBy(x => x.Partition, StringComparer.Ordinal)
            .ThenBy(x => x.PassId, StringComparer.Ordinal)
            .ThenBy(x => x.Signature, StringComparer.Ordinal)
            .ThenBy(x => x.ObservationId)
            .ToList();
    }

    private static IEnumerable<string[]> Cartesian(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, string[]> values)
    {
        var current = new string[fields.Count];

        IEnumerable<string[]> Walk(int index)
        {
            if (index == fields.Count)
            {
                yield return current.ToArray();
                yield break;
            }

            foreach (var value in values[fields[index]])
            {
                current[index] = value;
                foreach (var row in Walk(index + 1))
                    yield return row;
            }
        }

        return Walk(0);
    }

    /// <summary>
    /// Upper bound on physical SQL join incidences, excluding same-manager pairs
    /// exactly as the materialization query does. Duplicate pass hits still count
    /// separately here, so the cap remains conservative for the distinct union.
    /// </summary>
    public static long CountCrossManagerPairIncidences(
        IReadOnlyCollection<(int ObservationId, string Partition, string PassId, string Signature)> signatures,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(signatures);
        ArgumentNullException.ThrowIfNull(observations);
        var gestores = observations.ToDictionary(x => x.ObservationId, x => x.Gestor);
        decimal total = 0m;
        foreach (var group in signatures.GroupBy(x => (x.Partition, x.PassId, x.Signature)))
        {
            long prior = 0;
            foreach (var count in group.Select(x => x.ObservationId).Distinct()
                         .GroupBy(id => gestores[id], StringComparer.Ordinal)
                         .Select(g => g.LongCount()))
            {
                total += (decimal)prior * count;
                if (total > HardPairIncidenceCap)
                    return HardPairIncidenceCap + 1L;
                prior = checked(prior + count);
            }
        }
        return checked((long)total);
    }

    private static long CountPairIncidences(
        IReadOnlyCollection<SignatureRow> signatures,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> observations) =>
        CountCrossManagerPairIncidences(
            signatures.Select(x => (x.ObservationId, x.Partition, x.PassId, x.Signature)).ToArray(),
            observations);

    private static async Task RequireHarnessAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("SELECT DB_NAME();", connection);
        var database = (string?)await command.ExecuteScalarAsync(cancellationToken);
        if (!string.Equals(database, "JornadaHarness", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Probe de calibração V9 proibido fora do SQL isolado JornadaHarness.");
    }

    private static async Task CreateTablesAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            IF OBJECT_ID('tempdb..#v9_cal_pairs') IS NOT NULL DROP TABLE #v9_cal_pairs;
            IF OBJECT_ID('tempdb..#v9_cal_sig') IS NOT NULL DROP TABLE #v9_cal_sig;
            IF OBJECT_ID('tempdb..#v9_cal_obs') IS NOT NULL DROP TABLE #v9_cal_obs;

            CREATE TABLE #v9_cal_obs(
                obs_id int NOT NULL PRIMARY KEY,
                base_id nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL,
                part varchar(12) COLLATE Latin1_General_100_BIN2 NOT NULL,
                gestor nvarchar(80) COLLATE Latin1_General_100_BIN2 NOT NULL,
                nome nvarchar(500) NULL,
                mae nvarchar(500) NULL,
                nascimento date NULL
            );

            CREATE TABLE #v9_cal_sig(
                obs_id int NOT NULL,
                part varchar(12) COLLATE Latin1_General_100_BIN2 NOT NULL,
                pass_id nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
                assinatura char(64) COLLATE Latin1_General_100_BIN2 NOT NULL
            );
            CREATE INDEX IX_v9_cal_sig
              ON #v9_cal_sig(part,pass_id,assinatura,obs_id);
            """,
            connection)
        {
            CommandTimeout = 300
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task BulkObservationsAsync(
        SqlConnection connection,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> observations,
        CancellationToken cancellationToken)
    {
        var table = new DataTable();
        table.Columns.Add("obs_id", typeof(int));
        table.Columns.Add("base_id", typeof(string));
        table.Columns.Add("part", typeof(string));
        table.Columns.Add("gestor", typeof(string));
        table.Columns.Add("nome", typeof(string));
        table.Columns.Add("mae", typeof(string));
        table.Columns.Add("nascimento", typeof(DateTime));
        foreach (var x in observations)
            table.Rows.Add(
                x.ObservationId,
                x.BasePersonId,
                x.Partition,
                x.Gestor,
                (object?)x.PersonName ?? DBNull.Value,
                (object?)x.MotherName ?? DBNull.Value,
                x.BirthDate is { } d ? d.ToDateTime(TimeOnly.MinValue) : DBNull.Value);

        using var bulk = new SqlBulkCopy(connection)
        {
            DestinationTableName = "#v9_cal_obs",
            BatchSize = 15_000,
            BulkCopyTimeout = 300
        };
        await bulk.WriteToServerAsync(table, cancellationToken);
    }

    private static async Task BulkSignaturesAsync(
        SqlConnection connection,
        IReadOnlyCollection<SignatureRow> signatures,
        CancellationToken cancellationToken)
    {
        var table = new DataTable();
        table.Columns.Add("obs_id", typeof(int));
        table.Columns.Add("part", typeof(string));
        table.Columns.Add("pass_id", typeof(string));
        table.Columns.Add("assinatura", typeof(string));
        foreach (var x in signatures)
            table.Rows.Add(x.ObservationId, x.Partition, x.PassId, x.Signature);

        using var bulk = new SqlBulkCopy(connection)
        {
            DestinationTableName = "#v9_cal_sig",
            BatchSize = 25_000,
            BulkCopyTimeout = 300
        };
        await bulk.WriteToServerAsync(table, cancellationToken);
    }

    /// <summary>
    /// Repetições físicas do mesmo SQL de materialização em conexão isolada,
    /// sem recalibrar, sem alterar opções ou usar TEST. Este diagnóstico é
    /// somente TRAIN/VALIDATION e NÃO substitui a avaliação TEST independente.
    /// Cada amostra reexecuta o SELECT DISTINCT e a criação de índice reais.
    /// </summary>
    public static async Task<IReadOnlyList<double>> RepeatPhysicalMaterializationAsync(
        SqlConnection connection,
        int repetitions,
        long expectedDistinctPairs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (repetitions < 20 || repetitions > 100)
            throw new ArgumentOutOfRangeException(nameof(repetitions), "São exigidas 20..100 repetições físicas.");
        if (expectedDistinctPairs < 0 || expectedDistinctPairs > HardPairIncidenceCap)
            throw new ArgumentOutOfRangeException(nameof(expectedDistinctPairs));
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Conexão SQL deve estar aberta.");
        await RequireHarnessAsync(connection, cancellationToken);
        // Não aceitar uma conexão sem frame previamente materializado.
        await using (var check = new SqlCommand(
            "SELECT CASE WHEN OBJECT_ID('tempdb..#v9_cal_obs') IS NOT NULL AND OBJECT_ID('tempdb..#v9_cal_sig') IS NOT NULL THEN 1 ELSE 0 END",
            connection))
        {
            if (Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 1)
                throw new InvalidOperationException("Frame SQL físico ausente na sessão.");
        }
        var samples = new List<double>(repetitions);
        for (var i = 0; i < repetitions; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using (var drop = new SqlCommand(
                "IF OBJECT_ID('tempdb..#v9_cal_pairs') IS NOT NULL DROP TABLE #v9_cal_pairs;",
                connection))
                await drop.ExecuteNonQueryAsync(cancellationToken);
            var stopwatch = Stopwatch.StartNew();
            var actual = await MaterializePairsAsync(connection, cancellationToken);
            stopwatch.Stop();
            if (actual != expectedDistinctPairs)
                throw new InvalidDataException(
                    $"Materialização física {i + 1} divergiu: {actual} != {expectedDistinctPairs}.");
            samples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }
        return samples.AsReadOnly();
    }

    private static async Task<long> MaterializePairsAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT DISTINCT s1.obs_id AS a,s2.obs_id AS b,s1.part
            INTO #v9_cal_pairs
            FROM #v9_cal_sig s1
            JOIN #v9_cal_sig s2
              ON s2.part=s1.part
             AND s2.pass_id=s1.pass_id
             AND s2.assinatura=s1.assinatura
             AND s2.obs_id>s1.obs_id
            JOIN #v9_cal_obs a ON a.obs_id=s1.obs_id
            JOIN #v9_cal_obs b ON b.obs_id=s2.obs_id
            WHERE a.gestor<>b.gestor;

            CREATE UNIQUE CLUSTERED INDEX IX_v9_cal_pairs
              ON #v9_cal_pairs(part,a,b);

            SELECT COUNT_BIG(*) FROM #v9_cal_pairs;
            """,
            connection)
        {
            CommandTimeout = 300
        };
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<V9FsAdaptiveCandidatePair>> ReadCandidatePairsAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            WITH endpoints AS (
                SELECT a AS obs_id FROM #v9_cal_pairs
                UNION ALL
                SELECT b AS obs_id FROM #v9_cal_pairs
            ), degree AS (
                SELECT obs_id,COUNT_BIG(*) AS degree
                FROM endpoints GROUP BY obs_id
            )
            SELECT p.part,
                   a.base_id,b.base_id,
                   CASE WHEN a.base_id=b.base_id THEN 1 ELSE 0 END AS is_true,
                   a.nome,b.nome,a.mae,b.mae,a.nascimento,b.nascimento,
                   CASE WHEN da.degree>=db.degree THEN da.degree ELSE db.degree END AS degree
            FROM #v9_cal_pairs p
            JOIN #v9_cal_obs a ON a.obs_id=p.a
            JOIN #v9_cal_obs b ON b.obs_id=p.b
            JOIN degree da ON da.obs_id=p.a
            JOIN degree db ON db.obs_id=p.b
            ORDER BY p.part,p.a,p.b;
            """,
            connection)
        {
            CommandTimeout = 300
        };

        var rows = new List<V9FsAdaptiveCandidatePair>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var degree = reader.GetInt64(10);
            if (degree <= 0 || degree > int.MaxValue)
                throw new InvalidDataException("Fanout físico fora do domínio do adaptador.");

            rows.Add(new V9FsAdaptiveCandidatePair(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3) == 1,
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : DateOnly.FromDateTime(reader.GetDateTime(8)),
                reader.IsDBNull(9) ? null : DateOnly.FromDateTime(reader.GetDateTime(9)),
                (int)degree));
        }

        return rows;
    }


    private static async Task<IReadOnlyList<(int A, int B)>> ReadValidationPairsAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT a,b FROM #v9_cal_pairs WHERE part='VALIDATION' ORDER BY a,b;", connection)
        { CommandTimeout = 300 };
        var result = new List<(int A, int B)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add((reader.GetInt32(0), reader.GetInt32(1)));
        return result;
    }

    private static HashSet<string> PairBaseSet(
        IEnumerable<V9FsAdaptiveCandidatePair> rows)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            result.Add(row.LeftIdentityGroupId);
            result.Add(row.RightIdentityGroupId);
        }
        return result;
    }

    private static string BlockingFingerprint(
        IReadOnlyCollection<LinkageBlockingPass> passes)
    {
        var canonical = string.Join("\n", passes
            .OrderBy(x => x.PassId, StringComparer.Ordinal)
            .Select(x => x.PassId + "=" + string.Join(",", x.Fields)));
        return Sha(
            Version + "\n"
            + BlockingProjectionKeyProjector.MethodVersion + "\n"
            + canonical);
    }

    private static string CorpusFingerprint(
        string partition,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> observations,
        string blockingFingerprint)
    {
        var sb = new StringBuilder()
            .Append(Version).Append('\n')
            .Append(blockingFingerprint).Append('\n')
            .Append(partition).Append('\n');
        foreach (var row in observations.Where(x => x.Partition == partition)
                     .OrderBy(x => x.ObservationId))
            sb.Append(row.ObservationId).Append('|')
              .Append(row.BasePersonId).Append('|')
              .Append(row.Gestor).Append('|')
              .Append(row.PersonName).Append('|')
              .Append(row.MotherName).Append('|')
              .Append(row.BirthDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
        return Sha(sb.ToString());
    }

    private static string IdentityFingerprint(
        string partition, IReadOnlyCollection<string> identities) =>
        Sha(Version + "\nIDENTITIES\n" + partition + "\n"
            + string.Join("\n", identities.OrderBy(x => x, StringComparer.Ordinal)));

    private static string Sha(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static void RequireSha(string? sha, string label)
    {
        if (sha is null || sha.Length != 64 || sha.Any(x => !Uri.IsHexDigit(x)))
            throw new ArgumentException("SHA-256 obrigatório: " + label);
    }
}
