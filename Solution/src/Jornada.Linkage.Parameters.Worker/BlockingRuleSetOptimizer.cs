using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record BlockingRuleSetOptimizationResult(
    IReadOnlyList<LinkageBlockingPass> Passes,
    BlockingRuleSetDiagnosticResult Diagnostic,
    int FieldClauseCount,
    int DistinctFieldCount,
    string CanonicalSignature);

/// <summary>
/// Seleciona deterministicamente o melhor ruleset dentre candidatos explicitamente fornecidos.
/// Não usa score composto ou pesos ocultos: prioriza recall, redução, cobertura e só então
/// menor custo estrutural. A geração/limitação do espaço de candidatos é responsabilidade
/// separada e deve ser versionada pelo Calibrador.
/// </summary>
public static class BlockingRuleSetOptimizer
{
    public const string MethodVersion = "BLOCKING_RULESET_OPTIMIZER_V1";

    public static BlockingRuleSetOptimizationResult SelectBest(
        IReadOnlyCollection<BlockingFeatureObservation> observations,
        IEnumerable<IReadOnlyList<LinkageBlockingPass>> candidates,
        double minimumTrueMatchRecall = 0d)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(candidates);
        if (minimumTrueMatchRecall is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(minimumTrueMatchRecall));

        var evaluated = candidates
            .Select(Canonicalize)
            .GroupBy(static passes => Signature(passes), StringComparer.Ordinal)
            .Select(static group => group.First())
            .Select(passes => BuildResult(observations, passes))
            .Where(result => result.Diagnostic.TrueMatchRecall >= minimumTrueMatchRecall)
            .OrderByDescending(static result => result.Diagnostic.TrueMatchRecall)
            .ThenByDescending(static result => result.Diagnostic.ReductionRatio)
            .ThenByDescending(static result => result.Diagnostic.CompleteMatchCoverage)
            .ThenBy(static result => result.FieldClauseCount)
            .ThenBy(static result => result.DistinctFieldCount)
            .ThenBy(static result => result.Passes.Count)
            .ThenBy(static result => result.CanonicalSignature, StringComparer.Ordinal)
            .ToArray();

        if (evaluated.Length == 0)
            throw new InvalidOperationException(
                "Nenhum ruleset candidato satisfez o recall mínimo exigido para promoção.");

        return evaluated[0];
    }

    private static BlockingRuleSetOptimizationResult BuildResult(
        IReadOnlyCollection<BlockingFeatureObservation> observations,
        IReadOnlyList<LinkageBlockingPass> passes)
    {
        var diagnostic = BlockingRuleSetDiagnostic.Analyze(observations, passes);
        var clauses = passes.Sum(static pass => pass.Fields.Count);
        var distinct = passes.SelectMany(static pass => pass.Fields).Distinct(StringComparer.Ordinal).Count();
        return new BlockingRuleSetOptimizationResult(
            passes,
            diagnostic,
            clauses,
            distinct,
            Signature(passes));
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
