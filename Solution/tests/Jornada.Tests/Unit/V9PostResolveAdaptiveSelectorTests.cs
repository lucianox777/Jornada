using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9PostResolveAdaptiveSelectorTests
{
    private static readonly AdaptiveCalibrationPreviewBudget Budget = new(
        AdaptiveCalibrationPreviewBudget.CurrentVersion, new string('a', 64), .95, .01, 100, 2);

    private static V9PhysicalSourceResolveMetric Row(string id, long tp = 96, long fp = 0,
        string grouping = "RAW_SQL_OBSERVATION_CANDIDATES", bool oracle = false) => new(
        id, .99m, 0m, .5m, grouping, 100, 100, 100, tp, fp, 0, 100 - tp - fp,
        1, tp / 100d, tp + fp == 0 ? null : fp / (double)(tp + fp), .2, 50,
        new Dictionary<string, long> { ["ALL_DECISIONS"] = 100 }, oracle, false, 0);

    private static T? Throws<T>(Action action) where T : Exception => Assert.Throws<T>(action);

    private static V9PostResolveSelectionReport Evaluate(string partition, bool usesTest,
        AdaptiveCalibrationPreviewBudget budget, IReadOnlyList<V9PhysicalSourceResolveMetric> rows) =>
        V9PostResolveAdaptiveSelector.EvaluateValidation(partition, usesTest, budget, rows,
            rows.Select(x => x.OptionId).Distinct().ToDictionary(x => x, _ => 10d));
    [Test]
    public void Selects_final_source_configuration_excludes_superior_oracle_and_never_activates()
    {
        var selected = Row("source");
        var result = Evaluate("VALIDATION", false, Budget,
            [Row("oracle", 100, grouping: "ORACLE_SYNTHETIC_GOLD_GROUPING_NOT_OPERATIONAL", oracle: true),
                selected, Row("worse", 95)]);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.BestDescriptiveConfiguration!.OptionId, Is.EqualTo(selected.OptionId));
            Assert.That(result.BestDescriptiveConfiguration.TrueAcceptedSourceObservations, Is.EqualTo(96));
            Assert.That(result.SuggestedDiagnosticConfiguration, Is.Null);
            Assert.That(result.ExcludedOracleConfigurations, Is.EqualTo(1));
            Assert.That(result.OperationalModelCreated, Is.False);
            Assert.That(result.AutomaticActivationAllowed, Is.False);
            Assert.That(result.TestUsed, Is.False);
            Assert.That(result.Status, Is.EqualTo("NO_SOURCE_SUGGESTION_WITHOUT_SUPPORTED_CLUSTER_FDR_BOUND"));
        }));
    }

    [Test]
    public void Missing_or_excessive_fanout_never_suggests_configuration()
    {
        Assert.That(V9PostResolveAdaptiveSelector.EvaluateValidation("VALIDATION", false, Budget,
            [Row("one")]).BestDescriptiveConfiguration, Is.Null);
        Assert.That(V9PostResolveAdaptiveSelector.EvaluateValidation("VALIDATION", false, Budget,
            [Row("one")], new Dictionary<string, double> { ["one"] = 101 })
            .BestDescriptiveConfiguration, Is.Null);
    }

    [Test]
    public void False_acceptances_over_budget_are_rejected_even_with_full_recall()
    {
        var row = Row("unsafe", 98, 2);
        Assert.That(Evaluate("VALIDATION", false, Budget, [row])
            .BestDescriptiveConfiguration, Is.Null);
    }

    [Test]
    public void Counts_global_source_recall_including_preblocking_losses()
    {
        var row = Row("lost", 94) with { EligibleSourceObservationsWithTrueSqlCandidate = 94, BlockingSourceRecall = .94 };
        var result = Evaluate("VALIDATION", false, Budget, [row]);
        Assert.That(result.BestDescriptiveConfiguration, Is.Null);
    }

    [Test]
    public void Rejects_unverified_reported_ratios_instead_of_trusting_them()
    {
        Throws<InvalidDataException>(() => Evaluate(
            "VALIDATION", false, Budget, [Row("lie", 10) with { GlobalSourceRecall = 1 }]));
    }

    [Test]
    public void Rejects_different_cohort_and_duplicate_configuration()
    {
        Throws<InvalidDataException>(() => Evaluate(
            "VALIDATION", false, Budget, [Row("one"), Row("two") with { PreBlockingBaseIdentities = 49 }]));
        Throws<InvalidDataException>(() => Evaluate(
            "VALIDATION", false, Budget, [Row("one"), Row("one")]));
    }

    [TestCase("TEST", false)]
    [TestCase("VALIDATION", true)]
    [TestCase("TRAIN", false)]
    public void Rejects_test_and_training_selection(string partition, bool usesTest) =>
        Throws<InvalidDataException>(() => Evaluate(
            partition, usesTest, Budget, [Row("one")]));

    [Test]
    public void Fingerprint_and_selection_are_invariant_to_enumeration_order()
    {
        var first = Row("b");
        var second = Row("a");
        var a = Evaluate("VALIDATION", false, Budget, [first, second]);
        var b = Evaluate("VALIDATION", false, Budget, [second, first]);
        Assert.That(a.EvidenceFingerprintSha256, Is.EqualTo(b.EvidenceFingerprintSha256));
        Assert.That(a.Status, Is.EqualTo(b.Status));
        Assert.That(a.BestDescriptiveConfiguration!.OptionId, Is.EqualTo(b.BestDescriptiveConfiguration!.OptionId));
        Assert.That(a.BestDescriptiveConfiguration!.OptionId, Is.EqualTo("a"));
    }

    [Test]
    public void Suggestion_requires_independent_cluster_support_and_comparison_adjustment()
    {
        var row = Row("safe", grouping: "BLIND_FS_COMPLETE_LINK_CANDIDATES") with { ClusterIidIndependentlyValidated = true,
            ConditionalClusterCpFdrUpper = .005, BonferroniComparedConfigurations = 1 };
        Assert.That(Evaluate("VALIDATION", false, Budget, [row]).SuggestedDiagnosticConfiguration,
            Has.Property(nameof(V9PhysicalSourceResolveMetric.OptionId)).EqualTo(row.OptionId));
        Assert.That(Evaluate("VALIDATION", false, Budget, [row with
            { BonferroniComparedConfigurations = 0 }]).SuggestedDiagnosticConfiguration, Is.Null);
        Assert.That(Evaluate("VALIDATION", false, Budget, [row with
            { ClusterIidIndependentlyValidated = false }]).SuggestedDiagnosticConfiguration, Is.Null);
        Assert.That(Evaluate("VALIDATION", false, Budget, [row with
            { ConflictMargin = 0 }]).SuggestedDiagnosticConfiguration, Is.Null);
        Assert.That(Evaluate("VALIDATION", false, Budget, [row with
            { CandidateGrouping = "RAW_SQL_OBSERVATION_CANDIDATES" }]).SuggestedDiagnosticConfiguration, Is.Null);
        Assert.That(Evaluate("VALIDATION", false, Budget, [row with
            { CandidateGrouping = "BLIND_EXACT_PERSON_MOTHER_DOB_GROUPING" }]).SuggestedDiagnosticConfiguration, Is.Null);
    }

    [Test]
    public void Report_freezes_caller_owned_reason_counts_and_preserves_its_digest()
    {
        var reasons = new Dictionary<string, long> { ["RESOLVED"] = 96, ["UNRESOLVED"] = 4 };
        var input = Row("one") with { ReasonsByResolveStatus = reasons };
        var result = Evaluate("VALIDATION", false, Budget, [input]);
        var fingerprint = result.EvidenceFingerprintSha256;
        reasons["RESOLVED"] = 1;
        reasons.Clear();
        Assert.That(result.BestDescriptiveConfiguration!.ReasonsByResolveStatus["RESOLVED"], Is.EqualTo(96));
        Assert.That(result.BestDescriptiveConfiguration.ReasonsByResolveStatus.Values.Sum(), Is.EqualTo(100));
        Assert.That(result.EvidenceFingerprintSha256, Is.EqualTo(fingerprint));
        Assert.That((Action)(() => ((IDictionary<string, long>)result.BestDescriptiveConfiguration.ReasonsByResolveStatus)
            .Add("MUTATED", 1)), NUnit.Framework.Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void Rejects_invalid_parameter_ranges_and_unreconciled_reason_counts()
    {
        Throws<InvalidDataException>(() => Evaluate("VALIDATION", false, Budget,
            [Row("bad") with { TfWeight = 1.01m }]));
        Throws<InvalidDataException>(() => Evaluate("VALIDATION", false, Budget,
            [Row("bad") with { ConflictMargin = 1001m }]));
        Throws<InvalidDataException>(() => Evaluate("VALIDATION", false, Budget,
            [Row("bad") with { ReasonsByResolveStatus = new Dictionary<string, long> { ["WRONG"] = 99 } }]));
    }

    [Test]
    public void Rejects_bound_below_observed_fdr_and_reused_option_with_different_parameters()
    {
        Throws<InvalidDataException>(() => Evaluate("VALIDATION", false, Budget,
            [Row("one", 98, 2) with { ConditionalClusterCpFdrUpper = .001 }]));
        Throws<InvalidDataException>(() => Evaluate("VALIDATION", false, Budget,
            [Row("one"), Row("one", grouping: "BLIND_FS_COMPLETE_LINK_CANDIDATES") with { TfWeight = 1 }]));
    }
}
