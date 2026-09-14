using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public enum LinkageParameterStage
{
    PRIOR_BOOTSTRAP,
    ESTIMADO,
    HOMOLOGADO
}

public sealed record BootstrapMProfile(
    decimal NameExact = 0.90m,
    decimal NameHigh = 0.07m,
    decimal NameMedium = 0.02m,
    decimal NameLow = 0.01m,
    decimal MotherExact = 0.90m,
    decimal MotherHigh = 0.07m,
    decimal MotherMedium = 0.02m,
    decimal MotherLow = 0.01m,
    decimal BirthExact = 0.95m)
{
    public void Validate()
    {
        ValidateDistribution(NameExact, NameHigh, NameMedium, NameLow, nameof(NameExact));
        ValidateDistribution(MotherExact, MotherHigh, MotherMedium, MotherLow, nameof(MotherExact));
        if (BirthExact <= 0m || BirthExact >= 1m)
            throw new ArgumentOutOfRangeException(nameof(BirthExact));
    }

    private static void ValidateDistribution(decimal exact, decimal high, decimal medium, decimal low, string name)
    {
        if (new[] { exact, high, medium, low }.Any(x => x <= 0m || x >= 1m))
            throw new ArgumentOutOfRangeException(name, "Probabilidades bootstrap devem estar estritamente entre zero e um.");
        if (Math.Abs(exact + high + medium + low - 1m) > 0.00000001m)
            throw new ArgumentException("Distribuição bootstrap deve somar 1.", name);
    }
}

public sealed record NameFrequencyCount(string ValueNormalized, long Frequency);

public sealed record BootstrapLinkageParameterSet(
    LinkageParameterStage Stage,
    IReadOnlyDictionary<string, decimal> Parameters,
    string MOrigin,
    string UNameOrigin,
    bool EligibleForAutomaticActivation,
    long PublishedNameFrequencyMass);

/// <summary>
/// Cold start auditável do Fellegi-Sunter.
/// m não é estimado pelo IBGE. Frequências marginais do IBGE alimentam somente u de
/// concordância exata por colisão; fuzzy e nome da mãe permanecem priors/proxies marcados.
/// </summary>
public static class BootstrapLinkageParameterEstimator
{
    public static BootstrapLinkageParameterSet Estimate(
        IReadOnlyCollection<NameFrequencyCount> ibgeNames,
        long populationSize,
        long distinctBirthDates,
        decimal threshold,
        decimal conflictMargin,
        BootstrapMProfile? mPrior = null,
        decimal uNameHighPrior = 0.01m,
        decimal uNameMediumPrior = 0.04m)
    {
        if (ibgeNames.Count == 0)
            throw new InvalidOperationException("Bootstrap exige referência IBGE de NOME não vazia.");
        if (ibgeNames.Any(x => x.Frequency <= 0 || string.IsNullOrWhiteSpace(x.ValueNormalized)))
            throw new InvalidOperationException("Referência IBGE contém frequência/nome inválido.");

        var publishedMass = ibgeNames.Sum(x => checked(x.Frequency));
        var collision = ExactCollisionProbability(ibgeNames, publishedMass);
        return EstimateFromIbgeCollision(
            collision, publishedMass, populationSize, distinctBirthDates,
            threshold, conflictMargin, mPrior, uNameHighPrior, uNameMediumPrior);
    }

    public static BootstrapLinkageParameterSet EstimateFromIbgeCollision(
        decimal exactNameCollision,
        long publishedNameFrequencyMass,
        long populationSize,
        long distinctBirthDates,
        decimal threshold,
        decimal conflictMargin,
        BootstrapMProfile? mPrior = null,
        decimal uNameHighPrior = 0.01m,
        decimal uNameMediumPrior = 0.04m)
    {
        if (exactNameCollision <= 0m || exactNameCollision >= 1m)
            throw new ArgumentOutOfRangeException(nameof(exactNameCollision));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(publishedNameFrequencyMass);
        if (uNameHighPrior <= 0m || uNameMediumPrior <= 0m)
            throw new ArgumentOutOfRangeException(nameof(uNameHighPrior));

        mPrior ??= new BootstrapMProfile();
        mPrior.Validate();

        var uLow = 1m - exactNameCollision - uNameHighPrior - uNameMediumPrior;
        if (uLow <= 0m)
            throw new InvalidOperationException("Priors fuzzy de u consomem toda a massa disponível.");

        var birthUExact = distinctBirthDates > 0
            ? Math.Clamp(1m / distinctBirthDates, 0.00000001m, 0.25m)
            : 1m / 36525m;
        var priorMatch = populationSize > 0 && distinctBirthDates > 0
            ? Math.Clamp((decimal)distinctBirthDates / populationSize, 0.000001m, 0.25m)
            : 0.001m;

        var p = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["PARAMETER_STAGE_PRIOR_BOOTSTRAP"] = 1m,
            ["AUTO_PROMOTION_ALLOWED"] = 0m,
            ["M_SAMPLE_SIZE"] = 0m,
            ["M_ORIGIN_PRIOR_INSTITUCIONAL"] = 1m,
            ["U_NOME_ORIGIN_IBGE_COLLISION"] = 1m,
            ["IBGE_NAME_PUBLISHED_MASS"] = publishedNameFrequencyMass,
            ["T_LINKAGE"] = threshold,
            ["CONFLICT_MARGIN"] = conflictMargin,
            ["PRIOR_MATCH_PROBABILITY"] = priorMatch,
            ["PRIOR_BLOCK_MIN"] = 0.000001m,
            ["PRIOR_BLOCK_MAX"] = 0.25m,
            ["M_NOME_EXACT"] = mPrior.NameExact,
            ["M_NOME_HIGH"] = mPrior.NameHigh,
            ["M_NOME_MEDIUM"] = mPrior.NameMedium,
            ["M_NOME_LOW"] = mPrior.NameLow,
            ["M_NOME_MAE_EXACT"] = mPrior.MotherExact,
            ["M_NOME_MAE_HIGH"] = mPrior.MotherHigh,
            ["M_NOME_MAE_MEDIUM"] = mPrior.MotherMedium,
            ["M_NOME_MAE_LOW"] = mPrior.MotherLow,
            ["U_NOME_EXACT"] = exactNameCollision,
            ["U_NOME_HIGH"] = uNameHighPrior,
            ["U_NOME_MEDIUM"] = uNameMediumPrior,
            ["U_NOME_LOW"] = uLow,
            ["U_NOME_MAE_EXACT"] = exactNameCollision,
            ["U_NOME_MAE_HIGH"] = uNameHighPrior,
            ["U_NOME_MAE_MEDIUM"] = uNameMediumPrior,
            ["U_NOME_MAE_LOW"] = uLow,
            ["U_NOME_MAE_IBGE_PROXY"] = 1m,
            ["SCORING_BIRTH_SINGLE_EVIDENCE_V3"] = 1m,
            ["M_DATA_NASCIMENTO_EXACT"] = mPrior.BirthExact,
            ["M_DATA_NASCIMENTO_DIFF"] = 1m - mPrior.BirthExact,
            ["U_DATA_NASCIMENTO_EXACT"] = birthUExact,
            ["U_DATA_NASCIMENTO_DIFF"] = 1m - birthUExact
        };

        return new BootstrapLinkageParameterSet(
            LinkageParameterStage.PRIOR_BOOTSTRAP,
            p,
            "PRIOR_INSTITUCIONAL",
            "IBGE_CENSO2022_COLISAO_EXATA_MASSA_PUBLICADA",
            false,
            publishedNameFrequencyMass);
    }

    public static decimal ExactCollisionProbability(
        IReadOnlyCollection<NameFrequencyCount> frequencies,
        long? denominator = null)
    {
        if (frequencies.Count == 0)
            throw new ArgumentException("Frequências vazias.", nameof(frequencies));

        var total = denominator ?? frequencies.Sum(x => checked(x.Frequency));
        if (total <= 0)
            throw new ArgumentOutOfRangeException(nameof(denominator));

        decimal numerator = 0m;
        foreach (var row in frequencies)
        {
            if (row.Frequency <= 0)
                throw new InvalidOperationException("Frequência deve ser positiva.");
            var p = (decimal)row.Frequency / total;
            numerator += p * p;
        }

        return Math.Clamp(numerator, 0.00000001m, 0.99999999m);
    }
}
