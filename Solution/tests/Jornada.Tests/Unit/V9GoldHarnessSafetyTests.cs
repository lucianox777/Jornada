using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9GoldHarnessSafetyTests
{
    [TestCase("Server=(localdb)\\MSSQLLocalDB;Database=JornadaLocal;Integrated Security=true")]
    [TestCase("Server=(localdb)\\JornadaV9Gold20261019_0000abcd;Database=JornadaLocal;Integrated Security=true")]
    [TestCase("Server=localhost;Database=JornadaHarness;Integrated Security=true")]
    [TestCase("Server=(localdb)\\JornadaV9Gold20261019_0000abcd;Database=JornadaHarness;User Id=fixture")]
    [TestCase("Server=(localdb)\\JornadaV9GoldAnything;Database=JornadaHarness;Integrated Security=true")]
    public void Rejects_original_remote_and_non_owned_database_before_connecting(string connection) =>
        Assert.Throws<InvalidOperationException>((Action)(() => V9GoldHarnessCommand.ValidateDisposableConnection(connection)));

    [TestCase("UPPER_TRAIN=0")]
    [TestCase("UPPER_TRAIN=0.0")]
    [TestCase("UPPER_TRAIN=0,7")]
    [TestCase("UPPER_TRAIN=invalid")]
    [TestCase("UPPER_TRAIN=")]
    [TestCase("OTHER=0.8")]
    public void Joint_upper_parser_fails_closed_for_invalid_manual_threshold(string argument) =>
        Assert.Throws<ArgumentException>((Action)(() =>
            Jornada.Linkage.Parameters.Worker.V9LowerNonMatchTrainCalibrator.ParseJointUpperArgument(argument)));

    [Test]
    public void Joint_upper_parser_accepts_invariant_decimal_candidate()
    {
        Assert.That(Jornada.Linkage.Parameters.Worker.V9LowerNonMatchTrainCalibrator
            .ParseJointUpperArgument("UPPER_TRAIN=0.85"), Is.EqualTo(0.85m));
        Assert.That(Jornada.Linkage.Parameters.Worker.V9LowerNonMatchTrainCalibrator
            .ParseJointUpperArgument("UPPER_TRAIN=1"), Is.EqualTo(1m));
        Assert.That(Jornada.Linkage.Parameters.Worker.V9LowerNonMatchTrainCalibrator
            .ParseJointUpperArgument("UPPER_TRAIN=1.0"), Is.EqualTo(1m));
    }

    private static string[] JointTrial(string order, string lower, string upper) =>
        [V9GoldHarnessCommand.JointTrainTrialFlag, "selection", "validation", "fit",
            "train.csv", "new-output", order, lower, upper];

    [Test]
    public void Joint_train_accepts_only_explicit_three_order_physical_pair()
    {
        foreach (var order in Jornada.Linkage.Parameters.Worker.V9LowerNonMatchTrainCalibrator.JointOrders)
            Assert.DoesNotThrow((Action)(() => V9GoldHarnessCommand.ValidateJointTrainTrialArguments(
                JointTrial(order, "LOWER_NONMATCH=0.1", "UPPER_TRAIN=1"))));
    }

    [TestCase("G0_G3", "LOWER_NONMATCH=0.1", "UPPER_TRAIN=0.9")]
    [TestCase("BASELINE_G0_G1_G2_G3", "LOWER_NONMATCH=0.9", "UPPER_TRAIN=0.9")]
    [TestCase("BASELINE_G0_G1_G2_G3", "LOWER_NONMATCH=0.91", "UPPER_TRAIN=0.9")]
    [TestCase("BASELINE_G0_G1_G2_G3", "LOWER_NONMATCH=0", "UPPER_TRAIN=0.9")]
    [TestCase("BASELINE_G0_G1_G2_G3", "LOWER_NONMATCH=0.1", "UPPER_TRAIN=1.01")]
    [TestCase("BASELINE_G0_G1_G2_G3", "LOWER_NONMATCH=0,1", "UPPER_TRAIN=0.9")]
    public void Joint_train_rejects_invalid_or_unregistered_cells(string order, string lower, string upper) =>
        Assert.Throws<ArgumentException>((Action)(() =>
            V9GoldHarnessCommand.ValidateJointTrainTrialArguments(JointTrial(order, lower, upper))));

    [Test]
    public void Joint_train_rejects_missing_or_duplicate_trial_options_before_sql()
    {
        var valid = JointTrial("BASELINE_G0_G1_G2_G3", "LOWER_NONMATCH=0.1", "UPPER_TRAIN=0.9");
        Assert.Throws<ArgumentException>((Action)(() =>
            V9GoldHarnessCommand.ValidateJointTrainTrialArguments(valid[..^1])));
        var duplicate = (string[])valid.Clone();
        duplicate[8] = duplicate[7];
        Assert.Throws<ArgumentException>((Action)(() =>
            V9GoldHarnessCommand.ValidateJointTrainTrialArguments(duplicate)));
    }


    [Test]
    public void Joint_train_recall_denominator_is_frozen_before_threshold_dependent_publication()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> waves =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["G0"] = ["A", "B", "A"],
                ["G1"] = ["A", "C"],
                ["G2"] = ["C", "A", "D"],
                ["G3"] = ["B", "D", "E"]
            };
        Assert.Multiple((Action)(() =>
        {
            Assert.That(V9GoldHarnessCommand.CountPreregisteredTrueLinkOpportunities(
                waves, ["G0", "G1", "G2", "G3"]), Is.EqualTo(5));
            Assert.That(V9GoldHarnessCommand.CountPreregisteredTrueLinkOpportunities(
                waves, ["G3", "G2", "G1", "G0"]), Is.EqualTo(6));
            // Duplicate A inside first G0 wave is not an earlier-wave link.
            Assert.That(V9GoldHarnessCommand.CountPreregisteredTrueLinkOpportunities(
                new Dictionary<string, IReadOnlyList<string>> { ["G0"] = ["A", "A"] },
                ["G0"]), Is.Zero);
        }));
    }

    [Test]
    public void Joint_train_denominator_rejects_missing_duplicate_and_corrupt_waves()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> waves =
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["G0"] = ["A"], ["G1"] = ["A"]
            };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldHarnessCommand.CountPreregisteredTrueLinkOpportunities(waves, ["G0"])));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldHarnessCommand.CountPreregisteredTrueLinkOpportunities(waves, ["G0", "G0"])));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldHarnessCommand.CountPreregisteredTrueLinkOpportunities(
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["G0"] = ["A"], ["G1"] = [""]
                }, ["G0", "G1"])));
    }

    [Test]
    public void Accepts_only_explicit_disposable_instance_contract_before_resident_marker_and_empty_checks() =>
        Assert.DoesNotThrow((Action)(() => V9GoldHarnessCommand.ValidateDisposableConnection(
            "Server=(localdb)\\JornadaV9Gold20261019_0000abcd;Database=JornadaHarness;Integrated Security=true")));
}
