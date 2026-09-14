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

    private static readonly IReadOnlyDictionary<string, decimal> ParametersV2 = new Dictionary<string, decimal>(Parameters)
    {
        ["M_NASC_DIA_EXACT"] = 0.95m, ["M_NASC_DIA_DIFF"] = 0.05m,
        ["U_NASC_DIA_EXACT"] = 0.03m, ["U_NASC_DIA_DIFF"] = 0.97m,
        ["M_NASC_MES_EXACT"] = 0.98m, ["M_NASC_MES_DIFF"] = 0.02m,
        ["U_NASC_MES_EXACT"] = 0.08m, ["U_NASC_MES_DIFF"] = 0.92m,
        ["M_NASC_ANO_EXACT"] = 0.99m, ["M_NASC_ANO_DIFF"] = 0.01m,
        ["U_NASC_ANO_EXACT"] = 0.02m, ["U_NASC_ANO_DIFF"] = 0.98m
    };

    private static readonly IReadOnlyDictionary<string, decimal> ParametersV3 = new Dictionary<string, decimal>(ParametersV2)
    {
        [LinkageParameterCatalog.BirthSingleEvidenceScoring] = 1m,
        ["M_DATA_NASCIMENTO_EXACT"] = 0.96m,
        ["M_DATA_NASCIMENTO_DIFF"] = 0.04m,
        ["U_DATA_NASCIMENTO_EXACT"] = 0.002m,
        ["U_DATA_NASCIMENTO_DIFF"] = 0.998m
    };

    private static readonly IReadOnlyDictionary<string, decimal> ParametersV4 = JointBirthParameters(includeLegacyFlags: false);

    [Test]
    public void Exact_name_and_mother_name_produce_high_posterior()
    {
        var score = FellegiSunterScoring.CalculatePosterior(Parameters, NameComparisonState.EXACT, NameComparisonState.EXACT);
        Assert.That(score, Is.GreaterThan(0.95m));
    }

    [Test]
    public void Missing_mother_name_is_neutral_evidence()
    {
        var withoutMother = FellegiSunterScoring.CalculatePosterior(Parameters, NameComparisonState.HIGH, null, blockCandidateCount: 100);
        var expected = FellegiSunterScoring.CalculatePosterior(new Dictionary<string, decimal>(Parameters), NameComparisonState.HIGH, null, blockCandidateCount: 100);
        var observedLowMother = FellegiSunterScoring.CalculatePosterior(Parameters, NameComparisonState.HIGH, NameComparisonState.LOW, blockCandidateCount: 100);
        Assert.Multiple(() => { Assert.That(withoutMother, Is.EqualTo(expected)); Assert.That(withoutMother, Is.GreaterThan(observedLowMother)); });
    }

    [Test]
    public void Low_similarity_produces_low_posterior()
    {
        var score = FellegiSunterScoring.CalculatePosterior(Parameters, NameComparisonState.LOW, NameComparisonState.LOW);
        Assert.That(score, Is.LessThan(0.01m));
    }

    [Test]
    public void Larger_block_produces_lower_posterior_for_same_evidence()
    {
        var small = FellegiSunterScoring.CalculatePosterior(Parameters, NameComparisonState.HIGH, NameComparisonState.HIGH, 10);
        var large = FellegiSunterScoring.CalculatePosterior(Parameters, NameComparisonState.HIGH, NameComparisonState.HIGH, 10_000);
        Assert.That(large, Is.LessThan(small));
    }

    [Test]
    public void Birth_day_month_and_year_remain_separate_in_v2_replay()
    {
        var source = new DateOnly(1980, 6, 5);
        var exact = FellegiSunterScoring.CalculatePosterior(ParametersV2, NameComparisonState.HIGH, NameComparisonState.HIGH, 100, source, source);
        var different = FellegiSunterScoring.CalculatePosterior(ParametersV2, NameComparisonState.HIGH, NameComparisonState.HIGH, 100, source, new DateOnly(1980, 6, 6));
        Assert.That(exact, Is.GreaterThan(different));
    }

    [Test]
    public void V3_birth_is_single_evidence_and_ignores_v2_component_strengths()
    {
        var source = new DateOnly(1980, 6, 5);
        var extremeLegacy = new Dictionary<string, decimal>(ParametersV3)
        {
            ["M_NASC_DIA_EXACT"] = 0.999999m, ["U_NASC_DIA_EXACT"] = 0.000001m,
            ["M_NASC_MES_EXACT"] = 0.999999m, ["U_NASC_MES_EXACT"] = 0.000001m,
            ["M_NASC_ANO_EXACT"] = 0.999999m, ["U_NASC_ANO_EXACT"] = 0.000001m
        };
        var baseline = FellegiSunterScoring.CalculatePosterior(ParametersV3, NameComparisonState.HIGH, NameComparisonState.HIGH, 100, source, source);
        var changedLegacy = FellegiSunterScoring.CalculatePosterior(extremeLegacy, NameComparisonState.HIGH, NameComparisonState.HIGH, 100, source, source);
        var different = FellegiSunterScoring.CalculatePosterior(ParametersV3, NameComparisonState.HIGH, NameComparisonState.HIGH, 100, source, new DateOnly(1980, 6, 6));
        Assert.Multiple(() => { Assert.That(changedLegacy, Is.EqualTo(baseline)); Assert.That(baseline, Is.GreaterThan(different)); });
    }

    [Test]
    public void V4_birth_uses_one_joint_state_and_distinguishes_partial_agreement()
    {
        var source = new DateOnly(1980, 6, 5);
        var exact = FellegiSunterScoring.CalculatePosterior(ParametersV4, NameComparisonState.HIGH, NameComparisonState.HIGH, 100, source, source);
        var yearOnly = FellegiSunterScoring.CalculatePosterior(ParametersV4, NameComparisonState.HIGH, NameComparisonState.HIGH, 100, source, new DateOnly(1980, 7, 6));
        var dayMonth = FellegiSunterScoring.CalculatePosterior(ParametersV4, NameComparisonState.HIGH, NameComparisonState.HIGH, 100, source, new DateOnly(1981, 6, 5));
        var dayYear = FellegiSunterScoring.CalculatePosterior(ParametersV4, NameComparisonState.HIGH, NameComparisonState.HIGH, 100, source, new DateOnly(1980, 7, 5));
        Assert.Multiple(() =>
        {
            Assert.That(exact, Is.GreaterThan(dayYear));
            Assert.That(dayYear, Is.GreaterThan(dayMonth));
            Assert.That(dayMonth, Is.GreaterThan(yearOnly));
        });
    }

    [Test]
    public void V4_takes_precedence_over_v3_and_v2_birth_parameters_when_replaying_mixed_model()
    {
        var source = new DateOnly(1980, 6, 5);
        var mixed = JointBirthParameters(includeLegacyFlags: true);
        var alteredLegacy = new Dictionary<string, decimal>(mixed)
        {
            ["M_DATA_NASCIMENTO_EXACT"] = 0.500001m,
            ["U_DATA_NASCIMENTO_EXACT"] = 0.499999m,
            ["M_NASC_DIA_EXACT"] = 0.500001m,
            ["U_NASC_DIA_EXACT"] = 0.499999m
        };
        var baseline = FellegiSunterScoring.CalculatePosterior(mixed, NameComparisonState.HIGH, NameComparisonState.HIGH, 100, source, source);
        var changed = FellegiSunterScoring.CalculatePosterior(alteredLegacy, NameComparisonState.HIGH, NameComparisonState.HIGH, 100, source, source);
        Assert.That(changed, Is.EqualTo(baseline));
    }

    [Test]
    public void V1_model_without_birth_parameters_keeps_previous_score()
    {
        var withoutBirth = FellegiSunterScoring.CalculatePosterior(Parameters, NameComparisonState.HIGH, NameComparisonState.HIGH, 100);
        var withBirth = FellegiSunterScoring.CalculatePosterior(Parameters, NameComparisonState.HIGH, NameComparisonState.HIGH, 100,
            new DateOnly(1980, 6, 5), new DateOnly(1981, 7, 6));
        Assert.That(withBirth, Is.EqualTo(withoutBirth));
    }

    private static Dictionary<string, decimal> JointBirthParameters(bool includeLegacyFlags)
    {
        var parameters = includeLegacyFlags
            ? new Dictionary<string, decimal>(ParametersV3)
            : new Dictionary<string, decimal>(Parameters);
        parameters[LinkageParameterCatalog.BirthJointEvidenceScoring] = 1m;
        var m = new[] { .01m, .02m, .03m, .06m, .08m, .15m, .20m, .45m };
        var u = new[] { .45m, .15m, .12m, .08m, .08m, .05m, .04m, .03m };
        for (var i = 0; i < LinkageParameterCatalog.BirthJointStates.Count; i++)
        {
            var state = LinkageParameterCatalog.BirthJointStates[i];
            parameters[$"M_NASCIMENTO_CONJUNTO_{state}"] = m[i];
            parameters[$"U_NASCIMENTO_CONJUNTO_{state}"] = u[i];
        }
        return parameters;
    }
}
