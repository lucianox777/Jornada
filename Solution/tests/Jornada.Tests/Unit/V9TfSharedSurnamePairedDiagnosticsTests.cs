using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9TfSharedSurnamePairedDiagnosticsTests
{
    private const string Ruleset = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly string[] Suffixes = ["W0.5_M0", "W0.5_M1", "W1_M0", "W1_M1"];

    private static IReadOnlyList<V9FsAdaptiveOptionConfiguration> Options()
    {
        var options = new List<V9FsAdaptiveOptionConfiguration>
        {
            new("T0.95_TF0", .5m, .95m)
        };
        for (int i = 0; i < 36; i++)
            options.Add(new("CONTROL_" + i, .5m, .95m));
        foreach (var suffix in Suffixes)
        {
            V9FsUnitTfOption[] units =
            [
                new(CompositeNominalTfSideV9.Person, NominalFrequencyUnitV9.FirstName, 0m,
                    CompositeNominalTfAdjustmentModeV9.Symmetric),
                new(CompositeNominalTfSideV9.Person, NominalFrequencyUnitV9.LastContentSurname, .5m,
                    CompositeNominalTfAdjustmentModeV9.Symmetric),
                new(CompositeNominalTfSideV9.Mother, NominalFrequencyUnitV9.FirstName, 0m,
                    CompositeNominalTfAdjustmentModeV9.Symmetric),
                new(CompositeNominalTfSideV9.Mother, NominalFrequencyUnitV9.LastContentSurname, .5m,
                    CompositeNominalTfAdjustmentModeV9.Symmetric)
            ];
            options.Add(new("TF_TWO_PL_ML_" + suffix, .5m, .95m, UnitTfOptions: units));
            options.Add(new("TF_SHARED_PL_ML_" + suffix, .5m, .95m,
                UnitTfOptions: units, SharedSurnameMode:
                V9SharedSurnameModeV9.PersonSideOnlyWhenShared, EligibleForSelection: false));
        }
        return options;
    }

    private static V9PhysicalSourceResolveMetric Metric(string id, int falseAccepted = 1) => new(
        Ruleset + ":" + id, .95m, 0m, .5m,
        "BLIND_FS_COMPLETE_LINK_CANDIDATES", 200, 200, 200,
        160, falseAccepted, 0, 40 - falseAccepted,
        1, .8, falseAccepted / (160d + falseAccepted), .05, 100,
        new Dictionary<string, long>(), false, false, 0);

    private static (List<V9PhysicalSourceResolveMetric> Rows, Dictionary<string, long> Counts) Evidence()
    {
        var rows = new List<V9PhysicalSourceResolveMetric>();
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var suffix in Suffixes)
        {
            var bilateral = Metric("TF_TWO_PL_ML_" + suffix);
            var shared = Metric("TF_SHARED_PL_ML_" + suffix, falseAccepted: 2);
            rows.Add(bilateral);
            rows.Add(shared);
            counts.Add(bilateral.OptionId, 7);
            counts.Add(shared.OptionId, 7);
        }
        return (rows, counts);
    }

    private static IReadOnlyDictionary<string, string> CandidateDigests() =>
        new Dictionary<string, string>(StringComparer.Ordinal) { [Ruleset] = new string('f', 64) };

    [Test]
    public void Physical_budget_counts_all_45_but_selection_multiplicity_counts_only_41()
    {
        var sha = new string('a', 64);
        var plan = new V9AdaptiveCalibrationPlan(
            sha, new V9TrainBlockingDiscoveryOptions(), Options(), [1m, 2m],
            new(AdaptiveCalibrationPreviewBudget.CurrentVersion, sha, .95, .01, 100, 2),
            new(.95, .01, 2), 100, 1000,
            new(MaximumPrefixLength: 4, EndToEndRepetitions: 20,
                FsBlindGroupingOnly: true, MaximumBlindClusterSize: 16),
            PreviouslyComparedValidationConfigurations: 30);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(V9AdaptiveCalibrationCoordinator.CountComparedConfigurations(plan, 2), Is.EqualTo(210));
            Assert.That(V9AdaptiveCalibrationCoordinator.CountSelectableConfigurations(plan, 2), Is.EqualTo(194));
        }));
    }

    [Test]
    public void All_four_diagnostic_arms_use_the_same_SQL_ruleset_and_measured_pair_count()
    {
        var (rows, counts) = Evidence();
        var report = V9TfSharedSurnamePairedDiagnostics.Build(Options(), rows, counts, CandidateDigests());
        Assert.Multiple((Action)(() =>
        {
            Assert.That(report, Has.Count.EqualTo(4));
            Assert.That(report.All(x => !x.EligibleForSelection && x.SharedSurnameCountMeasured), Is.True);
            Assert.That(report.All(x => x.SharedSurnameCandidatePairs == 7 && x.RulesetSha256 == Ruleset
                && x.CandidatePairsSha256 == new string('f', 64)), Is.True);
            Assert.That(report.All(x => x.DeltaFalseAcceptedSources == 1), Is.True);
            Assert.That(report.All(x => !x.FalseCreationsMeasured
                && x.SharedFalseCreations is null && x.BilateralFalseCreations is null), Is.True);
        }));
    }

    [Test]
    public void Count_mismatch_is_never_replaced_with_an_estimate()
    {
        var (rows, counts) = Evidence();
        counts[rows[0].OptionId] = 8;
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9TfSharedSurnamePairedDiagnostics.Build(Options(), rows, counts, CandidateDigests())));
    }

    [Test]
    public void Other_ruleset_or_missing_control_cannot_be_used_for_a_pair()
    {
        var (rows, counts) = Evidence();
        var changed = rows[0] with { OptionId = new string('b', 64) + ":" + "TF_TWO_PL_ML_" + Suffixes[0] };
        rows[0] = changed;
        counts.Add(changed.OptionId, 7);
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9TfSharedSurnamePairedDiagnostics.Build(Options(), rows, counts, CandidateDigests())));
    }

    [Test]
    public void Missing_SQL_edge_digest_never_produces_a_physical_pair_receipt()
    {
        var (rows, counts) = Evidence();
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9TfSharedSurnamePairedDiagnostics.Build(Options(), rows, counts,
                new Dictionary<string, string>())));
    }

    [Test]
    public void Four_arms_must_be_ineligible_and_match_the_bilateral_fingerprint()
    {
        var options = Options().ToArray();
        V9TfSharedSurnamePairedDiagnostics.ValidateOptions(options);
        var diagnosticIndex = Array.FindIndex(options, x => !x.EligibleForSelection);
        options[diagnosticIndex] = options[diagnosticIndex] with { EligibleForSelection = true };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9TfSharedSurnamePairedDiagnostics.ValidateOptions(options)));
    }
}
