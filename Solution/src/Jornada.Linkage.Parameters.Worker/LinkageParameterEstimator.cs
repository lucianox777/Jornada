using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record IdentityTrainingPair(
    string LeftName,
    DateOnly LeftBirthDate,
    string LeftMotherName,
    string RightName,
    DateOnly RightBirthDate,
    string RightMotherName,
    string? LeftSourceCode = null,
    string? RightSourceCode = null);

/// <summary>
/// Estima parâmetros m/u do baseline Fellegi-Sunter a partir de dois conjuntos:
/// pares verdadeiros formados por observações independentes de Gestores distintos que
/// convergiram deterministicamente por CPF ao mesmo UUID e pares não-match amostrados da Gold.
/// A independência inter-Gestor evita treinar m contra a própria Gold derivada da observação.
/// Usa suavização de Dirichlet/Laplace para impedir pesos infinitos.
///
/// A partir da V2, nascimento é calibrado também em três evidências binárias separadas:
/// dia, mês e ano. A data completa continua preservada e sua concordância exata continua
/// registrada para auditoria/backward compatibility, mas o scorer V2 pode atribuir peso
/// independente a cada componente.
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
        decimal conflictMargin)
    {
        if (matchedPairs.Count == 0)
            throw new InvalidOperationException("Não há pares determinísticos suficientes para estimar probabilidades m.");
        if (unmatchedPairs.Count == 0)
            throw new InvalidOperationException("Não há pares não-match suficientes para estimar probabilidades u.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(smoothingAlpha);

        var result = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["M_SAMPLE_SIZE"] = matchedPairs.Count,
            ["U_SAMPLE_SIZE"] = unmatchedPairs.Count,
            ["SMOOTHING_ALPHA"] = smoothingAlpha,
            ["T_LINKAGE"] = threshold,
            ["CONFLICT_MARGIN"] = conflictMargin,
            // Isto seleciona o modelo de scoring, não o plano de blocking. O prefixo
            // SCORING evita misturar parâmetros Fellegi-Sunter com evidência de seleção
            // dos passes, que é persistida separadamente pelo Calibrador.
            ["SCORING_BIRTH_COMPONENTS_V2"] = 1m,
            ["PRIOR_MATCH_PROBABILITY"] = EstimateReferencePrior(populationSize, distinctBirthDates),
            ["PRIOR_BLOCK_MIN"] = 0.000001m,
            ["PRIOR_BLOCK_MAX"] = 0.25m
        };

        AddDistribution(result, "M_NOME", matchedPairs.Select(p => IdentityComparison.CompareName(p.LeftName, p.RightName)), smoothingAlpha);
        AddDistribution(result, "U_NOME", unmatchedPairs.Select(p => IdentityComparison.CompareName(p.LeftName, p.RightName)), smoothingAlpha);
        AddDistribution(result, "M_NOME_MAE", matchedPairs.Select(p => IdentityComparison.CompareName(p.LeftMotherName, p.RightMotherName)), smoothingAlpha);
        AddDistribution(result, "U_NOME_MAE", unmatchedPairs.Select(p => IdentityComparison.CompareName(p.LeftMotherName, p.RightMotherName)), smoothingAlpha);

        // Mantém a taxa da data completa para auditoria e compatibilidade com modelos V1.
        result["M_DATA_NASCIMENTO_EXACT"] = SmoothedBinary(
            matchedPairs.Count(p => p.LeftBirthDate == p.RightBirthDate), matchedPairs.Count, smoothingAlpha);
        result["U_DATA_NASCIMENTO_EXACT"] = SmoothedBinary(
            unmatchedPairs.Count(p => p.LeftBirthDate == p.RightBirthDate), unmatchedPairs.Count, smoothingAlpha);

        // V2: cada componente de nascimento é uma evidência Fellegi-Sunter própria.
        // Persistimos EXACT e DIFF explicitamente para que o modelo seja auditável.
        AddBinaryDistribution(
            result,
            "M_NASC_DIA",
            matchedPairs.Select(p => p.LeftBirthDate.Day == p.RightBirthDate.Day),
            smoothingAlpha);
        AddBinaryDistribution(
            result,
            "U_NASC_DIA",
            unmatchedPairs.Select(p => p.LeftBirthDate.Day == p.RightBirthDate.Day),
            smoothingAlpha);
        AddBinaryDistribution(
            result,
            "M_NASC_MES",
            matchedPairs.Select(p => p.LeftBirthDate.Month == p.RightBirthDate.Month),
            smoothingAlpha);
        AddBinaryDistribution(
            result,
            "U_NASC_MES",
            unmatchedPairs.Select(p => p.LeftBirthDate.Month == p.RightBirthDate.Month),
            smoothingAlpha);
        AddBinaryDistribution(
            result,
            "M_NASC_ANO",
            matchedPairs.Select(p => p.LeftBirthDate.Year == p.RightBirthDate.Year),
            smoothingAlpha);
        AddBinaryDistribution(
            result,
            "U_NASC_ANO",
            unmatchedPairs.Select(p => p.LeftBirthDate.Year == p.RightBirthDate.Year),
            smoothingAlpha);

        return result;
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

        // Referência global apenas para auditoria/monitoramento. O scorer operacional
        // condiciona o prior ao tamanho real do conjunto de candidatos observado.
        var prior = (decimal)distinctBirthDates / populationSize;
        return Math.Clamp(prior, 0.000001m, 0.25m);
    }
}
