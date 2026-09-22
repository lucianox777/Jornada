namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class ProcessorStatusLockContractTests
{
    [Test]
    public void Long_processing_transition_does_not_lock_delivery_and_status_error_read_is_nonblocking()
    {
        var root = FindRepositoryRoot();
        var reservation = File.ReadAllText(Path.Combine(
            root, "Solution", "src", "Jornada.Processor.Worker", "SqlProcessorRepository.Reservation.cs"));
        var api = File.ReadAllText(Path.Combine(
            root, "Solution", "src", "Jornada.Api", "SqlApiServices.cs"));

        var start = reservation.IndexOf("private static async Task SetProcessingAsync", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        var end = reservation.IndexOf("private static void AddLeaseParameters", start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start));
        var method = reservation[start..end];

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("UPDATE ingestao.lote SET status='PROCESSANDO'"));
            Assert.That(method, Does.Not.Contain("UPDATE ingestao.entrega"));
            Assert.That(api, Does.Contain("FROM ingestao.lote l WITH (READPAST)"));
        });
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt"))
                && Directory.Exists(Path.Combine(current.FullName, "Solution")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Raiz do repositório Jornada não encontrada.");
    }
}
