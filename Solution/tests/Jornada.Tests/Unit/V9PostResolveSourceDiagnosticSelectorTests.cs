using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9PostResolveSourceDiagnosticSelectorTests
{
    private static readonly AdaptiveCalibrationPreviewBudget Budget = new(
        AdaptiveCalibrationPreviewBudget.CurrentVersion,
        new string('c', 64), .95d, .01d, 1_000_000d, 2);

    private static V9PhysicalSourceResolveMetric Metric(
        string option = "T095_TF0", string grouping = "BLIND_FS_COMPLETE_LINK_CANDIDATES",
        decimal margin = .5m, long tp = 7505, long fp = 0,
        double? cp = .0222d, bool iid = false) =>
        new(option, .95m, 0m, margin, grouping, 11689,
            7841, 7727, tp, fp, 176, 11689 - tp - fp - 176,
            7727d / 7841d, (double)tp / 7841,
            tp + fp == 0 ? null : (double)fp / (tp + fp),
            .275d, 5556, new Dictionary<string, long>(),
            SyntheticGoldOracleGrouping: grouping.Contains("ORACLE", StringComparison.Ordinal),
            OperationallyCertified: false,
            ImpureBlindGroups: 0,
            ConditionalClusterCpFdrUpper: cp,
            BonferroniComparedConfigurations: 36,
            ClusterIidIndependentlyValidated: iid);

    [Test]
    public void Measured_95_percent_recall_zero_fp_is_not_certified_without_iid()
    {
        var report = V9PostResolveSourceDiagnosticSelector.Evaluate(
            [Metric(), Metric(grouping: "ORACLE_SYNTHETIC_GOLD_GROUPING_NOT_OPERATIONAL")],
            Budget);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.Options, Has.Count.EqualTo(1));
            Assert.That(report.Options[0].MeetsRecallFloor, Is.True);
            Assert.That(report.Options[0].MeetsPointFdrCeiling, Is.True);
            Assert.That(report.Options[0].MeetsConditionalClusterFdrCeiling, Is.False);
            Assert.That(report.Options[0].IdentityIidIndependentlyAttested, Is.False);
            Assert.That(report.Status, Is.EqualTo("NO_SUGGESTION_CLUSTER_INDEPENDENCE_UNPROVEN"));
            Assert.That(report.SuggestedOptionId, Is.Null);
            Assert.That(report.TestUsed, Is.False);
            Assert.That(report.AutomaticallyActivated, Is.False);
        }));
    }

    [Test]
    public void Actual_final_decision_and_bound_not_pairwise_or_oracle_drive_suggestion()
    {
        var report = V9PostResolveSourceDiagnosticSelector.Evaluate([
            Metric(grouping: "ORACLE_SYNTHETIC_GOLD_GROUPING_NOT_OPERATIONAL",
                cp: .0001d, iid: true),
            Metric(option: "LOWER", tp: 7300, cp: .0001d, iid: true),
            Metric(option: "T095", tp: 7505, cp: .005d, iid: true),
            Metric(option: "T090", tp: 7535, fp: 1, cp: .004d, iid: true),
            Metric(option: "NO_MARGIN", margin: 0m, tp: 7700, cp: .001d, iid: true),
        ], Budget);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.Options, Has.Count.EqualTo(4));
            Assert.That(report.Options.Single(x => x.OptionId == "NO_MARGIN")
                .MeetsPositiveMarginPolicy, Is.False);
            Assert.That(report.SuggestedOptionId, Is.EqualTo("T090"));
            Assert.That(report.SuggestedConflictMargin, Is.EqualTo(.5m));
            Assert.That(report.OperationalModelCreated, Is.False);
            Assert.That(report.AutomaticallyActivated, Is.False);
        }));
    }

    [Test]
    public void Empty_accepts_unknown_fdr_or_null_bound_never_passes()
    {
        var report = V9PostResolveSourceDiagnosticSelector.Evaluate([
            Metric(option:"NONE", tp:0, fp:0, cp:null),
            Metric(option:"NO_BOUND", cp:null, iid:true)
        ], Budget);
        Assert.That(report.SuggestedOptionId, Is.Null);
        Assert.That(report.Options.All(x => !x.EligibleForDescriptiveSelection), Is.True);
    }

    [Test]
    public void Mixed_denominator_duplicate_configuration_and_forged_rates_fail_closed()
    {
        Assert.That((Action)(() =>
            V9PostResolveSourceDiagnosticSelector.Evaluate(
                [Metric(), Metric()], Budget)), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() =>
            V9PostResolveSourceDiagnosticSelector.Evaluate(
                [Metric(), Metric(option:"DIFFERENT") with
                    { EligibleSourceObservationsBeforeBlocking = 7000 }], Budget)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() =>
            V9PostResolveSourceDiagnosticSelector.Evaluate(
                [Metric() with { GlobalSourceRecall = 1d }], Budget)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() =>
            V9PostResolveSourceDiagnosticSelector.Evaluate(
                [Metric() with { FdrAfterResolve = .9d }], Budget)),
            Throws.TypeOf<InvalidDataException>());
    }
}
