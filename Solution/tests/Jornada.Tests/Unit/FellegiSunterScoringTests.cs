using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class FellegiSunterScoringTests
{
    private static readonly IReadOnlyDictionary<string, decimal> Parameters = new Dictionary<string, decimal>
    {
        ["PRIOR_MATCH_PROBABILITY"] = 0.01m,
        ["M_NOME_EXACT"] = 0.90m, ["M_NOME_HIGH"] = 0.07m, ["M_NOME_MEDIUM"] = 0.02m, ["M_NOME_LOW"] = 0.01m,
        ["U_NOME_EXACT"] = 0.001m, ["U_NOME_HIGH"] = 0.004m, ["U_NOME_MEDIUM"] = 0.020m, ["U_NOME_LOW"] = 0.975m,
        ["M_NOME_MAE_EXACT"] = 0.88m, ["M_NOME_MAE_HIGH"] = 0.08m, ["M_NOME_MAE_MEDIUM"] = 0.03m, ["M_NOME_MAE_LOW"] = 0.01m,
        ["U_NOME_MAE_EXACT"] = 0.001m, ["U_NOME_MAE_HIGH"] = 0.004m, ["U_NOME_MAE_MEDIUM"] = 0.020m, ["U_NOME_MAE_LOW"] = 0.975m
    };

    [Test]
    public void Exact_name_and_mother_name_produce_high_posterior()
    {
        var score = FellegiSunterScoring.CalculatePosterior(
            Parameters, NameComparisonState.EXACT, NameComparisonState.EXACT);
        Assert.That(score, Is.GreaterThan(0.95m));
    }

    [Test]
    public void Low_similarity_produces_low_posterior()
    {
        var score = FellegiSunterScoring.CalculatePosterior(
            Parameters, NameComparisonState.LOW, NameComparisonState.LOW);
        Assert.That(score, Is.LessThan(0.01m));
    }
    [Test]
    public void Larger_block_produces_lower_posterior_for_same_evidence()
    {
        var small = FellegiSunterScoring.CalculatePosterior(
            Parameters, NameComparisonState.HIGH, NameComparisonState.HIGH, blockCandidateCount: 10);
        var large = FellegiSunterScoring.CalculatePosterior(
            Parameters, NameComparisonState.HIGH, NameComparisonState.HIGH, blockCandidateCount: 10_000);
        Assert.That(large, Is.LessThan(small));
    }

}
