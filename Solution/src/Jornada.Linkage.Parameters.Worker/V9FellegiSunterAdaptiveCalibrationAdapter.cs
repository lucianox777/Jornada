using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Par bruto do frame candidato probabilístico já materializado pelo blocking.
/// Não contém CPF: este adaptador é somente para o ramo sem CPF.
/// TRAIN e VALIDATION devem ser disjuntos por IdentityGroupId.
/// </summary>
public sealed record V9FsAdaptiveCandidatePair(
    string Partition,
    Guid IdentityGroupId,
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
    IReadOnlyList<V9FsAdaptiveCandidatePair> Pairs);

public sealed record V9FsAdaptiveOptionConfiguration(
    string OptionId,
    decimal SmoothingAlpha,
    decimal DecisionThreshold)
{
    public string FingerprintSha256 => V9FellegiSunterAdaptiveCalibrationAdapter
        .ConfigurationFingerprintSha256(this);
}

/// <summary>
/// Adaptador real do coordenador adaptativo para o scorer Fellegi-Sunter V9 C#.
/// Ele estima 81 células conjuntas, nascimento binário e prior SOMENTE em TRAIN,
/// congela o fit em memória pelo fingerprint e avalia o MESMO fit em VALIDATION.
/// Não lê TEST, não persiste/ativa modelo e, nesta V1, não aplica TF: peso TF
/// diferente de zero não é representado nem inferido silenciosamente.
/// </summary>
public sealed class V9FellegiSunterAdaptiveCalibrationAdapter : IAdaptiveCalibrationFamilyAdapter
{
    public const string Family = "FELLEGI_SUNTER_V9";
    public const string Version = "V9_FS_ADAPTIVE_REAL_SCORER_TRAIN_VALIDATION_V1";

    private sealed record FrozenFit(
        string FingerprintSha256,
        string OptionId,
        decimal Threshold,
        IReadOnlyList<FellegiSunterJointNominalCellV9> Cells,
        IReadOnlyDictionary<string, decimal> Parameters);

    private readonly V9FsAdaptiveCandidateFrame frame;
    private readonly IReadOnlyDictionary<string, V9FsAdaptiveOptionConfiguration> options;
    private readonly Dictionary<string, FrozenFit> fits = new(StringComparer.Ordinal);

    public V9FellegiSunterAdaptiveCalibrationAdapter(
        V9FsAdaptiveCandidateFrame frame,
        IEnumerable<V9FsAdaptiveOptionConfiguration> options)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(options);
        this.frame = frame;
        RequireSha(frame.TrainCorpusFingerprintSha256, "TRAIN corpus");
        RequireSha(frame.ValidationCorpusFingerprintSha256, "VALIDATION corpus");
        RequireSha(frame.TrainIdentityFingerprintSha256, "TRAIN identities");
        RequireSha(frame.ValidationIdentityFingerprintSha256, "VALIDATION identities");
        if (frame.TrainCorpusFingerprintSha256 == frame.ValidationCorpusFingerprintSha256
            || frame.TrainIdentityFingerprintSha256 == frame.ValidationIdentityFingerprintSha256)
            throw new InvalidDataException("TRAIN e VALIDATION precisam de fingerprints distintos.");
        if (frame.Pairs is null || frame.Pairs.Count == 0)
            throw new ArgumentException("Frame candidato vazio.");

        var byId = options.ToArray();
        if (byId.Length is < 1 or > 64
            || byId.Any(x => x is null || string.IsNullOrWhiteSpace(x.OptionId)
                || x.SmoothingAlpha <= 0m || x.SmoothingAlpha > 1000m
                || x.DecisionThreshold <= 0m || x.DecisionThreshold >= 1m)
            || byId.Select(x => x.OptionId).Distinct(StringComparer.Ordinal).Count() != byId.Length)
            throw new ArgumentException("Opções FS V9 inválidas ou duplicadas.");
        this.options = byId.ToDictionary(x => x.OptionId, StringComparer.Ordinal);

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

        var fitSha = FittedFingerprintSha256(config, cells, parameters, schema.FingerprintSha256);
        fits[fitSha] = new FrozenFit(fitSha, config.OptionId, config.DecisionThreshold,
            Array.AsReadOnly(cells.ToArray()),
            new Dictionary<string, decimal>(parameters, StringComparer.Ordinal));

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
            || !fitted.TrainOnly || !fits.TryGetValue(fitted.FittedParametersFingerprintSha256, out var frozen)
            || frozen.OptionId != fitted.OptionId)
            throw new InvalidDataException("Fit V9 desconhecido ou não congelado por este adaptador.");

        var rows = frame.Pairs.Where(x => x.Partition == "VALIDATION").ToArray();
        var truePairs = rows.Count(x => x.IsTruePair);
        var falsePairs = rows.Length - truePairs;
        if (truePairs <= 0 || falsePairs <= 0)
            throw new InvalidDataException("VALIDATION precisa conter M e U.");

        long tp = 0, fp = 0;
        foreach (var row in rows)
        {
            var score = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                frozen.Parameters,
                row.LeftPersonName, row.RightPersonName,
                row.LeftMotherName, row.RightMotherName,
                frozen.Cells, CompositeNominalComparisonV9.ContractVersion,
                leftBirthDate: row.LeftBirthDate, rightBirthDate: row.RightBirthDate);
            var accepted = score.Score.Posterior >= frozen.Threshold;
            if (accepted && row.IsTruePair) tp++;
            else if (accepted) fp++;
        }

        var falseDegrees = rows.Where(x => !x.IsTruePair)
            .Select(x => (double)x.CandidateDegreeForSource)
            .OrderBy(x => x).ToArray();
        var p99 = falseDegrees.Length == 0 ? 0d : PercentileNearestRank(falseDegrees, .99d);
        var groups = rows.Select(x => x.IdentityGroupId).Distinct().Count();

        return new AdaptiveCalibrationHoldoutMetrics(
            TruePairsWithoutCpf: truePairs,
            TrueAcceptedLinks: tp,
            FalseAcceptedLinks: fp,
            Abstentions: truePairs - tp,
            BlockingFalseCandidatePairs: falsePairs,
            BlockingFalseDegreeP99: p99,
            IndependentlyLabeledIdentityGroups: groups);
    }

    public static string ConfigurationFingerprintSha256(V9FsAdaptiveOptionConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var canonical = string.Join("|",
            Version, config.OptionId,
            config.SmoothingAlpha.ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.DecisionThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "TF=DISABLED_V1");
        return Sha(canonical);
    }

    private void ValidateFrame()
    {
        var validPartitions = new HashSet<string>(["TRAIN", "VALIDATION"], StringComparer.Ordinal);
        if (frame.Pairs.Any(x => x is null
                || !validPartitions.Contains(x.Partition)
                || x.IdentityGroupId == Guid.Empty
                || x.CandidateDegreeForSource <= 0))
            throw new InvalidDataException("Frame FS V9 contém partição/grupo/fanout inválido.");
        var trainGroups = frame.Pairs.Where(x => x.Partition == "TRAIN")
            .Select(x => x.IdentityGroupId).ToHashSet();
        var validationGroups = frame.Pairs.Where(x => x.Partition == "VALIDATION")
            .Select(x => x.IdentityGroupId).ToHashSet();
        if (trainGroups.Count < 2 || validationGroups.Count < 2 || trainGroups.Overlaps(validationGroups))
            throw new InvalidDataException("TRAIN/VALIDATION devem ser disjuntos por identidade-base.");
    }

    private void ValidateSlice(
        AdaptiveCalibrationSlice slice, string partition, string corpusSha, string identitySha)
    {
        if (slice is null || slice.Partition != partition || slice.UsesTestLabels
            || !slice.IndependentBaseIdentityPartitionAttested
            || slice.CorpusFingerprintSha256 != corpusSha
            || slice.IndependentIdentityPartitionFingerprintSha256 != identitySha)
            throw new InvalidDataException("Slice adaptativo não coincide com o frame FS V9 congelado.");

        var groups = frame.Pairs.Where(x => x.Partition == partition)
            .Select(x => x.IdentityGroupId).Distinct().Count();
        if (slice.DistinctIdentityGroups != groups)
            throw new InvalidDataException("Contagem de grupos independentes diverge do frame FS V9.");
    }

    private static void RequireClassSupport(V9FsAdaptiveCandidatePair[] rows, string label)
    {
        if (rows.Length < 2 || rows.Select(x => x.IdentityGroupId).Distinct().Count() < 2)
            throw new InvalidDataException($"TRAIN {label} exige dois grupos independentes.");
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
        string schemaSha)
    {
        var sb = new StringBuilder()
            .Append(Version).Append('\n')
            .Append(config.FingerprintSha256).Append('\n')
            .Append(schemaSha).Append('\n');
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
