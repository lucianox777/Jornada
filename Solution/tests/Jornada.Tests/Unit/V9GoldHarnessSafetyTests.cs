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

    [Test]
    public void Accepts_only_explicit_disposable_instance_contract_before_resident_marker_and_empty_checks() =>
        Assert.DoesNotThrow((Action)(() => V9GoldHarnessCommand.ValidateDisposableConnection(
            "Server=(localdb)\\JornadaV9Gold20261019_0000abcd;Database=JornadaHarness;Integrated Security=true")));
}
