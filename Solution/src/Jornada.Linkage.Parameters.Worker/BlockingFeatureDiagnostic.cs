namespace Jornada.Linkage.Parameters.Worker;

public sealed record BlockingFeatureObservation(
    bool IsReferenceMatch,
    IReadOnlyDictionary<string, bool?> Agreements,
    decimal Weight = 1m);

public sealed record BlockingFieldDiagnostic(
    string Field,
    double TrueMatchRecall,
    double NonMatchRetention,
    double ReductionRatio,
    double AgreementLogLikelihoodRatio,
    double MissingRate,
    decimal EffectiveObservedWeight);

public sealed record BlockingFieldDependency(
    string LeftField,
    string RightField,
    double PhiAgreementCorrelation,
    decimal EffectiveObservedWeight);

public sealed record BlockingFeatureDiagnosticReport(
    string MethodVersion,
    IReadOnlyList<BlockingFieldDiagnostic> Fields,
    IReadOnlyList<BlockingFieldDependency> Dependencies);

/// <summary>
/// Read-only diagnostic for candidate blocking features. It measures empirical recall,
/// pair-space reduction, discrimination and pairwise dependence from an independently
/// labelled corpus. It never changes BirthBlockingPlan or any operational linkage policy.
/// </summary>
public static class BlockingFeatureDiagnostic
{
    public const string MethodVersion = "BLOCKING_FEATURE_DIAGNOSTIC_V1";
    private const double Smoothing = 0.5d;

    public static BlockingFeatureDiagnosticReport Analyze(
        IReadOnlyCollection<BlockingFeatureObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count == 0)
            throw new ArgumentException("O corpus de diagnóstico não pode ser vazio.", nameof(observations));

        foreach (var observation in observations)
        {
            ArgumentNullException.ThrowIfNull(observation);
            ArgumentNullException.ThrowIfNull(observation.Agreements);
            if (observation.Weight <= 0m)
                throw new ArgumentOutOfRangeException(nameof(observations), "Todos os pesos devem ser positivos.");
            if (observation.Agreements.Keys.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Nome de campo de blocking inválido.", nameof(observations));
        }

        var fields = observations
            .SelectMany(static x => x.Agreements.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();

        if (fields.Length == 0)
            throw new ArgumentException("Nenhum campo elegível foi informado.", nameof(observations));

        var totalWeight = observations.Sum(static x => x.Weight);
        var matchWeight = observations.Where(static x => x.IsReferenceMatch).Sum(static x => x.Weight);
        var nonMatchWeight = observations.Where(static x => !x.IsReferenceMatch).Sum(static x => x.Weight);
        if (matchWeight <= 0m || nonMatchWeight <= 0m)
            throw new ArgumentException("O corpus deve conter vínculos e não-vínculos de referência.", nameof(observations));

        var diagnostics = fields
            .Select(field => AnalyzeField(field, observations, totalWeight, matchWeight, nonMatchWeight))
            .OrderByDescending(static x => x.TrueMatchRecall)
            .ThenByDescending(static x => x.ReductionRatio)
            .ThenByDescending(static x => x.AgreementLogLikelihoodRatio)
            .ThenBy(static x => x.Field, StringComparer.Ordinal)
            .ToArray();

        var dependencies = new List<BlockingFieldDependency>();
        for (var i = 0; i < fields.Length; i++)
        for (var j = i + 1; j < fields.Length; j++)
            dependencies.Add(AnalyzeDependency(fields[i], fields[j], observations));

        return new BlockingFeatureDiagnosticReport(MethodVersion, diagnostics, dependencies);
    }

    private static BlockingFieldDiagnostic AnalyzeField(
        string field,
        IReadOnlyCollection<BlockingFeatureObservation> observations,
        decimal totalWeight,
        decimal matchWeight,
        decimal nonMatchWeight)
    {
        decimal observedWeight = 0m;
        decimal matchAgreementWeight = 0m;
        decimal nonMatchAgreementWeight = 0m;

        foreach (var observation in observations)
        {
            if (!observation.Agreements.TryGetValue(field, out var agreement) || agreement is null)
                continue;

            observedWeight += observation.Weight;
            if (agreement.Value)
            {
                if (observation.IsReferenceMatch) matchAgreementWeight += observation.Weight;
                else nonMatchAgreementWeight += observation.Weight;
            }
        }

        var recall = (double)(matchAgreementWeight / matchWeight);
        var retention = (double)(nonMatchAgreementWeight / nonMatchWeight);
        var reduction = 1d - retention;
        var missing = 1d - (double)(observedWeight / totalWeight);

        var matchAgreementProbability =
            ((double)matchAgreementWeight + Smoothing) / ((double)matchWeight + 2d * Smoothing);
        var nonMatchAgreementProbability =
            ((double)nonMatchAgreementWeight + Smoothing) / ((double)nonMatchWeight + 2d * Smoothing);
        var llr = Math.Log(matchAgreementProbability / nonMatchAgreementProbability);

        return new BlockingFieldDiagnostic(
            field,
            recall,
            retention,
            reduction,
            llr,
            missing,
            observedWeight);
    }

    private static BlockingFieldDependency AnalyzeDependency(
        string left,
        string right,
        IReadOnlyCollection<BlockingFeatureObservation> observations)
    {
        var rows = observations
            .Select(x =>
            {
                var hasLeft = x.Agreements.TryGetValue(left, out var l) && l is not null;
                var hasRight = x.Agreements.TryGetValue(right, out var r) && r is not null;
                return (Observation: x, HasBoth: hasLeft && hasRight, Left: l, Right: r);
            })
            .Where(static x => x.HasBoth)
            .ToArray();

        var weight = rows.Sum(static x => x.Observation.Weight);
        if (weight <= 0m)
            return new BlockingFieldDependency(left, right, 0d, 0m);

        var total = (double)weight;
        var meanX = rows.Sum(x => (double)x.Observation.Weight * (x.Left!.Value ? 1d : 0d)) / total;
        var meanY = rows.Sum(x => (double)x.Observation.Weight * (x.Right!.Value ? 1d : 0d)) / total;

        var covariance = 0d;
        var varianceX = 0d;
        var varianceY = 0d;
        foreach (var row in rows)
        {
            var w = (double)row.Observation.Weight;
            var x = row.Left!.Value ? 1d : 0d;
            var y = row.Right!.Value ? 1d : 0d;
            covariance += w * (x - meanX) * (y - meanY);
            varianceX += w * (x - meanX) * (x - meanX);
            varianceY += w * (y - meanY) * (y - meanY);
        }

        var denominator = Math.Sqrt(varianceX * varianceY);
        var phi = denominator == 0d ? 0d : covariance / denominator;
        return new BlockingFieldDependency(left, right, phi, weight);
    }
}
