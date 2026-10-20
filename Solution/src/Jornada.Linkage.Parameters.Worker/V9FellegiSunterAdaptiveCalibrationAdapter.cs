using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Par bruto do frame candidato probabilístico já materializado pelo blocking.
/// Não contém CPF: este adaptador é somente para o ramo sem CPF.
/// TRAIN e VALIDATION devem ser disjuntos pelas identidades-base de ambos os lados.
/// </summary>
public sealed record V9FsAdaptiveCandidatePair(
    string Partition,
    string LeftIdentityGroupId,
    string RightIdentityGroupId,
    bool IsTruePair,
    string? LeftPersonName,
    string? RightPersonName,
    string? LeftMotherName,
    string? RightMotherName,
    DateOnly? LeftBirthDate,
    DateOnly? RightBirthDate,
    int CandidateDegreeForSource);

public sealed record V9FsAdaptiveCandidateFrame(
    string TrainCorpusFingerprintSha256,
    string ValidationCorpusFingerprintSha256,
    string TrainIdentityFingerprintSha256,
    string ValidationIdentityFingerprintSha256,
    IReadOnlyList<V9FsAdaptiveCandidatePair> Pairs,
    IReadOnlyCollection<string>? TrainPreBlockingBaseIdentityIds = null,
    IReadOnlyCollection<string>? ValidationPreBlockingBaseIdentityIds = null,
    IReadOnlyList<LinkageBlockingPass>? CandidateBlockingPasses = null,
    string? ProjectionSchemaVersion = null,
    string? ProjectionFingerprintSha256 = null,
    string? BlockingFrameFingerprintSha256 = null);

public sealed record V9FsAdaptiveOptionConfiguration(
    string OptionId,
    decimal SmoothingAlpha,
    decimal DecisionThreshold,
    decimal TfWeight = 0m,
    CompositeNominalTfSideV9 TfSide = CompositeNominalTfSideV9.Person,
    decimal TfMinimumFrequency = 0.000001m,
    long TfMinimumIndependentSupport = 1, V9JointSupportPolicy? JointSupportPolicy = null)
{
    public string FingerprintSha256 => V9FellegiSunterAdaptiveCalibrationAdapter
        .ConfigurationFingerprintSha256(this);
}

/// <summary>
/// Adaptador real do coordenador adaptativo para o scorer Fellegi-Sunter V9 C#.
/// Ele estima 81 células conjuntas, nascimento binário e prior SOMENTE em TRAIN,
/// congela o fit em memória pelo fingerprint e avalia o MESMO fit em VALIDATION.
/// Não lê TEST, não persiste/ativa modelo. TF opcional V2 admite peso em [0,1]
/// somente para prenomes ExactFirst com snapshot IBGE fixado e baseline U da
/// MESMA partição TRAIN SQL. Ausências com TF positiva falham fechado.
/// </summary>
public sealed class V9FellegiSunterAdaptiveCalibrationAdapter : IAdaptiveCalibrationFamilyAdapter
{
    public const string Family = "FELLEGI_SUNTER_V9";
    public const string Version = "V9_FS_ADAPTIVE_REAL_SCORER_TRAIN_VALIDATION_V2";

    private sealed record FrozenFit(
        string FingerprintSha256,
        string OptionId,
        decimal Threshold,
        IReadOnlyList<FellegiSunterJointNominalCellV9> Cells,
        IReadOnlyDictionary<string, decimal> Parameters,
        V9TrainSqlFirstNameUCalibration.Frozen? TrainUnitU, V9JointSupportProvenance? JointSupport);

    private readonly V9FsAdaptiveCandidateFrame frame;
    private readonly IReadOnlyDictionary<string, V9FsAdaptiveOptionConfiguration> options;
    private readonly FrozenIbgeNominalTfV9? frozenTfReference;
    private readonly Dictionary<string, FrozenFit> fits = new(StringComparer.Ordinal);

    public V9FellegiSunterAdaptiveCalibrationAdapter(
        V9FsAdaptiveCandidateFrame frame,
        IEnumerable<V9FsAdaptiveOptionConfiguration> options,
        FrozenIbgeNominalTfV9? frozenTfReference = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(options);
        RequireSha(frame.TrainCorpusFingerprintSha256, "TRAIN corpus");
        RequireSha(frame.ValidationCorpusFingerprintSha256, "VALIDATION corpus");
        RequireSha(frame.TrainIdentityFingerprintSha256, "TRAIN identities");
        RequireSha(frame.ValidationIdentityFingerprintSha256, "VALIDATION identities");
        if (frame.TrainCorpusFingerprintSha256.Equals(frame.ValidationCorpusFingerprintSha256,
                StringComparison.OrdinalIgnoreCase)
            || frame.TrainIdentityFingerprintSha256.Equals(frame.ValidationIdentityFingerprintSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("TRAIN e VALIDATION precisam de fingerprints distintos.");
        if (frame.Pairs is null || frame.Pairs.Count == 0)
            throw new ArgumentException("Frame candidato vazio.");
        if (frame.CandidateBlockingPasses is not null)
        {
            RequireSha(frame.ProjectionFingerprintSha256, "projection");
            RequireSha(frame.BlockingFrameFingerprintSha256, "blocking");
            if (string.IsNullOrWhiteSpace(frame.ProjectionSchemaVersion)
                || frame.CandidateBlockingPasses.Count == 0
                || frame.CandidateBlockingPasses.Any(x => x is null
                    || string.IsNullOrWhiteSpace(x.PassId) || x.Fields is null
                    || x.Fields.Count == 0 || x.Fields.Any(string.IsNullOrWhiteSpace)
                    || x.Fields.Distinct(StringComparer.Ordinal).Count() != x.Fields.Count)
                || frame.CandidateBlockingPasses.Select(x => x.PassId)
                    .Distinct(StringComparer.Ordinal).Count() != frame.CandidateBlockingPasses.Count)
                throw new InvalidDataException("Metadados de blocking/projeção do frame inválidos.");
        }
        else if (frame.ProjectionSchemaVersion is not null || frame.ProjectionFingerprintSha256 is not null
            || frame.BlockingFrameFingerprintSha256 is not null)
            throw new InvalidDataException("Metadados de blocking/projeção devem ser fornecidos juntos.");
        // A lista fornecida pelo chamador pode ser mutável; congele um snapshot antes de validar.
        // Os pares são records com campos imutáveis (strings, DateOnly e valores escalares).
        this.frame = frame with
        {
            Pairs = Array.AsReadOnly(frame.Pairs.ToArray()),
            TrainPreBlockingBaseIdentityIds = frame.TrainPreBlockingBaseIdentityIds is null
                ? null : Array.AsReadOnly(frame.TrainPreBlockingBaseIdentityIds.ToArray()),
            ValidationPreBlockingBaseIdentityIds = frame.ValidationPreBlockingBaseIdentityIds is null
                ? null : Array.AsReadOnly(frame.ValidationPreBlockingBaseIdentityIds.ToArray()),
            CandidateBlockingPasses = frame.CandidateBlockingPasses is null ? null
                : Array.AsReadOnly(frame.CandidateBlockingPasses.Select(x =>
                    new LinkageBlockingPass(x.PassId, Array.AsReadOnly(x.Fields.ToArray()))).ToArray())
        };

        var byId = options.ToArray();
        if (byId.Length is < 1 or > 64
            || byId.Any(x => x is null || string.IsNullOrWhiteSpace(x.OptionId)
                || x.SmoothingAlpha <= 0m || x.SmoothingAlpha > 1000m
                || x.DecisionThreshold <= 0m || x.DecisionThreshold >= 1m
                || x.TfWeight is < 0m or > 1m || !Enum.IsDefined(x.TfSide)
                || x.TfMinimumFrequency is < 0m or > 1m
                || x.TfMinimumIndependentSupport < 1)
            || (frozenTfReference is null && byId.Any(x => x.TfWeight > 0m))
            || byId.Select(x => x.OptionId).Distinct(StringComparer.Ordinal).Count() != byId.Length)
            throw new ArgumentException("Opções FS V9 inválidas ou duplicadas.");
        foreach (var option in byId)
            if (option.JointSupportPolicy is not null) V9JointSupportEstimation.ValidatePolicy(option.JointSupportPolicy);
        this.options = byId.ToDictionary(x => x.OptionId, StringComparer.Ordinal);
        this.frozenTfReference = frozenTfReference;
        if (frame.CandidateBlockingPasses is not null && byId.Any(x => x.TfWeight > 0m)
            && !string.Equals(frozenTfReference!.BlockingFrameFingerprint,
                frame.BlockingFrameFingerprintSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Referência TF divergente do frame blocking calibrado.");

        ValidateFrame();
    }

    public string ModelFamily => Family;
    public string AdapterVersion => Version;

    /// <summary>
    /// Exports the exact TRAIN fit for a pinned, portable preview snapshot.
    /// This neither approves nor activates it. Positive TF exports the pinned
    /// frequency reference and conditional TRAIN unit U alongside the fit.
    /// </summary>
    public V9FittedSnapshot ExportFrozenSnapshot(
        AdaptiveCalibrationFitDraft fitted,
        IReadOnlyList<LinkageBlockingPass> blockingPasses,
        string projectionSchemaVersion,
        string projectionFingerprintSha256,
        decimal conflictMargin,
        V9SnapshotCandidateGrouping? candidateGrouping = null)
    {
        ArgumentNullException.ThrowIfNull(fitted);
        if (frame.CandidateBlockingPasses is null || blockingPasses is null
            || string.IsNullOrWhiteSpace(frame.ProjectionSchemaVersion)
            || frame.ProjectionSchemaVersion != projectionSchemaVersion
            || frame.ProjectionFingerprintSha256 != projectionFingerprintSha256
            || !BlockingPassesEqual(frame.CandidateBlockingPasses, blockingPasses))
            throw new InvalidDataException("Exportação exige blocking e projeção idênticos ao frame de calibração.");
        if (fitted.ModelFamily != Family || fitted.AdapterVersion != Version
            || !fitted.TrainOnly
            || fitted.TrainingCorpusFingerprintSha256 != frame.TrainCorpusFingerprintSha256
            || !options.TryGetValue(fitted.OptionId, out var config)
            || fitted.ConfigurationFingerprintSha256 != config.FingerprintSha256
            || !fits.TryGetValue(fitted.FittedParametersFingerprintSha256, out var frozen)
            || frozen.OptionId != fitted.OptionId)
            throw new InvalidDataException("Exportação exige fit TRAIN congelado por este adaptador.");
        V9SnapshotFirstNameTf? tf = null;
        if (config.TfWeight > 0m)
        {
            if (frozen.TrainUnitU is null || frozenTfReference is null
                || frozen.TrainUnitU.TrainFingerprintSha256 != frame.TrainCorpusFingerprintSha256
                || frozen.TrainUnitU.Side != config.TfSide)
                throw new InvalidDataException("Referência TF ou baseline U TRAIN não coincide com o fit congelado.");
            var reference = frozenTfReference.ExportForSnapshot(config.TfSide);
            tf = new V9SnapshotFirstNameTf(config.TfSide, reference.Reference,
                reference.Frequencies, frozen.TrainUnitU.FirstNameUnitUByValue,
                frozen.TrainUnitU.FingerprintSha256, config.TfMinimumFrequency,
                config.TfMinimumIndependentSupport, frame.TrainCorpusFingerprintSha256);
        }
        return V9FittedSnapshot.Create(new V9FittedSnapshotPayload(
            (frozen.JointSupport is null ? V9FittedSnapshot.SchemaVersion : V9FittedSnapshot.SupportSchemaVersion), CompositeNominalComparisonV9.ContractVersion,
            projectionSchemaVersion, projectionFingerprintSha256,
            frozen.FingerprintSha256,
            frame.TrainCorpusFingerprintSha256, frame.ValidationCorpusFingerprintSha256,
            frame.TrainIdentityFingerprintSha256, frame.ValidationIdentityFingerprintSha256,
            Version, frozen.OptionId, frozen.Threshold, conflictMargin, config.TfWeight,
            blockingPasses, frozen.Cells, frozen.Parameters, tf,
            BlockingFrameFingerprintSha256: frame.BlockingFrameFingerprintSha256,
            CandidateGrouping: candidateGrouping, JointSupport: frozen.JointSupport));
    }

    private static bool BlockingPassesEqual(
        IReadOnlyList<LinkageBlockingPass> left, IReadOnlyList<LinkageBlockingPass> right)
    {
        if (left.Count != right.Count || left.Count == 0 || right.Any(x => x is null || x.Fields is null))
            return false;
        var a = left.OrderBy(x => x.PassId, StringComparer.Ordinal).ToArray();
        var b = right.OrderBy(x => x.PassId, StringComparer.Ordinal).ToArray();
        return a.Zip(b).All(x => x.First.PassId == x.Second.PassId
            && x.First.Fields.SequenceEqual(x.Second.Fields, StringComparer.Ordinal));
    }

    public AdaptiveCalibrationFitDraft FitTrain(
        AdaptiveCalibrationModelOption modelOption,
        AdaptiveCalibrationSlice train)
    {
        ArgumentNullException.ThrowIfNull(modelOption);
        ValidateSlice(train, "TRAIN", frame.TrainCorpusFingerprintSha256,
            frame.TrainIdentityFingerprintSha256);

        if (modelOption.ModelFamily != Family || modelOption.AdapterVersion != Version
            || !options.TryGetValue(modelOption.OptionId, out var config)
            || modelOption.ConfigurationFingerprintSha256 != config.FingerprintSha256)
            throw new InvalidDataException("Opção/configuração FS V9 não coincide com o adaptador.");

        var schema = PersonResolutionFsCellSchema.Generate([
            PersonResolutionContractCatalog.FullName,
            PersonResolutionContractCatalog.MotherName,
            PersonResolutionContractCatalog.BirthDate]);
        if (schema.Cells.Count(x => x.AttributeCode == PersonResolutionContractCatalog.FullName) != 9
            || schema.Cells.Count(x => x.AttributeCode == PersonResolutionContractCatalog.MotherName) != 9)
            throw new InvalidDataException("Catálogo V9 não contém os nove níveis nominais canônicos.");

        var rows = frame.Pairs.Where(x => x.Partition == "TRAIN").ToArray();
        var matched = rows.Where(x => x.IsTruePair).ToArray();
        var unmatched = rows.Where(x => !x.IsTruePair).ToArray();
        RequireClassSupport(matched, "M");
        RequireClassSupport(unmatched, "U");

        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        var cellCount = levels.Length * levels.Length;
        var mCounts = CountNominal(matched);
        var uCounts = CountNominal(unmatched);
        var alpha = config.SmoothingAlpha;

        var cells = new List<FellegiSunterJointNominalCellV9>(cellCount);
        foreach (var person in levels)
        foreach (var mother in levels)
        {
            var key = (person, mother);
            var m = (mCounts.GetValueOrDefault(key) + alpha)
                / (matched.Length + alpha * cellCount);
            var u = (uCounts.GetValueOrDefault(key) + alpha)
                / (unmatched.Length + alpha * cellCount);
            cells.Add(new FellegiSunterJointNominalCellV9(person, mother, m, u));
        }

        V9JointSupportProvenance? support = null;
        if (config.JointSupportPolicy is { } jointSupportPolicy)
        {
            var estimated = V9JointSupportEstimation.Estimate(cells.Select(c => new V9JointObservedCell(c.Person, c.Mother,
                mCounts.GetValueOrDefault((c.Person, c.Mother)), uCounts.GetValueOrDefault((c.Person, c.Mother)))),
                jointSupportPolicy, alpha, frame.TrainCorpusFingerprintSha256);
            cells = estimated.Probabilities.ToList();
            support = estimated.Provenance;
        }

        var mBirth = BirthCounts(matched);
        var uBirth = BirthCounts(unmatched);
        if (mBirth.Total < 2 || uBirth.Total < 2)
            throw new InvalidDataException("Nascimento TRAIN exige ao menos dois pares observáveis por classe.");

        var prior = (matched.Length + alpha) / (matched.Length + unmatched.Length + 2m * alpha);
        var parameters = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = prior,
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.NeutralMissingEvidenceScoring] = 1m,
            [LinkageParameterCatalog.BirthSingleEvidenceScoring] = 1m,
            ["M_DATA_NASCIMENTO_EXACT"] = (mBirth.Exact + alpha) / (mBirth.Total + 2m * alpha),
            ["M_DATA_NASCIMENTO_DIFF"] = (mBirth.Diff + alpha) / (mBirth.Total + 2m * alpha),
            ["U_DATA_NASCIMENTO_EXACT"] = (uBirth.Exact + alpha) / (uBirth.Total + 2m * alpha),
            ["U_DATA_NASCIMENTO_DIFF"] = (uBirth.Diff + alpha) / (uBirth.Total + 2m * alpha)
        };

        V9TrainSqlFirstNameUCalibration.Frozen? trainU = null;
        if (config.TfWeight > 0m)
        {
            if (frozenTfReference is null)
                throw new InvalidOperationException("TF positiva exige snapshot IBGE fixado.");
            trainU = V9TrainSqlFirstNameUCalibration.Fit(
                rows, config.TfSide, alpha, frame.TrainCorpusFingerprintSha256);
        }

        var fitSha = FittedFingerprintSha256(config, cells, parameters, schema.FingerprintSha256,
            frame.TrainCorpusFingerprintSha256, frame.TrainIdentityFingerprintSha256);
        if (trainU is not null)
            fitSha = Sha(string.Join("|", fitSha, trainU.FingerprintSha256,
                frozenTfReference!.ReferenceVersionId,
                frozenTfReference.ReferenceCode,
                frozenTfReference.PublishedReferenceSha256,
                frozenTfReference.MaterializedFrequencySha256,
                frozenTfReference.BlockingFrameFingerprint));
        if (frame.CandidateBlockingPasses is not null)
            fitSha = Sha(fitSha + "|FRAME_METADATA|" + System.Text.Json.JsonSerializer.Serialize(new
            {
                frame.ProjectionSchemaVersion,
                frame.ProjectionFingerprintSha256,
                frame.BlockingFrameFingerprintSha256,
                Passes = frame.CandidateBlockingPasses.OrderBy(x => x.PassId, StringComparer.Ordinal)
            }));
        if (support is not null) fitSha = Sha(fitSha + "|JOINT_SUPPORT|" + System.Text.Json.JsonSerializer.Serialize(support));
        fits[fitSha] = new FrozenFit(fitSha, config.OptionId, config.DecisionThreshold,
            Array.AsReadOnly(cells.ToArray()),
            new Dictionary<string, decimal>(parameters, StringComparer.Ordinal), trainU, support);

        return new AdaptiveCalibrationFitDraft(
            Family, Version, config.OptionId, config.FingerprintSha256, fitSha,
            train.CorpusFingerprintSha256, TrainOnly: true);
    }

    public AdaptiveCalibrationHoldoutMetrics EvaluateValidation(
        AdaptiveCalibrationFitDraft fitted,
        AdaptiveCalibrationSlice validation)
    {
        ArgumentNullException.ThrowIfNull(fitted);
        ValidateSlice(validation, "VALIDATION", frame.ValidationCorpusFingerprintSha256,
            frame.ValidationIdentityFingerprintSha256);
        if (fitted.ModelFamily != Family || fitted.AdapterVersion != Version
            || !fitted.TrainOnly
            || fitted.TrainingCorpusFingerprintSha256 != frame.TrainCorpusFingerprintSha256
            || !options.TryGetValue(fitted.OptionId, out var config)
            || fitted.ConfigurationFingerprintSha256 != config.FingerprintSha256
            || !fits.TryGetValue(fitted.FittedParametersFingerprintSha256, out var frozen)
            || frozen.OptionId != fitted.OptionId
            || frozen.FingerprintSha256 != fitted.FittedParametersFingerprintSha256)
            throw new InvalidDataException("Fit V9 desconhecido ou não congelado por este adaptador.");

        var rows = frame.Pairs.Where(x => x.Partition == "VALIDATION").ToArray();
        var truePairs = rows.Count(x => x.IsTruePair);
        var falsePairs = rows.Length - truePairs;
        if (truePairs <= 0 || falsePairs <= 0)
            throw new InvalidDataException("VALIDATION precisa conter M e U.");

        // The fitted table is immutable across the whole holdout: validate and
        // precompute its 81 log ratios once, rather than hashing it per pair.
        var frozenNominal = FrozenFellegiSunterJointNominalV9.Freeze(
            frozen.Cells, CompositeNominalComparisonV9.ContractVersion);
        long tp = 0, fp = 0;
        foreach (var row in rows)
        {
            CompositeNominalTfPreviewV9? nominalTf = null;
            if (config.TfWeight > 0m)
            {
                if (frozen.TrainUnitU is null || frozenTfReference is null)
                    throw new InvalidDataException("Fit TF V9 inconsistente.");
                nominalTf = V9TrainSqlFirstNameUCalibration.Preview(
                    frozen.TrainUnitU, frozenTfReference,
                    CompositeNominalComparisonV9.Classify(row.LeftPersonName, row.RightPersonName),
                    CompositeNominalComparisonV9.Classify(row.LeftMotherName, row.RightMotherName),
                    row, config.TfWeight, config.TfMinimumFrequency,
                    config.TfMinimumIndependentSupport);
            }
            var score = FellegiSunterScoring.CalculateFrozenCompositeNominalV9PreviewWithBreakdown(
                frozen.Parameters,
                row.LeftPersonName, row.RightPersonName,
                row.LeftMotherName, row.RightMotherName,
                frozenNominal,
                leftBirthDate: row.LeftBirthDate, rightBirthDate: row.RightBirthDate,
                nominalTf: nominalTf);
            var accepted = score.Score.Posterior >= frozen.Threshold;
            if (accepted && row.IsTruePair) tp++;
            else if (accepted) fp++;
        }

        var falseDegrees = rows.Where(x => !x.IsTruePair)
            .Select(x => (double)x.CandidateDegreeForSource)
            .OrderBy(x => x).ToArray();
        var p99 = falseDegrees.Length == 0 ? 0d : PercentileNearestRank(falseDegrees, .99d);
        // Independent validation groups are the entire pre-blocking cohort,
        // including identities with zero SQL candidates. Restricting this
        // denominator to groups appearing in SQL induces selection bias.
        var groups = PartitionIdentityGroups("VALIDATION").Count;

        return new AdaptiveCalibrationHoldoutMetrics(
            TruePairsWithoutCpf: truePairs,
            TrueAcceptedLinks: tp,
            FalseAcceptedLinks: fp,
            Abstentions: truePairs - tp,
            BlockingFalseCandidatePairs: falsePairs,
            BlockingFalseDegreeP99: p99,
            IndependentlyLabeledIdentityGroups: groups);
    }


    /// <summary>
    /// Diagnostic source-observation Rank -> Resolve on both directions of the
    /// COMPLETE physical SQL candidate union. Its true-link denominator is
    /// TWICE the unordered cross-manager M-pair universe (directed arcs).
    /// It uses the TRAIN-fitted 81-cell model with NO refit on VALIDATION.
    /// The decision kernel is shared with operational Resolve, but this
    /// synthetic evaluation is NOT a persisted Runner/HML run.
    /// </summary>
    internal IReadOnlyList<V9PhysicalFinalResolveMetric> EvaluatePostResolveValidation(
        IReadOnlyCollection<V9PhysicalCalibrationObservation> observations,
        IReadOnlyList<(int A, int B)> validationSqlPairs,
        long eligibleUnorderedM, long recoveredUnorderedM,
        decimal conflictMargin, int maximumAcceptedPerBaseIdentity)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(validationSqlPairs);
        if (eligibleUnorderedM <= 0 || recoveredUnorderedM < 0
            || recoveredUnorderedM > eligibleUnorderedM
            || conflictMargin < 0m || conflictMargin > 1000m
            || maximumAcceptedPerBaseIdentity < 1)
            throw new InvalidDataException("M ou política pós-Resolve inválidos.");

        var validation = observations.Where(x => x.Partition == "VALIDATION")
            .ToDictionary(x => x.ObservationId);
        if (validation.Count < 2)
            throw new InvalidDataException("VALIDATION precisa de observações.");
        static Guid SyntheticCandidateId(int id) =>
            new(id, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);
        var candidatesByGuid = validation.Values.ToDictionary(
            x => SyntheticCandidateId(x.ObservationId));
        var neighbors = validation.Keys.ToDictionary(x => x, _ => new HashSet<int>());
        foreach (var (left, right) in validationSqlPairs)
        {
            if (left >= right || !validation.TryGetValue(left, out var leftObservation)
                || !validation.TryGetValue(right, out var rightObservation)
                || leftObservation.Gestor == rightObservation.Gestor
                || !neighbors[left].Add(right) || !neighbors[right].Add(left))
                throw new InvalidDataException("União SQL física duplicada, contaminada ou sem orientação canônica.");
        }
        if (validationSqlPairs.LongCount(x => validation[x.A].BasePersonId
                == validation[x.B].BasePersonId) != recoveredUnorderedM)
            throw new InvalidDataException("M SQL de VALIDATION divergiu das arestas lidas.");

        var results = new List<V9PhysicalFinalResolveMetric>(options.Count);
        foreach (var config in options.Values.OrderBy(x => x.OptionId, StringComparer.Ordinal))
        {
            var fitted = fits.Values.SingleOrDefault(x => x.OptionId == config.OptionId)
                ?? throw new InvalidDataException("Modelo TRAIN não ajustado para Resolve.");
            if (config.TfWeight > 0m && (fitted.TrainUnitU is null || frozenTfReference is null))
                throw new InvalidDataException("Fit TF positivo não está congelado.");

            var groups = validation.Values.Select(x => x.BasePersonId)
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(x => x, x => (Tp: 0L, Fp: 0L), StringComparer.Ordinal);
            long tp = 0, fp = 0, conflicts = 0, unresolved = 0;
            foreach (var source in validation.Values.OrderBy(x => x.ObservationId))
            {
                var list = neighbors[source.ObservationId].OrderBy(x => x)
                    .Select(id => validation[id]).ToArray();
                var candidates = list.Select(x => new LinkageCandidate(
                    SyntheticCandidateId(x.ObservationId), x.PersonName,
                    x.BirthDate, x.MotherName)).ToArray();
                var sourceValue = new IdentityObservation(
                    null, "SEM_CPF", source.PersonName, source.BirthDate, source.MotherName);
                CompositeNominalTfPreviewV9? Tf(
                    IdentityObservation ignored, LinkageCandidate candidate)
                {
                    if (fitted.TrainUnitU is null || frozenTfReference is null)
                        return null;
                    var target = candidatesByGuid[candidate.PessoaUuid];
                    var pair = new V9FsAdaptiveCandidatePair(
                        "VALIDATION", source.BasePersonId, target.BasePersonId,
                        source.BasePersonId == target.BasePersonId,
                        source.PersonName, target.PersonName,
                        source.MotherName, target.MotherName,
                        source.BirthDate, target.BirthDate, 1);
                    return V9TrainSqlFirstNameUCalibration.Preview(
                        fitted.TrainUnitU, frozenTfReference,
                        CompositeNominalComparisonV9.Classify(
                            source.PersonName, target.PersonName),
                        CompositeNominalComparisonV9.Classify(
                            source.MotherName, target.MotherName),
                        pair, config.TfWeight,
                        config.TfMinimumFrequency, config.TfMinimumIndependentSupport);
                }
                var decision = ProbabilisticLinkageDecisions.ResolveCompositeNominalV9Preview(
                    SyntheticCandidateId(source.ObservationId),
                    fitted.Parameters, sourceValue, candidates, fitted.Cells,
                    CompositeNominalComparisonV9.ContractVersion,
                    config.DecisionThreshold, conflictMargin,
                    config.TfWeight > 0m ? Tf : null,
                    config.TfWeight > 0m ? config.TfSide : null);
                if (decision.Status == ResolutionStatus.CONFLITO)
                {
                    conflicts++;
                    continue;
                }
                if (decision.Status == ResolutionStatus.NAO_RESOLVIDO)
                {
                    unresolved++;
                    continue;
                }
                if (decision.Status != ResolutionStatus.RESOLVIDO
                    || decision.PessoaUuidResolvido is not { } targetId
                    || !candidatesByGuid.TryGetValue(targetId, out var targetObs)
                    || !neighbors[source.ObservationId].Contains(targetObs.ObservationId))
                    throw new InvalidDataException("Resolve retornou alvo fora da união SQL.");
                var score = groups[source.BasePersonId];
                if (targetObs.BasePersonId == source.BasePersonId)
                {
                    tp++;
                    score.Tp++;
                }
                else
                {
                    fp++;
                    score.Fp++;
                }
                groups[source.BasePersonId] = score;
            }

            var directedEligible = checked(2L * eligibleUnorderedM);
            var directedRecovered = checked(2L * recoveredUnorderedM);
            if (tp > directedRecovered
                || tp + fp + conflicts + unresolved != validation.Count)
                throw new InvalidDataException("Decisões pós-Resolve não reconciliam com o censo.");
            var confidence = V9IdentityClusterFdr.UpperBoundForPopulationMeanRatio(
                groups.OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => new V9IdentityClusterFdr.IdentityGroup(
                        x.Key, x.Value.Tp, x.Value.Fp)),
                maximumAcceptedPerBaseIdentity, alpha: .05d);
            results.Add(new V9PhysicalFinalResolveMetric(
                config.OptionId, config.DecisionThreshold, config.TfWeight,
                directedEligible, directedRecovered, tp, fp, conflicts, unresolved,
                (double)directedRecovered / directedEligible,
                (double)tp / directedEligible,
                tp + fp == 0 ? null : (double)fp / (tp + fp),
                confidence.UpperFdr, groups.Count,
                maximumAcceptedPerBaseIdentity, .05d,
                FinalRankResolveEvaluated: true, OperationallyCertified: false));
        }
        return Array.AsReadOnly(results.ToArray());
    }


    /// <summary>
    /// Corrects the diagnostic denominator: one outcome per eligible source
    /// observation, NOT one outcome per directed true observation-pair.
    /// The optional oracle mode coalesces candidate observations using known
    /// SYNTHETIC ground-truth identity; this is an optimistic diagnostic upper
    /// bound, NEVER evidence that the operational Gold would cluster correctly.
    /// No TEST input, refit, publishing or automatic selector activation.
    /// </summary>
    public IReadOnlyList<V9PhysicalSourceResolveMetric> EvaluateSourceUnitResolveValidation(
        IReadOnlyCollection<V9PhysicalCalibrationObservation> observations,
        IReadOnlyList<(int A, int B)> validationSqlPairs,
        IReadOnlyCollection<decimal> conflictMargins,
        int maximumAcceptedPerBaseIdentity,
        bool includeFsBlindGrouping = false,
        decimal minimumFsGroupingPosterior = .99m,
        int? maximumBlindClusterSize = null,
        IReadOnlySet<string>? evaluatedOptionIds = null,
        bool fsBlindGroupingOnly = false,
        int? totalComparedConfigurations = null,
        bool includeIdentityOutcomes = false)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(validationSqlPairs);
        ArgumentNullException.ThrowIfNull(conflictMargins);
        if ((maximumBlindClusterSize.HasValue && maximumBlindClusterSize.Value < 2)
            || (fsBlindGroupingOnly && !includeFsBlindGrouping)
            || (evaluatedOptionIds is not null && (evaluatedOptionIds.Count == 0
                || evaluatedOptionIds.Any(id => !options.ContainsKey(id))))
            || maximumAcceptedPerBaseIdentity < 1
            || (includeFsBlindGrouping && minimumFsGroupingPosterior is <= .5m or >= 1m)
            || conflictMargins.Count is < 1 or > 8
            || conflictMargins.Any(x => x is < 0m or > 1000m)
            || conflictMargins.Distinct().Count() != conflictMargins.Count)
            throw new InvalidDataException("Margens ou cap da avaliação source inválidos.");
        if (observations.Any(x => x is null || x.ObservationId <= 0
                || string.IsNullOrWhiteSpace(x.BasePersonId)
                || x.Partition is not ("TRAIN" or "VALIDATION"))
            || observations.Select(x => x.ObservationId).Distinct().Count() != observations.Count)
            throw new InvalidDataException("Observações source inválidas, duplicadas ou com partição TEST.");

        var sourceTrainGroups = observations.Where(x => x.Partition == "TRAIN")
            .Select(x => x.BasePersonId).ToHashSet(StringComparer.Ordinal);
        var sourceValidationGroups = observations.Where(x => x.Partition == "VALIDATION")
            .Select(x => x.BasePersonId).ToHashSet(StringComparer.Ordinal);
        if (sourceTrainGroups.Overlaps(sourceValidationGroups)
            || !sourceValidationGroups.SetEquals(PartitionIdentityGroups("VALIDATION"))
            || (sourceTrainGroups.Count > 0 && !sourceTrainGroups.SetEquals(PartitionIdentityGroups("TRAIN"))))
            throw new InvalidDataException("Coorte source diverge das identidades congeladas TRAIN/VALIDATION.");

        var validation = observations.Where(x => x.Partition == "VALIDATION")
            .ToDictionary(x => x.ObservationId);
        if (validation.Count < 2
            || validation.Values.Any(x => string.IsNullOrWhiteSpace(x.BasePersonId)))
            throw new InvalidDataException("Universo completo de VALIDATION ausente.");
        static Guid ObservationGuid(int id) =>
            new(id, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);
        var observationByGuid = validation.Values.ToDictionary(
            x => ObservationGuid(x.ObservationId));

        // Grouping key is a synthetic TRUTH oracle for the second diagnostic
        // arm only, not a model-inferred or production Gold identifier.
        var orderedBases = validation.Values.Select(x => x.BasePersonId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var groupIdByBase = orderedBases.Select((id, index) =>
                (Id: id, Guid: new Guid(index + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2)))
            .ToDictionary(x => x.Id, x => x.Guid, StringComparer.Ordinal);
        var baseByGroupGuid = groupIdByBase.ToDictionary(x => x.Value, x => x.Key);

        // THIRD ARM, fully BLIND to synthetic ground-truth labels:
        // a provisional entity is the complete normalized composite
        // (person, mother, DOB) when all three values are available.
        // Missing values never allow two observations to coalesce.
        // This is a conservative surrogate, NOT operational Gold.
        var exactGroups = new Dictionary<(string, string, DateOnly), Guid>();
        var blindByObservationId = new Dictionary<int, Guid>();
        var trueBasesByBlindGuid = new Dictionary<Guid, HashSet<string>>();
        int blindSequence = 0;
        foreach (var observed in validation.Values.OrderBy(x => x.ObservationId))
        {
            var person = IdentityComparison.NormalizeText(observed.PersonName);
            var mother = IdentityComparison.NormalizeText(observed.MotherName);
            Guid id;
            if (person is not null && mother is not null
                && observed.BirthDate is { } dob)
            {
                var key = (person, mother, dob);
                if (!exactGroups.TryGetValue(key, out id))
                {
                    id = new Guid(checked(++blindSequence), 0, 0,
                        0, 0, 0, 0, 0, 0, 0, 3);
                    exactGroups[key] = id;
                }
            }
            else
            {
                id = new Guid(checked(++blindSequence), 0, 0,
                    0, 0, 0, 0, 0, 0, 0, 3);
            }
            blindByObservationId[observed.ObservationId] = id;
            if (!trueBasesByBlindGuid.TryGetValue(id, out var trueBases))
            {
                trueBases = new HashSet<string>(StringComparer.Ordinal);
                trueBasesByBlindGuid[id] = trueBases;
            }
            // Truth is recorded ONLY for evaluation of impurity / FP,
            // never for choosing this blind group's key or membership.
            trueBases.Add(observed.BasePersonId);
        }
        var impureBlindGroups = trueBasesByBlindGuid.Values
            .Count(bases => bases.Count > 1);

        var neighbors = validation.Keys.ToDictionary(id => id, _ => new HashSet<int>());
        foreach (var (a, b) in validationSqlPairs)
        {
            if (a >= b || !validation.TryGetValue(a, out var left)
                || !validation.TryGetValue(b, out var right)
                || left.Gestor == right.Gestor
                || !neighbors[a].Add(b) || !neighbors[b].Add(a))
                throw new InvalidDataException("Aresta SQL física inválida ou duplicada.");
        }
        var eligibleBases = validation.Values
            .GroupBy(x => x.BasePersonId, StringComparer.Ordinal)
            .Where(g => g.Select(x => x.Gestor)
                .Distinct(StringComparer.Ordinal).Skip(1).Any())
            .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        var eligibleSources = validation.Values.LongCount(
            x => eligibleBases.Contains(x.BasePersonId));
        var eligibleSourcesByBase = includeIdentityOutcomes ? validation.Values
            .GroupBy(x => x.BasePersonId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => eligibleBases.Contains(g.Key) ? g.LongCount() : 0, StringComparer.Ordinal) : null;
        if (eligibleSources == 0)
            throw new InvalidDataException("Nenhuma fonte possui par verdadeiro elegível.");
        var trueSqlSources = validation.Values.LongCount(source =>
            eligibleBases.Contains(source.BasePersonId)
            && neighbors[source.ObservationId].Any(other =>
                validation[other].BasePersonId == source.BasePersonId));
        if (trueSqlSources > eligibleSources)
            throw new InvalidDataException("Cobertura SQL por fonte inconsistente.");

        var metrics = new List<V9PhysicalSourceResolveMetric>();
        var localComparedConfigurations = checked(
            (evaluatedOptionIds?.Count ?? options.Count) * conflictMargins.Count * (fsBlindGroupingOnly ? 1 : includeFsBlindGrouping ? 4 : 3));
        var predeclaredComparedConfigurations = totalComparedConfigurations ?? localComparedConfigurations;
        if (predeclaredComparedConfigurations < localComparedConfigurations)
            throw new InvalidDataException("Multiplicidade global não pode omitir configurações comparadas.");
        foreach (var config in options.Values.Where(x => evaluatedOptionIds is null || evaluatedOptionIds.Contains(x.OptionId)).OrderBy(x => x.OptionId, StringComparer.Ordinal))
        {
            var fitted = fits.Values.SingleOrDefault(x => x.OptionId == config.OptionId)
                ?? throw new InvalidDataException("Fit TRAIN necessário para avaliar Resolve.");
            if (config.TfWeight > 0m && (fitted.TrainUnitU is null || frozenTfReference is null))
                throw new InvalidDataException("TF positiva sem snapshot TRAIN congelado.");

            var counters = conflictMargins.SelectMany(margin =>
                    Enumerable.Range(0, 3).Select(groupMode => (margin, groupMode)))
                .ToDictionary(x => x,
                    _ => new V9SourceResolveAccumulator(orderedBases));
            // Scores are computed once from the SAME frozen TRAIN fit for
            // each source. FS grouping only sees observed SQL adjacency
            // and those ranked scores, never the validation truth labels.
            var rankedBySource = includeFsBlindGrouping
                ? new Dictionary<int, IReadOnlyList<CandidateScore>>()
                : null;

            foreach (var source in validation.Values.OrderBy(x => x.ObservationId))
            {
                var candidates = neighbors[source.ObservationId].OrderBy(id => id)
                    .Select(id => validation[id])
                    .Select(x => new LinkageCandidate(
                        ObservationGuid(x.ObservationId), x.PersonName,
                        x.BirthDate, x.MotherName)).ToArray();
                var sourceObservation = new IdentityObservation(
                    null, "SEM_CPF", source.PersonName, source.BirthDate, source.MotherName);

                CompositeNominalTfPreviewV9? TfForPair(
                    IdentityObservation ignored, LinkageCandidate candidate)
                {
                    if (fitted.TrainUnitU is null || frozenTfReference is null)
                        return null;
                    var target = observationByGuid[candidate.PessoaUuid];
                    var pair = new V9FsAdaptiveCandidatePair(
                        "VALIDATION", source.BasePersonId, target.BasePersonId,
                        source.BasePersonId == target.BasePersonId,
                        source.PersonName, target.PersonName,
                        source.MotherName, target.MotherName,
                        source.BirthDate, target.BirthDate, 1);
                    return V9TrainSqlFirstNameUCalibration.Preview(
                        fitted.TrainUnitU, frozenTfReference,
                        CompositeNominalComparisonV9.Classify(
                            source.PersonName, target.PersonName),
                        CompositeNominalComparisonV9.Classify(
                            source.MotherName, target.MotherName),
                        pair, config.TfWeight,
                        config.TfMinimumFrequency, config.TfMinimumIndependentSupport);
                }

                // Calculate the SAME FS evidence exactly once per observed
                // candidate; do NOT use ground-truth identity to choose scores.
                var ranked = ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview(
                    fitted.Parameters, sourceObservation, candidates, fitted.Cells,
                    CompositeNominalComparisonV9.ContractVersion,
                    config.TfWeight > 0m ? TfForPair : null,
                    config.TfWeight > 0m ? config.TfSide : null);
                if (rankedBySource is not null)
                    rankedBySource.Add(source.ObservationId, ranked);

                // Oracle Gold upper-bound: collapse multiple observations into
                // one synthetic ground-truth entity, retain its strongest FS
                // score, then reuse EXACTLY the same ResolveRankedCore kernel.
                var groupedRanking = fsBlindGroupingOnly ? [] : ranked
                    .GroupBy(x => groupIdByBase[
                        observationByGuid[x.PessoaUuid].BasePersonId])
                    .Select(g => g.First() with { PessoaUuid = g.Key })
                    .OrderByDescending(x => x.LogOdds)
                    .ThenBy(x => x.PessoaUuid).ToArray();
                var blindRanking = fsBlindGroupingOnly ? [] : ranked
                    .GroupBy(x => blindByObservationId[
                        observationByGuid[x.PessoaUuid].ObservationId])
                    .Select(g => g.First() with { PessoaUuid = g.Key })
                    .OrderByDescending(x => x.LogOdds)
                    .ThenBy(x => x.PessoaUuid).ToArray();

                foreach (var margin in conflictMargins)
                foreach (var mode in fsBlindGroupingOnly ? Array.Empty<int>() : [0, 1, 2])
                {
                    var ranking = mode switch
                    {
                        0 => ranked,
                        1 => blindRanking,
                        2 => groupedRanking,
                        _ => throw new InvalidOperationException("Modo de agrupamento inválido.")
                    };
                    var decision = ProbabilisticLinkageDecisions
                        .ResolveRankedCompositeNominalV9Preview(
                            ObservationGuid(source.ObservationId),
                            fitted.Parameters, ranking,
                            config.DecisionThreshold, margin);
                    string? targetBase = null;
                    if (decision.Status == ResolutionStatus.RESOLVIDO)
                    {
                        if (decision.PessoaUuidResolvido is not { } selected)
                            throw new InvalidDataException("Resolve sem alvo em RESOLVIDO.");
                        if (mode == 2)
                        {
                            if (!baseByGroupGuid.TryGetValue(selected, out targetBase))
                                throw new InvalidDataException("Grupo oracle fora de VALIDATION.");
                        }
                        else if (mode == 1)
                        {
                            if (!trueBasesByBlindGuid.TryGetValue(selected, out var trueBases))
                                throw new InvalidDataException("Grupo blind fora de VALIDATION.");
                            // Impure inferred entities are ALWAYS errors,
                            // even if one of their sources shares the true ID.
                            // Never use the oracle to choose their real target.
                            targetBase = trueBases.Count == 1
                                ? trueBases.Single()
                                : "IMPURE_BLIND_GROUP:" + selected;
                        }
                        else
                        {
                            if (!observationByGuid.TryGetValue(selected, out var target))
                                throw new InvalidDataException("Observação resolvida fora do SQL.");
                            targetBase = target.BasePersonId;
                        }
                    }
                    counters[(margin, mode)].Add(
                        source.BasePersonId, targetBase, decision);
                }
            }


            if (rankedBySource is not null)
            {
                // Two directed Rank scores from the frozen C# FS scorer
                // for EVERY physical SQL pair. No oracle membership or
                // reconstructed/invented missing edge enters clustering.
                var fsEdges = validationSqlPairs.Select(pair =>
                {
                    var forward = rankedBySource[pair.A].Single(x =>
                        x.PessoaUuid == ObservationGuid(pair.B));
                    var backward = rankedBySource[pair.B].Single(x =>
                        x.PessoaUuid == ObservationGuid(pair.A));
                    return new V9BlindFsSqlEdge(
                        pair.A, pair.B, forward.LogOdds, backward.LogOdds);
                }).ToArray();
                var inferred = V9BlindIncrementalIdentityGrouping.Build(
                    validation.Values.ToArray(), fsEdges,
                    minimumFsGroupingPosterior,
                    maximumBlindClusterSize ?? maximumAcceptedPerBaseIdentity);
                // Synthetic truth is used ONLY AFTER grouping, to count
                // impure clusters and final TP/FP. Never for merging.
                var truthByInferredGuid = validation.Values
                    .GroupBy(x => inferred.IdentityByObservation[x.ObservationId])
                    .ToDictionary(x => x.Key,
                        x => x.Select(v => v.BasePersonId)
                            .Distinct(StringComparer.Ordinal).ToArray());
                var impureFsGroups = truthByInferredGuid.Values
                    .Count(x => x.Length > 1);
                foreach (var margin in conflictMargins.OrderBy(x => x))
                {
                    var counter = new V9SourceResolveAccumulator(orderedBases);
                    foreach (var source in validation.Values.OrderBy(x => x.ObservationId))
                    {
                        var collapsed = rankedBySource[source.ObservationId]
                            .GroupBy(x => inferred.IdentityByObservation[
                                observationByGuid[x.PessoaUuid].ObservationId])
                            .Select(g => g.First() with { PessoaUuid = g.Key })
                            .OrderByDescending(x => x.LogOdds)
                            .ThenBy(x => x.PessoaUuid).ToArray();
                        var decision = ProbabilisticLinkageDecisions
                            .ResolveRankedCompositeNominalV9Preview(
                                ObservationGuid(source.ObservationId),
                                fitted.Parameters, collapsed,
                                config.DecisionThreshold, margin);
                        string? targetBase = null;
                        if (decision.Status == ResolutionStatus.RESOLVIDO)
                        {
                            if (decision.PessoaUuidResolvido is not { } selected
                                || !truthByInferredGuid.TryGetValue(
                                    selected, out var bases))
                                throw new InvalidDataException(
                                    "Resolve FS blind retornou identidade desconhecida.");
                            targetBase = bases.Length == 1 ? bases[0]
                                : "IMPURE_FS_BLIND_GROUP:" + selected;
                        }
                        counter.Add(source.BasePersonId, targetBase, decision);
                    }
                    if (counter.Total != validation.Count
                        || counter.Tp > trueSqlSources
                        || counter.Tp > eligibleSources)
                        throw new InvalidDataException(
                            "Decisões FS blind não reconciliam por fonte.");
                    var sourcesByIdentity = counter.GroupContributions
                        .OrderBy(x => x.Key, StringComparer.Ordinal)
                        .Select(x => new V9IdentityClusterFdr.IdentityGroup(
                            x.Key, x.Value.Tp, x.Value.Fp)).ToArray();
                    var bound = V9IdentityClusterFdr.UpperBoundForPopulationMeanRatio(
                        sourcesByIdentity, maximumAcceptedPerBaseIdentity, alpha: .05d);
                    var cp = V9IdentityClusterFdr.UpperBoundForPopulationMeanRatioByAnyErrorCp(
                        sourcesByIdentity, maximumAcceptedPerBaseIdentity,
                        predeclaredComparedConfigurations, alpha: .05d);
                    metrics.Add(new V9PhysicalSourceResolveMetric(
                        config.OptionId, config.DecisionThreshold, config.TfWeight,
                        margin, "BLIND_FS_COMPLETE_LINK_CANDIDATES",
                        validation.Count, eligibleSources, trueSqlSources,
                        counter.Tp, counter.Fp,
                        counter.Conflicts, counter.Unresolved,
                        (double)trueSqlSources / eligibleSources,
                        (double)counter.Tp / eligibleSources,
                        counter.Tp + counter.Fp == 0 ? null
                            : (double)counter.Fp / (counter.Tp + counter.Fp),
                        bound.UpperFdr, orderedBases.Length,
                        counter.Reasons.OrderBy(x => x.Key, StringComparer.Ordinal)
                            .ToDictionary(x => x.Key, x => x.Value,
                                StringComparer.Ordinal),
                        SyntheticGoldOracleGrouping: false,
                        OperationallyCertified: false,
                        ImpureBlindGroups: impureFsGroups,
                        ConditionalClusterCpFdrUpper: cp.UpperFdrConditional,
                        ClopperPearsonUpperAnyFpByIdentity:
                            cp.UpperProbabilityOfAnyGroupErrorClopperPearson,
                        BonferroniComparedConfigurations: predeclaredComparedConfigurations,
                        ClusterIidIndependentlyValidated: false,
                        IdentityOutcomes: includeIdentityOutcomes ? Array.AsReadOnly(sourcesByIdentity.Select(g =>
                            new V9SourceIdentityOutcome(g.BaseIdentityFingerprint,
                                eligibleSourcesByBase!.GetValueOrDefault(g.BaseIdentityFingerprint),
                                g.TrueAccepted, g.FalseAccepted)).ToArray()) : null));
                }
            }

            foreach (var margin in conflictMargins.OrderBy(x => x))
            foreach (var mode in fsBlindGroupingOnly ? Array.Empty<int>() : [0, 1, 2])
            {
                var counter = counters[(margin, mode)];
                if (counter.Total != validation.Count || counter.Tp > trueSqlSources
                    || counter.Tp > eligibleSources)
                    throw new InvalidDataException("Decisões não reconciliam por fonte.");
                var sourcesByIdentity = counter.GroupContributions
                    .OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => new V9IdentityClusterFdr.IdentityGroup(
                        x.Key, x.Value.Tp, x.Value.Fp)).ToArray();
                var confidence = V9IdentityClusterFdr.UpperBoundForPopulationMeanRatio(
                    sourcesByIdentity, maximumAcceptedPerBaseIdentity, alpha: .05d);
                var cp = V9IdentityClusterFdr.UpperBoundForPopulationMeanRatioByAnyErrorCp(
                    sourcesByIdentity, maximumAcceptedPerBaseIdentity,
                    predeclaredComparedConfigurations, alpha: .05d);
                metrics.Add(new V9PhysicalSourceResolveMetric(
                    config.OptionId, config.DecisionThreshold, config.TfWeight,
                    margin, mode switch
                    {
                        0 => "RAW_SQL_OBSERVATION_CANDIDATES",
                        1 => "BLIND_EXACT_PERSON_MOTHER_DOB_GROUPING",
                        2 => "ORACLE_SYNTHETIC_GOLD_GROUPING_NOT_OPERATIONAL",
                        _ => throw new InvalidOperationException("Modo de agrupamento inválido.")
                    },
                    validation.Count, eligibleSources, trueSqlSources,
                    counter.Tp, counter.Fp, counter.Conflicts, counter.Unresolved,
                    (double)trueSqlSources / eligibleSources,
                    (double)counter.Tp / eligibleSources,
                    counter.Tp + counter.Fp == 0 ? null
                        : (double)counter.Fp / (counter.Tp + counter.Fp),
                    confidence.UpperFdr, orderedBases.Length,
                    counter.Reasons.OrderBy(x => x.Key, StringComparer.Ordinal)
                        .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
                    SyntheticGoldOracleGrouping: mode == 2,
                    OperationallyCertified: false,
                    ImpureBlindGroups: mode == 1 ? impureBlindGroups : 0,
                    ConditionalClusterCpFdrUpper: cp.UpperFdrConditional,
                    ClopperPearsonUpperAnyFpByIdentity:
                        cp.UpperProbabilityOfAnyGroupErrorClopperPearson,
                    BonferroniComparedConfigurations: predeclaredComparedConfigurations,
                    ClusterIidIndependentlyValidated: false));
            }
        }
        return Array.AsReadOnly(metrics.ToArray());
    }

    private sealed class V9SourceResolveAccumulator
    {
        internal long Tp { get; private set; }
        internal long Fp { get; private set; }
        internal long Conflicts { get; private set; }
        internal long Unresolved { get; private set; }
        internal long Total => Tp + Fp + Conflicts + Unresolved;
        internal Dictionary<string, (long Tp, long Fp)> GroupContributions { get; }
        internal Dictionary<string, long> Reasons { get; } = new(StringComparer.Ordinal);

        internal V9SourceResolveAccumulator(IEnumerable<string> baseIds)
        {
            GroupContributions = baseIds.ToDictionary(
                id => id, _ => (Tp: 0L, Fp: 0L), StringComparer.Ordinal);
        }

        internal void Add(string sourceBase, string? targetBase,
            ProbabilisticLinkageDecision decision)
        {
            var reason = decision.Motivo ?? decision.Status.ToString();
            Reasons[reason] = checked(Reasons.GetValueOrDefault(reason) + 1);
            switch (decision.Status)
            {
                case ResolutionStatus.CONFLITO:
                    Conflicts++;
                    break;
                case ResolutionStatus.NAO_RESOLVIDO:
                    Unresolved++;
                    break;
                case ResolutionStatus.RESOLVIDO:
                    if (targetBase is null)
                        throw new InvalidDataException("Vínculo sem grupo alvo.");
                    var entry = GroupContributions[sourceBase];
                    if (sourceBase == targetBase)
                    {
                        Tp++;
                        entry.Tp++;
                    }
                    else
                    {
                        Fp++;
                        entry.Fp++;
                    }
                    GroupContributions[sourceBase] = entry;
                    break;
                default:
                    throw new InvalidDataException("Status Resolve desconhecido.");
            }
        }
    }

    public static string ConfigurationFingerprintSha256(V9FsAdaptiveOptionConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var canonical = string.Join("|",
            Version, config.OptionId,
            config.SmoothingAlpha.ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.DecisionThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "TF_WEIGHT=" + config.TfWeight.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "TF_SIDE=" + config.TfSide,
            "TF_MIN_FREQ=" + config.TfMinimumFrequency.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "TF_SUPPORT=" + config.TfMinimumIndependentSupport);
        if (config.JointSupportPolicy is not null) canonical += "|JOINT_SUPPORT|" + System.Text.Json.JsonSerializer.Serialize(config.JointSupportPolicy);
        return Sha(canonical);
    }

    private void ValidateFrame()
    {
        var validPartitions = new HashSet<string>(["TRAIN", "VALIDATION"], StringComparer.Ordinal);
        if (frame.Pairs.Any(x => x is null
                || !validPartitions.Contains(x.Partition)
                || string.IsNullOrWhiteSpace(x.LeftIdentityGroupId)
                || string.IsNullOrWhiteSpace(x.RightIdentityGroupId)
                || x.CandidateDegreeForSource <= 0
                || (x.IsTruePair && x.LeftIdentityGroupId != x.RightIdentityGroupId)
                || (!x.IsTruePair && x.LeftIdentityGroupId == x.RightIdentityGroupId)))
            throw new InvalidDataException("Frame FS V9 contém partição/grupo/fanout inválido.");
        var trainGroups = PartitionIdentityGroups("TRAIN");
        var validationGroups = PartitionIdentityGroups("VALIDATION");
        if (trainGroups.Count < 2 || validationGroups.Count < 2 || trainGroups.Overlaps(validationGroups))
            throw new InvalidDataException("TRAIN/VALIDATION devem ser disjuntos por identidade-base.");
        if (!IdentityGroups("TRAIN").IsSubsetOf(trainGroups)
            || !IdentityGroups("VALIDATION").IsSubsetOf(validationGroups))
            throw new InvalidDataException("Pares SQL contêm identidade ausente do universo pré-blocking.");
    }

    private void ValidateSlice(
        AdaptiveCalibrationSlice slice, string partition, string corpusSha, string identitySha)
    {
        if (slice is null || slice.Partition != partition || slice.UsesTestLabels
            || !slice.IndependentBaseIdentityPartitionAttested
            || slice.CorpusFingerprintSha256 != corpusSha
            || slice.IndependentIdentityPartitionFingerprintSha256 != identitySha)
            throw new InvalidDataException("Slice adaptativo não coincide com o frame FS V9 congelado.");

        var groups = PartitionIdentityGroups(partition).Count;
        if (slice.DistinctIdentityGroups != groups)
            throw new InvalidDataException("Contagem de grupos independentes diverge do frame FS V9.");
    }

    private static void RequireClassSupport(V9FsAdaptiveCandidatePair[] rows, string label)
    {
        if (!HasClassSupport(rows))
            throw new InvalidDataException($"TRAIN {label} exige suporte de ao menos duas identidades.");
    }

    internal static bool HasClassSupport(V9FsAdaptiveCandidatePair[] rows)
    {
        var identities = rows.SelectMany(x => new[]
        {
            x.LeftIdentityGroupId, x.RightIdentityGroupId
        }).Distinct().Count();
        return rows.Length >= 2 && identities >= 2;
    }

    private HashSet<string> PartitionIdentityGroups(string partition)
    {
        if ((frame.TrainPreBlockingBaseIdentityIds is null)
            != (frame.ValidationPreBlockingBaseIdentityIds is null))
            throw new InvalidDataException("Censos pré-blocking TRAIN/VALIDATION devem ser informados juntos.");
        var snapshot = partition switch
        {
            "TRAIN" => frame.TrainPreBlockingBaseIdentityIds,
            "VALIDATION" => frame.ValidationPreBlockingBaseIdentityIds,
            _ => throw new InvalidDataException("Partição FS inválida.")
        };
        if (snapshot is null)
            return IdentityGroups(partition); // Legacy candidate-only unit fixtures.
        var unique = snapshot.ToHashSet(StringComparer.Ordinal);
        if (unique.Count != snapshot.Count
            || unique.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Censo pré-blocking duplicado ou com grupos vazios.");
        return unique;
    }

    private HashSet<string> IdentityGroups(string partition)
    {
        var groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in frame.Pairs.Where(x => x.Partition == partition))
        {
            groups.Add(pair.LeftIdentityGroupId);
            groups.Add(pair.RightIdentityGroupId);
        }
        return groups;
    }

    private static Dictionary<(CompositeNominalLevelV9 Person, CompositeNominalLevelV9 Mother), int>
        CountNominal(IEnumerable<V9FsAdaptiveCandidatePair> rows)
    {
        var result = new Dictionary<(CompositeNominalLevelV9, CompositeNominalLevelV9), int>();
        foreach (var row in rows)
        {
            var key = (
                CompositeNominalComparisonV9.Classify(row.LeftPersonName, row.RightPersonName),
                CompositeNominalComparisonV9.Classify(row.LeftMotherName, row.RightMotherName));
            result[key] = checked(result.GetValueOrDefault(key) + 1);
        }
        return result;
    }

    private static (int Exact, int Diff, int Total) BirthCounts(
        IEnumerable<V9FsAdaptiveCandidatePair> rows)
    {
        var exact = 0;
        var diff = 0;
        foreach (var row in rows)
        {
            if (row.LeftBirthDate is not { } left || row.RightBirthDate is not { } right)
                continue;
            if (left == right) exact++;
            else diff++;
        }
        return (exact, diff, exact + diff);
    }

    private static string FittedFingerprintSha256(
        V9FsAdaptiveOptionConfiguration config,
        IReadOnlyList<FellegiSunterJointNominalCellV9> cells,
        IReadOnlyDictionary<string, decimal> parameters,
        string schemaSha,
        string trainCorpusSha,
        string trainIdentitySha)
    {
        var sb = new StringBuilder()
            .Append(Version).Append('\n')
            .Append(config.FingerprintSha256).Append('\n')
            .Append(schemaSha).Append('\n')
            .Append(trainCorpusSha).Append('\n')
            .Append(trainIdentitySha).Append('\n');
        foreach (var cell in cells.OrderBy(x => x.Person).ThenBy(x => x.Mother))
            sb.Append(cell.Person).Append('|').Append(cell.Mother).Append('|')
                .Append(cell.M.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                .Append(cell.U.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        foreach (var pair in parameters.OrderBy(x => x.Key, StringComparer.Ordinal))
            sb.Append(pair.Key).Append('=')
                .Append(pair.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        return Sha(sb.ToString());
    }

    private static double PercentileNearestRank(double[] sorted, double p)
    {
        var rank = Math.Max(1, (int)Math.Ceiling(p * sorted.Length));
        return sorted[Math.Min(sorted.Length - 1, rank - 1)];
    }

    private static string Sha(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void RequireSha(string? sha, string label)
    {
        if (sha is null || sha.Length != 64 || sha.Any(x => !Uri.IsHexDigit(x)))
            throw new ArgumentException("SHA-256 obrigatório: " + label);
    }
}
