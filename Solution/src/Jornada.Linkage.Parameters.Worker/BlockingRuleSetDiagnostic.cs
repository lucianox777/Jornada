using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record BlockingRuleSetDiagnosticResult(
    IReadOnlyList<LinkageBlockingPass> Passes,
    double TrueMatchRecall,
    double NonMatchRetention,
    double ReductionRatio,
    double CompleteMatchCoverage,
    double CompleteNonMatchCoverage,
    decimal EffectiveObservedWeight);

/// <summary>
/// Avalia a mesma álgebra usada pelo runtime: AND entre campos de cada passe e OR entre passes.
/// Campo ausente torna aquele passe incompleto para a observação; o passe não é enfraquecido.
/// É somente diagnóstico/calibração e não decide identidade.
/// </summary>
public static class BlockingRuleSetDiagnostic
{
    public const string MethodVersion = "BLOCKING_RULESET_DIAGNOSTIC_V1";

    public static BlockingRuleSetDiagnosticResult Analyze(
        IReadOnlyCollection<BlockingFeatureObservation> observations,
        IEnumerable<LinkageBlockingPass> passes)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(passes);
        if (observations.Count == 0)
            throw new ArgumentException("O corpus de diagnóstico não pode ser vazio.", nameof(observations));

        var canonicalPasses = passes
            .Select(static pass => LinkageBlockingPass.Create(pass.PassId, pass.Fields))
            .OrderBy(static pass => pass.PassId, StringComparer.Ordinal)
            .ToArray();
        if (canonicalPasses.Length == 0)
            throw new ArgumentException("Ao menos um passe de blocking é obrigatório.", nameof(passes));
        if (canonicalPasses.Select(static pass => pass.PassId).Distinct(StringComparer.Ordinal).Count() != canonicalPasses.Length)
            throw new ArgumentException("IDs de passe devem ser únicos.", nameof(passes));

        foreach (var observation in observations)
        {
            ArgumentNullException.ThrowIfNull(observation);
            ArgumentNullException.ThrowIfNull(observation.Agreements);
            if (observation.Weight <= 0m)
                throw new ArgumentOutOfRangeException(nameof(observations), "Todos os pesos devem ser positivos.");
        }

        var matchWeight = observations.Where(static x => x.IsReferenceMatch).Sum(static x => x.Weight);
        var nonMatchWeight = observations.Where(static x => !x.IsReferenceMatch).Sum(static x => x.Weight);
        if (matchWeight <= 0m || nonMatchWeight <= 0m)
            throw new ArgumentException("O corpus deve conter vínculos e não-vínculos de referência.", nameof(observations));

        decimal retainedMatches = 0m;
        decimal retainedNonMatches = 0m;
        decimal completeMatches = 0m;
        decimal completeNonMatches = 0m;
        decimal effectiveObserved = 0m;

        foreach (var observation in observations)
        {
            var anyCompletePass = false;
            var retained = false;

            foreach (var pass in canonicalPasses)
            {
                var complete = true;
                var allAgree = true;

                foreach (var field in pass.Fields)
                {
                    if (!observation.Agreements.TryGetValue(field, out var agreement) || agreement is null)
                    {
                        complete = false;
                        break;
                    }

                    if (!agreement.Value)
                        allAgree = false;
                }

                if (!complete)
                    continue;

                anyCompletePass = true;
                if (allAgree)
                    retained = true;
            }

            if (anyCompletePass)
            {
                effectiveObserved += observation.Weight;
                if (observation.IsReferenceMatch) completeMatches += observation.Weight;
                else completeNonMatches += observation.Weight;
            }

            if (!retained)
                continue;

            if (observation.IsReferenceMatch) retainedMatches += observation.Weight;
            else retainedNonMatches += observation.Weight;
        }

        var recall = (double)(retainedMatches / matchWeight);
        var retention = (double)(retainedNonMatches / nonMatchWeight);

        return new BlockingRuleSetDiagnosticResult(
            canonicalPasses,
            recall,
            retention,
            1d - retention,
            (double)(completeMatches / matchWeight),
            (double)(completeNonMatches / nonMatchWeight),
            effectiveObserved);
    }
}
