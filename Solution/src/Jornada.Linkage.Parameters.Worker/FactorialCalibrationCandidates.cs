namespace Jornada.Linkage.Parameters.Worker;

public enum ParameterEstimatorKind
{
    Jornada,
    Splink
}

public sealed record ParameterEstimate(
    ParameterEstimatorKind Estimator,
    string Version,
    IReadOnlyDictionary<string, decimal> Values);

public sealed record FactorialCalibrationCandidate(
    string CandidateId,
    ParameterEstimatorKind MEstimator,
    ParameterEstimatorKind UEstimator,
    string MVersion,
    string UVersion,
    IReadOnlyDictionary<string, decimal> Parameters);

/// <summary>
/// Combina estimativas m e u de forma fatorial para avaliação end-to-end.
/// A seleção final ocorre pelo comportamento do candidato completo; não existe
/// vencedor por atributo nem preferência implícita por implementação.
/// </summary>
public static class FactorialCalibrationCandidateFactory
{
    public static IReadOnlyList<FactorialCalibrationCandidate> Create(
        IEnumerable<ParameterEstimate> mEstimates,
        IEnumerable<ParameterEstimate> uEstimates,
        IReadOnlyDictionary<string, decimal>? commonParameters = null)
    {
        ArgumentNullException.ThrowIfNull(mEstimates);
        ArgumentNullException.ThrowIfNull(uEstimates);

        var mItems = mEstimates.ToArray();
        var uItems = uEstimates.ToArray();
        if (mItems.Length == 0)
            throw new ArgumentException("Ao menos uma estimativa m é obrigatória.", nameof(mEstimates));
        if (uItems.Length == 0)
            throw new ArgumentException("Ao menos uma estimativa u é obrigatória.", nameof(uEstimates));

        ValidateUniqueEstimator(mItems, nameof(mEstimates));
        ValidateUniqueEstimator(uItems, nameof(uEstimates));

        var result = new List<FactorialCalibrationCandidate>(mItems.Length * uItems.Length);
        foreach (var m in mItems.OrderBy(static item => item.Estimator))
        foreach (var u in uItems.OrderBy(static item => item.Estimator))
        {
            var parameters = new Dictionary<string, decimal>(StringComparer.Ordinal);
            if (commonParameters is not null)
            {
                foreach (var pair in commonParameters)
                    parameters.Add(pair.Key, pair.Value);
            }

            AddPrefixed(parameters, m.Values, "M_", "m");
            AddPrefixed(parameters, u.Values, "U_", "u");

            result.Add(new FactorialCalibrationCandidate(
                $"M={m.Estimator}@{m.Version}|U={u.Estimator}@{u.Version}",
                m.Estimator,
                u.Estimator,
                m.Version,
                u.Version,
                parameters));
        }

        return result;
    }

    private static void AddPrefixed(
        IDictionary<string, decimal> target,
        IReadOnlyDictionary<string, decimal> source,
        string requiredPrefix,
        string label)
    {
        foreach (var pair in source.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!pair.Key.StartsWith(requiredPrefix, StringComparison.Ordinal))
                throw new ArgumentException($"Parâmetro {label} '{pair.Key}' deve iniciar com '{requiredPrefix}'.");
            if (!target.TryAdd(pair.Key, pair.Value))
                throw new ArgumentException($"Parâmetro duplicado no candidato: {pair.Key}.");
        }
    }

    private static void ValidateUniqueEstimator(IReadOnlyCollection<ParameterEstimate> estimates, string parameterName)
    {
        if (estimates.Select(static item => item.Estimator).Distinct().Count() != estimates.Count)
            throw new ArgumentException("Cada estimador deve aparecer uma única vez por família de parâmetros.", parameterName);
        if (estimates.Any(static item => string.IsNullOrWhiteSpace(item.Version)))
            throw new ArgumentException("Toda estimativa deve possuir versão.", parameterName);
    }
}
