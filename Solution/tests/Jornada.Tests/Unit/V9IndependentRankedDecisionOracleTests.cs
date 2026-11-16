using Jornada.Linkage.Runner;
using Jornada.Contracts;

namespace Jornada.Tests.Unit;

/// <summary>
/// Independent truth-table oracle for the V9 ranked decision boundary.
/// It does not call ReplayTest, grouping, SQL or the decision kernel to
/// compute expected outcomes. TF4 score calibration is NOT covered here.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class V9IndependentRankedDecisionOracleTests
{
    private static readonly Guid Model = Guid.Parse("00000000-0000-0000-0000-000000000123");
    private static readonly Guid First = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Test]
    public void Exhaustive_ranked_boundary_agrees_with_independent_truth_table()
    {
        const decimal threshold = .8m;
        const decimal marginFloor = 2m;
        var posterior = new[] { .4m, .79m, .8m, .95m };
        var odds = new[] { 0m, 1m, 3m, 5m };
        var cases = 0;
        foreach (var bestPosterior in posterior)
        foreach (var bestOdds in odds)
        foreach (var secondPosterior in posterior)
        foreach (var secondOdds in odds)
        foreach (var exactRisk in new[] { false, true })
        foreach (var demographicGuard in new[] { false, true })
        foreach (var dualGuard in new[] { false, true })
        {
            // Caller supplies a ranking in descending log-odds order.
            if (secondOdds > bestOdds) continue;
            var parameters = new Dictionary<string, decimal>();
            if (demographicGuard)
                parameters[LinkageParameterCatalog.NonUniqueDemographicExactGuard] = 1m;
            if (dualGuard)
                parameters[LinkageParameterCatalog.DualThresholdConflictGuard] = 1m;
            var ranked = new[]
            {
                new CandidateScore(First, bestPosterior, bestOdds, exactRisk),
                new CandidateScore(Second, secondPosterior, secondOdds)
            };
            var expected = bestPosterior < threshold
                ? ResolutionStatus.NAO_RESOLVIDO
                : demographicGuard && exactRisk
                    ? ResolutionStatus.CONFLITO
                    : dualGuard && secondPosterior >= threshold
                        ? ResolutionStatus.CONFLITO
                        : bestOdds - secondOdds < marginFloor
                            ? ResolutionStatus.CONFLITO
                            : ResolutionStatus.RESOLVIDO;
            var actual = ProbabilisticLinkageDecisions.ResolveRankedCompositeNominalV9Preview(
                Model, parameters, ranked, threshold, marginFloor);
            Assert.That(actual.Status, Is.EqualTo(expected),
                $"case={cases}: best={bestPosterior}/{bestOdds}, second={secondPosterior}/{secondOdds}, exact={exactRisk}, demographic={demographicGuard}, dual={dualGuard}");
            Assert.That(actual.ModeloId, Is.EqualTo(Model));
            cases++;
        }
        Assert.That(cases, Is.GreaterThan(100));
    }

    [Test]
    public void Empty_ranked_set_abstains_without_inventing_a_candidate()
    {
        var actual = ProbabilisticLinkageDecisions.ResolveRankedCompositeNominalV9Preview(
            Model, new Dictionary<string, decimal>(), [], .8m, 2m,
            "TF_SEM_FREQUENCIA_IBGE_PUBLICADA");
        Assert.That(actual.Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
        Assert.That(actual.Motivo, Is.EqualTo("TF_SEM_FREQUENCIA_IBGE_PUBLICADA"));
    }
}
