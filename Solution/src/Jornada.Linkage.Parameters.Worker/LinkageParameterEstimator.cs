using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record IdentityTrainingPair(
    string LeftName,
    DateOnly LeftBirthDate,
    string? LeftMotherName,
    string RightName,
    DateOnly RightBirthDate,
    string? RightMotherName,
    string? LeftSourceCode = null,
    string? RightSourceCode = null,
    IReadOnlyList<ResolutionSourceValue>? LeftResolutionValues = null,
    IReadOnlyList<ResolutionSourceValue>? RightResolutionValues = null);

public enum BirthScoringContract
{
    SingleEvidenceV3,
    JointEvidenceV4
}

/// <summary>
/// Estima parâmetros m/u do baseline Fellegi-Sunter a partir de dois conjuntos:
/// pares verdadeiros formados por observações independentes de Gestores distintos que
/// convergiram deterministicamente por CPF ao mesmo UUID e pares não-match amostrados da Gold.
/// A independência inter-Gestor evita treinar m contra a própria Gold derivada da observação.
/// Usa suavização de Dirichlet/Laplace para impedir pesos infinitos.
///
/// Nome da mãe é anulável no contrato Pessoa v3. Ausência em qualquer lado não é
/// discordância: esses pares ficam fora da distribuição NOME_MAE e a ausência será
/// tratada como evidência neutra pelo scorer.
///
/// A V4 representa nascimento como uma única evidência conjunta de 8 estados (dia/mês/ano),
/// evitando tanto a perda de informação do EXACT|DIFF quanto a tripla contagem independente.
/// O contrato de ativação é explícito para permitir replay/validação do piloto V3 sem misturar
/// flags. As distribuições V4, V3 e V2 continuam materializadas para replay e diagnóstico.
/// </summary>
public static class LinkageParameterEstimator
{
    private static readonly NameComparisonState[] States = Enum.GetValues<NameComparisonState>();

    public static IReadOnlyDictionary<string, decimal> Estimate(
        IReadOnlyList<IdentityTrainingPair> matchedPairs,
        IReadOnlyList<IdentityTrainingPair> unmatchedPairs,
        long populationSize,
        long distinctBirthDates,
        decimal smoothingAlpha,
        decimal threshold,
        decimal conflictMargin,
        BirthScoringContract birthScoringContract = BirthScoringContract.JointEvidenceV4)
    {
        if (matchedPairs.Count == 0)
            throw new InvalidOperationException("Não há pares determinísticos suficientes para estimar probabilidades m.");
        if (unmatchedPairs.Count == 0)
            throw new InvalidOperationException("Não há pares não-match suficientes para estimar probabilidades u.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(smoothingAlpha);
        if (!Enum.IsDefined(birthScoringContract))
            throw new ArgumentOutOfRangeException(nameof(birthScoringContract));

        var matchedMotherStates = PresentMotherNameComparisons(matchedPairs).ToArray();
        var unmatchedMotherStates = PresentMotherNameComparisons(unmatchedPairs).ToArray();
        var matchedBirthStates = matchedPairs.Select(BirthAgreementMask).ToArray();
        var unmatchedBirthStates = unmatchedPairs.Select(BirthAgreementMask).ToArray();

        var result = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["M_SAMPLE_SIZE"] = matchedPairs.Count,
            ["U_SAMPLE_SIZE"] = unmatchedPairs.Count,
            ["SMOOTHING_ALPHA"] = smoothingAlpha,
            ["T_LINKAGE"] = threshold,
            ["CONFLICT_MARGIN"] = conflictMargin,
            [birthScoringContract == BirthScoringContract.JointEvidenceV4
                ? LinkageParameterCatalog.BirthJointEvidenceScoring
                : LinkageParameterCatalog.BirthSingleEvidenceScoring] = 1m,
            ["PRIOR_MATCH_PROBABILITY"] = EstimateReferencePrior(populationSize, distinctBirthDates),
            ["PRIOR_BLOCK_MIN"] = 0.000001m,
            ["PRIOR_BLOCK_MAX"] = 0.25m
        };

        AddDistribution(result, "M_NOME", matchedPairs.Select(p => IdentityComparison.CompareName(p.LeftName, p.RightName)), smoothingAlpha);
        AddDistribution(result, "U_NOME", unmatchedPairs.Select(p => IdentityComparison.CompareName(p.LeftName, p.RightName)), smoothingAlpha);
        AddDistribution(result, "M_NOME_MAE", matchedMotherStates, smoothingAlpha);
        AddDistribution(result, "U_NOME_MAE", unmatchedMotherStates, smoothingAlpha);

        AddJointBirthDistribution(result, "M_NASCIMENTO_CONJUNTO", matchedBirthStates, smoothingAlpha);
        AddJointBirthDistribution(result, "U_NASCIMENTO_CONJUNTO", unmatchedBirthStates, smoothingAlpha);
        AddJointBirthSupport(result, "SUPPORT_M_NASCIMENTO_CONJUNTO", matchedBirthStates);
        AddJointBirthSupport(result, "SUPPORT_U_NASCIMENTO_CONJUNTO", unmatchedBirthStates);

        AddBinaryDistribution(result, "M_DATA_NASCIMENTO", matchedPairs.Select(p => p.LeftBirthDate == p.RightBirthDate), smoothingAlpha);
        AddBinaryDistribution(result, "U_DATA_NASCIMENTO", unmatchedPairs.Select(p => p.LeftBirthDate == p.RightBirthDate), smoothingAlpha);

        AddBinaryDistribution(result, "M_NASC_DIA", matchedPairs.Select(p => p.LeftBirthDate.Day == p.RightBirthDate.Day), smoothingAlpha);
        AddBinaryDistribution(result, "U_NASC_DIA", unmatchedPairs.Select(p => p.LeftBirthDate.Day == p.RightBirthDate.Day), smoothingAlpha);
        AddBinaryDistribution(result, "M_NASC_MES", matchedPairs.Select(p => p.LeftBirthDate.Month == p.RightBirthDate.Month), smoothingAlpha);
        AddBinaryDistribution(result, "U_NASC_MES", unmatchedPairs.Select(p => p.LeftBirthDate.Month == p.RightBirthDate.Month), smoothingAlpha);
        AddBinaryDistribution(result, "M_NASC_ANO", matchedPairs.Select(p => p.LeftBirthDate.Year == p.RightBirthDate.Year), smoothingAlpha);
        AddBinaryDistribution(result, "U_NASC_ANO", unmatchedPairs.Select(p => p.LeftBirthDate.Year == p.RightBirthDate.Year), smoothingAlpha);

        return result;
    }

    private static IEnumerable<NameComparisonState> PresentMotherNameComparisons(IEnumerable<IdentityTrainingPair> pairs)
    {
        foreach (var pair in pairs)
        {
            if (IdentityComparison.NormalizeText(pair.LeftMotherName) is null ||
                IdentityComparison.NormalizeText(pair.RightMotherName) is null)
                continue;

            yield return IdentityComparison.CompareName(pair.LeftMotherName, pair.RightMotherName);
        }
    }

    private static void AddDistribution(
        IDictionary<string, decimal> target,
        string prefix,
        IEnumerable<NameComparisonState> values,
        decimal alpha)
    {
        var counts = States.ToDictionary(s => s, _ => 0L);
        long total = 0;
        foreach (var state in values)
        {
            counts[state]++;
            total++;
        }

        var denominator = total + alpha * States.Length;
        foreach (var state in States)
            target[$"{prefix}_{state}"] = (counts[state] + alpha) / denominator;
    }

    private static void AddJointBirthDistribution(
        IDictionary<string, decimal> target,
        string prefix,
        IEnumerable<byte> values,
        decimal alpha)
    {
        var counts = new long[LinkageParameterCatalog.BirthJointStates.Count];
        long total = 0;
        foreach (var value in values)
        {
            if (value >= counts.Length)
                throw new InvalidOperationException("Estado conjunto de nascimento inválido.");
            counts[value]++;
            total++;
        }

        var denominator = total + alpha * counts.Length;
        for (var i = 0; i < counts.Length; i++)
            target[$"{prefix}_{LinkageParameterCatalog.BirthJointStates[i]}"] = (counts[i] + alpha) / denominator;
    }

    private static void AddJointBirthSupport(
        IDictionary<string, decimal> target,
        string prefix,
        IEnumerable<byte> values)
    {
        var counts = new long[LinkageParameterCatalog.BirthJointStates.Count];
        foreach (var value in values)
        {
            if (value >= counts.Length)
                throw new InvalidOperationException("Estado conjunto de nascimento inválido.");
            counts[value]++;
        }

        for (var i = 0; i < counts.Length; i++)
            target[$"{prefix}_{LinkageParameterCatalog.BirthJointStates[i]}"] = counts[i];
    }

    private static byte BirthAgreementMask(IdentityTrainingPair pair)
    {
        var mask = (pair.LeftBirthDate.Day == pair.RightBirthDate.Day ? 1 : 0) |
            (pair.LeftBirthDate.Month == pair.RightBirthDate.Month ? 2 : 0) |
            (pair.LeftBirthDate.Year == pair.RightBirthDate.Year ? 4 : 0);
        return (byte)mask;
    }

    private static void AddBinaryDistribution(
        IDictionary<string, decimal> target,
        string prefix,
        IEnumerable<bool> values,
        decimal alpha)
    {
        long exact = 0;
        long total = 0;
        foreach (var value in values)
        {
            if (value)
                exact++;
            total++;
        }

        var exactProbability = SmoothedBinary(exact, total, alpha);
        target[$"{prefix}_EXACT"] = exactProbability;
        target[$"{prefix}_DIFF"] = 1m - exactProbability;
    }

    private static decimal SmoothedBinary(long positive, long total, decimal alpha) =>
        (positive + alpha) / (total + 2m * alpha);

    private static decimal EstimateReferencePrior(long populationSize, long distinctBirthDates)
    {
        if (populationSize <= 0 || distinctBirthDates <= 0)
            return 0.001m;

        var prior = (decimal)distinctBirthDates / populationSize;
        return Math.Clamp(prior, 0.000001m, 0.25m);
    }
}
