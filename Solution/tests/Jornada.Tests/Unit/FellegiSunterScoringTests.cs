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

    private static readonly IReadOnlyDictionary<string, decimal> ParametersV2 =
        new Dictionary<string, decimal>(Parameters)
        {
            ["M_NASC_DIA_EXACT"] = 0.95m, ["M_NASC_DIA_DIFF"] = 0.05m,
            ["U_NASC_DIA_EXACT"] = 0.03m, ["U_NASC_DIA_DIFF"] = 0.97m,
            ["M_NASC_MES_EXACT"] = 0.98m, ["M_NASC_MES_DIFF"] = 0.02m,
            ["U_NASC_MES_EXACT"] = 0.08m, ["U_NASC_MES_DIFF"] = 0.92m,
            ["M_NASC_ANO_EXACT"] = 0.99m, ["M_NASC_ANO_DIFF"] = 0.01m,
            ["U_NASC_ANO_EXACT"] = 0.02m, ["U_NASC_ANO_DIFF"] = 0.98m
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

    [Test]
    public void Birth_day_month_and_year_are_scored_as_separate_evidence_in_v2()
    {
        var source = new DateOnly(1980, 6, 5);
        var exact = FellegiSunterScoring.CalculatePosterior(
            ParametersV2,
            NameComparisonState.HIGH,
            NameComparisonState.HIGH,
            blockCandidateCount: 100,
            leftBirthDate: source,
            rightBirthDate: source);

        var dayDifferent = FellegiSunterScoring.CalculatePosterior(
            ParametersV2,
            NameComparisonState.HIGH,
            NameComparisonState.HIGH,
            blockCandidateCount: 100,
            leftBirthDate: source,
            rightBirthDate: new DateOnly(1980, 6, 6));

        var monthDifferent = FellegiSunterScoring.CalculatePosterior(
            ParametersV2,
            NameComparisonState.HIGH,
            NameComparisonState.HIGH,
            blockCandidateCount: 100,
            leftBirthDate: source,
            rightBirthDate: new DateOnly(1980, 7, 5));

        var yearDifferent = FellegiSunterScoring.CalculatePosterior(
            ParametersV2,
            NameComparisonState.HIGH,
            NameComparisonState.HIGH,
            blockCandidateCount: 100,
            leftBirthDate: source,
            rightBirthDate: new DateOnly(1981, 6, 5));

        Assert.Multiple(() =>
        {
            Assert.That(exact, Is.GreaterThan(dayDifferent));
            Assert.That(exact, Is.GreaterThan(monthDifferent));
            Assert.That(exact, Is.GreaterThan(yearDifferent));
        });
    }

    [Test]
    public void V1_model_without_birth_component_parameters_keeps_previous_score()
    {
        var withoutBirth = FellegiSunterScoring.CalculatePosterior(
            Parameters,
            NameComparisonState.HIGH,
            NameComparisonState.HIGH,
            blockCandidateCount: 100);

        var withBirthArguments = FellegiSunterScoring.CalculatePosterior(
            Parameters,
            NameComparisonState.HIGH,
            NameComparisonState.HIGH,
            blockCandidateCount: 100,
            leftBirthDate: new DateOnly(1980, 6, 5),
            rightBirthDate: new DateOnly(1981, 7, 6));

        Assert.That(withBirthArguments, Is.EqualTo(withoutBirth));
    }
}
