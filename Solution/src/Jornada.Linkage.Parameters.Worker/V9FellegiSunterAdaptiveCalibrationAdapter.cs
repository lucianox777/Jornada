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
    IReadOnlyCollection<string>? ValidationPreBlockingBaseIdentityIds = null);

public sealed record V9FsAdaptiveOptionConfiguration(
    string OptionId,
    decimal SmoothingAlpha,
    decimal DecisionThreshold,
    decimal TfWeight = 0m,
    CompositeNominalTfSideV9 TfSide = CompositeNominalTfSideV9.Person,
    decimal TfMinimumFrequency = 0.000001m,
    long TfMinimumIndependentSupport = 1)
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
        V9TrainSqlFirstNameUCalibration.Frozen? TrainUnitU);

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
        if (frame.TrainCorpusFingerprintSha256 == frame.ValidationCorpusFingerprintSha256
            || frame.TrainIdentityFingerprintSha256 == frame.ValidationIdentityFingerprintSha256)
            throw new InvalidDataException("TRAIN e VALIDATION precisam de fingerprints distintos.");
        if (frame.Pairs is null || frame.Pairs.Count == 0)
            throw new ArgumentException("Frame candidato vazio.");
        // A lista fornecida pelo chamador pode ser mutável; congele um snapshot antes de validar.
        // Os pares são records com campos imutáveis (strings, DateOnly e valores escalares).
        this.frame = frame with
        {
            Pairs = Array.AsReadOnly(frame.Pairs.ToArray()),
            TrainPreBlockingBaseIdentityIds = frame.TrainPreBlockingBaseIdentityIds is null
                ? null : Array.AsReadOnly(frame.TrainPreBlockingBaseIdentityIds.ToArray()),
            ValidationPreBlockingBaseIdentityIds = frame.ValidationPreBlockingBaseIdentityIds is null
                ? null : Array.AsReadOnly(frame.ValidationPreBlockingBaseIdentityIds.ToArray())
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
        this.options = byId.ToDictionary(x => x.OptionId, StringComparer.Ordinal);
        this.frozenTfReference = frozenTfReference;

        ValidateFrame();
    }

    public string ModelFamily => Family;
    public string AdapterVersion => Version;

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
        fits[fitSha] = new FrozenFit(fitSha, config.OptionId, config.DecisionThreshold,
            Array.AsReadOnly(cells.ToArray()),
            new Dictionary<string, decimal>(parameters, StringComparer.Ordinal), trainU);

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
            var score = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                frozen.Parameters,
                row.LeftPersonName, row.RightPersonName,
                row.LeftMotherName, row.RightMotherName,
                frozen.Cells, CompositeNominalComparisonV9.ContractVersion,
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
            if (left >= right || !validation.ContainsKey(left)
                || !validation.ContainsKey(right)
                || validation[left].Gestor == validation[right].Gestor
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
        var identities = rows.SelectMany(x => new[]
        {
            x.LeftIdentityGroupId, x.RightIdentityGroupId
        }).Distinct().Count();
        if (rows.Length < 2 || identities < 2)
            throw new InvalidDataException($"TRAIN {label} exige suporte de ao menos duas identidades.");
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
