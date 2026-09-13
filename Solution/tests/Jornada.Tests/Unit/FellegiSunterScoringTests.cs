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

    private static readonly IReadOnlyDictionary<string, decimal> ParametersV3 =
        new Dictionary<string, decimal>(Parameters)
        {
            ["SCORING_BIRTH_SINGLE_EVIDENCE_V3"] = 1m,
            ["M_DATA_NASCIMENTO_EXACT"] = 0.95m, ["M_DATA_NASCIMENTO_DIFF"] = 0.05m,
            ["U_DATA_NASCIMENTO_EXACT"] = 0.00003m, ["U_DATA_NASCIMENTO_DIFF"] = 0.99997m
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
    public void Birth_date_is_one_evidence_in_v3()
    {
        var source = new DateOnly(1980, 6, 5);
        var exact = FellegiSunterScoring.CalculatePosterior(
            ParametersV3,
            NameComparisonState.HIGH,
            NameComparisonState.HIGH,
            blockCandidateCount: 100,
            leftBirthDate: source,
            rightBirthDate: source);

        var oneDayDifferent = FellegiSunterScoring.CalculatePosterior(
            ParametersV3,
            NameComparisonState.HIGH,
            NameComparisonState.HIGH,
            blockCandidateCount: 100,
            leftBirthDate: source,
            rightBirthDate: new DateOnly(1980, 6, 6));

        var completelyDifferent = FellegiSunterScoring.CalculatePosterior(
            ParametersV3,
            NameComparisonState.HIGH,
            NameComparisonState.HIGH,
            blockCandidateCount: 100,
            leftBirthDate: source,
            rightBirthDate: new DateOnly(1991, 12, 22));

        Assert.Multiple(() =>
        {
            Assert.That(exact, Is.GreaterThan(oneDayDifferent));
            Assert.That(oneDayDifferent, Is.EqualTo(completelyDifferent),
                "V3 não pode transformar componentes da mesma data em três evidências independentes.");
        });
    }

    [Test]
    public void Frequency_stratum_changes_likelihood_ratio_without_multiplying_similarity()
    {
        var stratified = new Dictionary<string, decimal>(Parameters)
        {
            ["M_NOME_EXACT_RARE"] = 0.90m,
            ["U_NOME_EXACT_RARE"] = 0.00001m,
            ["M_NOME_EXACT_VERY_COMMON"] = 0.90m,
            ["U_NOME_EXACT_VERY_COMMON"] = 0.05m
        };

        var rare = FellegiSunterScoring.CalculatePosterior(
            stratified,
            NameComparisonState.EXACT,
            NameComparisonState.HIGH,
            blockCandidateCount: 100,
            nameFrequencyStratum: NameFrequencyStratum.RARE);

        var veryCommon = FellegiSunterScoring.CalculatePosterior(
            stratified,
            NameComparisonState.EXACT,
            NameComparisonState.HIGH,
            blockCandidateCount: 100,
            nameFrequencyStratum: NameFrequencyStratum.VERY_COMMON);

        Assert.That(rare, Is.GreaterThan(veryCommon));
    }

    [Test]
    public void Unknown_frequency_keeps_historical_marginal_parameters()
    {
        var withoutFrequency = FellegiSunterScoring.CalculatePosterior(
            Parameters, NameComparisonState.HIGH, NameComparisonState.HIGH, blockCandidateCount: 100);
        var unknown = FellegiSunterScoring.CalculatePosterior(
            Parameters, NameComparisonState.HIGH, NameComparisonState.HIGH, blockCandidateCount: 100,
            nameFrequencyStratum: NameFrequencyStratum.UNKNOWN,
            motherNameFrequencyStratum: NameFrequencyStratum.UNKNOWN);

        Assert.That(unknown, Is.EqualTo(withoutFrequency));
    }
}
