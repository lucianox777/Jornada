using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9ExploratoryRiskPreferenceV1Tests
{
    private const long Eligible = 7841;
    private const string Tf0 = "T0.95_TF0";
    private const string Mother = "TF_ONE_MF_W1_M0_A0.5_T0.95";
    private const string TwoMother = "TF_TWO_MF_ML_W1_M0_A0.5_T0.95";
    private const string TwoPersonMother = "TF_TWO_PL_MF_W1_M0_A0.5_T0.95";
    private const string MoreRecall = "TF_TWO_PF_MF_W1_M0_A0.5_T0.95";

    private static string Key(string id) => V9ParetoSourceSelection.ConfigurationId(
        id, V9AdaptiveCalibrationCoordinator.Grouping, .5m);

    private static V9BootstrapPareto2D.CandidateOption Observed(
        string id, long tp, long fp, double conditionalCp, char fingerprint) =>
        new(Key(id), new string(fingerprint, 64), tp / (double)Eligible,
            fp / (double)(tp + fp), null, tp, fp, Eligible, 5556,
            false, false, conditionalCp, 3861);

    private static V9FsAdaptiveOptionConfiguration Model(string id,
        params (CompositeNominalTfSideV9 Side, NominalFrequencyUnitV9 Unit)[] active)
    {
        var units = active.Select(slot => new V9FsUnitTfOption(
            slot.Side, slot.Unit, 1m, CompositeNominalTfAdjustmentModeV9.Symmetric)).ToArray();
        return new(id, .5m, .95m, UnitTfOptions: units);
    }

    private static readonly V9FsAdaptiveOptionConfiguration[] Models =
    [
        Model(Tf0),
        Model(Mother, (CompositeNominalTfSideV9.Mother, NominalFrequencyUnitV9.FirstName)),
        Model(TwoMother, (CompositeNominalTfSideV9.Mother, NominalFrequencyUnitV9.FirstName),
            (CompositeNominalTfSideV9.Mother, NominalFrequencyUnitV9.LastContentSurname)),
        Model(TwoPersonMother, (CompositeNominalTfSideV9.Person, NominalFrequencyUnitV9.LastContentSurname),
            (CompositeNominalTfSideV9.Mother, NominalFrequencyUnitV9.FirstName)),
        Model(MoreRecall, (CompositeNominalTfSideV9.Person, NominalFrequencyUnitV9.FirstName),
            (CompositeNominalTfSideV9.Mother, NominalFrequencyUnitV9.FirstName))
    ];

    private static V9BootstrapPareto2D.CandidateOption[] Historical() =>
    [
        Observed(Tf0, 7505, 0, .007043077205281283, 'a'),
        Observed(Mother, 7533, 0, .00701409490945916, 'b'),
        Observed(TwoMother, 7533, 0, .00701409490945916, 'c'),
        Observed(TwoPersonMother, 7533, 0, .00701409490945916, 'd'),
        Observed(MoreRecall, 7566, 1, .00858677337885876, 'e')
    ];

    private static V9BootstrapPareto2D.Result Pareto(
        IEnumerable<V9BootstrapPareto2D.CandidateOption> options) =>
        V9BootstrapPareto2D.Evaluate(options, new(0, .001, 20));

    [Test]
    public void Bootstrap_tolerance_selects_one_mother_first_name_and_preserves_v3()
    {
        var v3 = Pareto(Historical());
        var previousWinner = v3.ExploratorySuggestedOptionId;
        var result = V9ExploratoryRiskPreferenceV1.Evaluate(new(), v3, Models);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.Version, Is.EqualTo(V9ExploratoryRiskPreferenceV1.Version));
            Assert.That(result.FdrUpperTolerancePp, Is.EqualTo(.05m));
            Assert.That(result.SuggestedOptionId, Is.EqualTo(Key(Mother)));
            Assert.That(result.RiskBandOptionIds, Does.Contain(Key(Tf0)));
            Assert.That(result.RiskBandOptionIds, Does.Not.Contain(Key(MoreRecall)));
            Assert.That(result.Status, Is.EqualTo("EXPLORATORY_RISK_PREFERENCE_NOT_CERTIFIED"));
            Assert.That(v3.ExploratorySuggestedOptionId, Is.EqualTo(previousWinner));
            Assert.That(v3.InitialSuggestedOptionId, Is.Null);
        }));
        var reverse = V9ExploratoryRiskPreferenceV1.Evaluate(
            new(), Pareto(Historical().Reverse()), Models.Reverse().ToArray());
        Assert.That(reverse.SuggestedOptionId, Is.EqualTo(result.SuggestedOptionId));
    }

    [Test]
    public void Zero_tolerance_uses_strict_minimum_cp_while_bootstrap_allows_recall_gain()
    {
        const string lowerRisk = "STRICT_LOW_RISK";
        var options = Historical().Append(Observed(lowerRisk, 7000, 0, .0068, 'f')).ToArray();
        var models = Models.Append(Model(lowerRisk)).ToArray();
        var v3 = Pareto(options);
        var strict = V9ExploratoryRiskPreferenceV1.Evaluate(new(0m), v3, models);
        var bootstrap = V9ExploratoryRiskPreferenceV1.Evaluate(new(.05m), v3, models);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(strict.SuggestedOptionId, Is.EqualTo(Key(lowerRisk)));
            Assert.That(bootstrap.SuggestedOptionId, Is.EqualTo(Key(Mother)));
        }));
    }

    [Test]
    public void Excludes_invalid_cluster_cp_and_structurally_bad_candidates()
    {
        var bad = Observed(MoreRecall, 7566, 1, .0001, 'e') with
        {
            EvidenceRejectionReasons = ["AGRUPAMENTO_NAO_OPERACIONAL"]
        };
        var missing = Observed(Tf0, 7505, 0, .007, 'a') with
        {
            IdentityClusterFdrUpper = null
        };
        var invalid = Observed(TwoMother, 7533, 0, .007, 'c') with
        {
            IdentityClusterFdrUpper = double.NaN
        };
        var v3 = Pareto([bad, missing, invalid]);
        var result = V9ExploratoryRiskPreferenceV1.Evaluate(new(), v3, Models);
        Assert.That(result.SuggestedOptionId, Is.Null);
        Assert.That(result.Status, Is.EqualTo("NO_VALID_EXPLORATORY_CP_OBSERVATION"));
    }

    [Test]
    public void Partial_search_cannot_claim_global_risk_preference()
    {
        var result = V9ExploratoryRiskPreferenceV1.Evaluate(
            new(), Pareto(Historical()), Models, wholeSearch: false);
        Assert.That(result.SuggestedOptionId, Is.Null);
        Assert.That(result.Status, Is.EqualTo("PARTIAL_SEARCH_NO_GLOBAL_RISK_SUGGESTION"));
    }

    [Test]
    public void Invalid_tolerance_fails_closed_before_sql()
    {
        foreach (var invalid in new[] { -.0001m, 100.0001m })
            Assert.Throws<InvalidDataException>(() =>
                V9ExploratoryRiskPreferenceV1.ValidatePolicy(new(invalid)));
        Assert.DoesNotThrow(() => V9ExploratoryRiskPreferenceV1.ValidatePolicy(new(0m)));
        Assert.That(new V9ExploratoryRiskPreferenceV1.Policy().FdrUpperTolerancePp,
            Is.EqualTo(.05m));
    }
}
