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

    [Test]
    public void Accepts_only_explicit_disposable_instance_contract_before_resident_marker_and_empty_checks() =>
        Assert.DoesNotThrow((Action)(() => V9GoldHarnessCommand.ValidateDisposableConnection(
            "Server=(localdb)\\JornadaV9Gold20261019_0000abcd;Database=JornadaHarness;Integrated Security=true")));
}
