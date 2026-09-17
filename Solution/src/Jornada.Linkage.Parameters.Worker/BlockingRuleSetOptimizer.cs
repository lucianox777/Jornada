using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record BlockingPassMarginalContribution(
    string PassId,
    decimal IncrementalTrueMatchWeight,
    decimal IncrementalNonMatchWeight,
    string Justification);

public sealed record BlockingRuleSetOptimizationResult(
    IReadOnlyList<LinkageBlockingPass> Passes,
    BlockingRuleSetDiagnosticResult Diagnostic,
    int FieldClauseCount,
    int DistinctFieldCount,
    string CanonicalSignature,
    IReadOnlyList<BlockingPassMarginalContribution> PassContributions,
    int UnjustifiedZeroGainPassCount);

/// <summary>
/// Seleciona deterministicamente o melhor ruleset dentre candidatos explicitamente fornecidos.
/// Recall mínimo é restrição fail-closed; entre alternativas elegíveis, prioriza redução do universo,
/// depois recall e contribuição marginal observada dos passes. Passe que não acrescenta cobertura de
/// verdade nem suporte u necessário recebe penalidade, mas não é proibido: amostras finitas podem não
/// conter a cobertura rara para a qual ele foi concebido. A geração/limitação do espaço de candidatos
/// é responsabilidade separada e deve ser versionada pelo Calibrador.
/// </summary>
public static class BlockingRuleSetOptimizer
{
    public const string MethodVersion = "BLOCKING_RULESET_OPTIMIZER_V3";

    public static BlockingRuleSetOptimizationResult SelectBest(
        IReadOnlyCollection<BlockingFeatureObservation> observations,
        IEnumerable<IReadOnlyList<LinkageBlockingPass>> candidates,
        double minimumTrueMatchRecall = 0d,
        bool requireObservedNonMatchSupport = false)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(candidates);
        if (minimumTrueMatchRecall is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(minimumTrueMatchRecall));

        var evaluated = candidates
            .Select(Canonicalize)
            .GroupBy(static passes => Signature(passes), StringComparer.Ordinal)
            .Select(static group => group.First())
            .Select(passes => BuildResult(observations, passes, requireObservedNonMatchSupport))
            .Where(result => result.Diagnostic.TrueMatchRecall >= minimumTrueMatchRecall)
            .Where(result => !requireObservedNonMatchSupport || result.Diagnostic.NonMatchRetention > 0d)
            .OrderByDescending(static result => result.Diagnostic.ReductionRatio)
            .ThenByDescending(static result => result.Diagnostic.TrueMatchRecall)
            .ThenBy(static result => result.UnjustifiedZeroGainPassCount)
            .ThenByDescending(static result => result.Diagnostic.CompleteMatchCoverage)
            .ThenBy(static result => result.FieldClauseCount)
            .ThenBy(static result => result.DistinctFieldCount)
            .ThenBy(static result => result.Passes.Count)
            .ThenBy(static result => result.CanonicalSignature, StringComparer.Ordinal)
            .ToArray();

        if (evaluated.Length == 0)
        {
            var supportRequirement = requireObservedNonMatchSupport
                ? " e suporte observado de não-vínculos retidos"
                : string.Empty;
            throw new InvalidOperationException(
                $"Nenhum ruleset candidato satisfez o recall mínimo exigido{supportRequirement} para promoção.");
        }

        return evaluated[0];
    }

    private static BlockingRuleSetOptimizationResult BuildResult(
        IReadOnlyCollection<BlockingFeatureObservation> observations,
        IReadOnlyList<LinkageBlockingPass> passes,
        bool requireObservedNonMatchSupport)
    {
        var diagnostic = BlockingRuleSetDiagnostic.Analyze(observations, passes);
        var clauses = passes.Sum(static pass => pass.Fields.Count);
        var distinct = passes.SelectMany(static pass => pass.Fields).Distinct(StringComparer.Ordinal).Count();
        var contributions = BuildPassContributions(observations, passes, requireObservedNonMatchSupport);
        var unjustifiedZeroGain = contributions.Count(static contribution =>
            contribution.Justification == "NO_OBSERVED_INCREMENTAL_GAIN");
        return new BlockingRuleSetOptimizationResult(
            passes,
            diagnostic,
            clauses,
            distinct,
            Signature(passes),
            contributions,
            unjustifiedZeroGain);
    }

    private static IReadOnlyList<BlockingPassMarginalContribution> BuildPassContributions(
        IReadOnlyCollection<BlockingFeatureObservation> observations,
        IReadOnlyList<LinkageBlockingPass> passes,
        bool requireObservedNonMatchSupport)
    {
        var fullMatchWeight = RetainedWeight(observations, passes, referenceMatch: true);
        var fullNonMatchWeight = RetainedWeight(observations, passes, referenceMatch: false);
        var result = new List<BlockingPassMarginalContribution>(passes.Count);

        for (var index = 0; index < passes.Count; index++)
        {
            var pass = passes[index];
            var without = passes.Where((_, candidateIndex) => candidateIndex != index).ToArray();
            var matchWithout = without.Length == 0 ? 0m : RetainedWeight(observations, without, referenceMatch: true);
            var nonMatchWithout = without.Length == 0 ? 0m : RetainedWeight(observations, without, referenceMatch: false);
            var matchGain = Math.Max(0m, fullMatchWeight - matchWithout);
            var nonMatchGain = Math.Max(0m, fullNonMatchWeight - nonMatchWithout);
            var justification = matchGain > 0m
                ? "TRUE_MATCH_COVERAGE"
                : requireObservedNonMatchSupport && nonMatchGain > 0m
                    ? "OBSERVED_NONMATCH_SUPPORT"
                    : "NO_OBSERVED_INCREMENTAL_GAIN";

            result.Add(new BlockingPassMarginalContribution(
                pass.PassId,
                matchGain,
                nonMatchGain,
                justification));
        }

        return result;
    }

    private static decimal RetainedWeight(
        IReadOnlyCollection<BlockingFeatureObservation> observations,
        IReadOnlyList<LinkageBlockingPass> passes,
        bool referenceMatch)
    {
        decimal weight = 0m;
        foreach (var observation in observations)
        {
            if (observation.IsReferenceMatch != referenceMatch)
                continue;
            if (passes.Any(pass => PassRetains(observation, pass)))
                weight += observation.Weight;
        }
        return weight;
    }

    private static bool PassRetains(BlockingFeatureObservation observation, LinkageBlockingPass pass)
    {
        foreach (var field in pass.Fields)
        {
            if (!observation.Agreements.TryGetValue(field, out var agreement) || agreement is not true)
                return false;
        }
        return true;
    }

    private static IReadOnlyList<LinkageBlockingPass> Canonicalize(IReadOnlyList<LinkageBlockingPass> passes)
    {
        ArgumentNullException.ThrowIfNull(passes);
        if (passes.Count == 0)
            throw new ArgumentException("Ruleset candidato sem passes.", nameof(passes));

        var canonical = passes
            .Select(static pass => LinkageBlockingPass.Create(pass.PassId, pass.Fields))
            .OrderBy(static pass => pass.PassId, StringComparer.Ordinal)
            .ToArray();
        if (canonical.Select(static pass => pass.PassId).Distinct(StringComparer.Ordinal).Count() != canonical.Length)
            throw new ArgumentException("IDs de passe devem ser únicos dentro do ruleset candidato.", nameof(passes));
        return canonical;
    }

    private static string Signature(IReadOnlyList<LinkageBlockingPass> passes) =>
        string.Join("||", passes.Select(static pass =>
            pass.PassId + ":" + string.Join("+", pass.Fields)));
}
