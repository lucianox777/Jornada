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
            Assert.That(method, Does.Contain("ingestao.lote_heartbeat"),
                "PROCESSANDO exige lease renovado na linha independente.");
            Assert.That(reservation, Does.Contain("UPDATE ingestao.lote_heartbeat"),
                "O heartbeat não pode atualizar a linha com FKs longamente bloqueadas.");
            Assert.That(reservation, Does.Contain("COALESCE(h.lease_expira_em,l.lease_expira_em)"),
                "A recuperação deve usar o heartbeat vivo antes do snapshot inicial.");

            var persistence = File.ReadAllText(Path.Combine(
                root, "Solution", "src", "Jornada.Processor.Worker", "SqlProcessorRepository.Persistence.cs"));
            var shortCommit = persistence.IndexOf("await transition.CommitAsync(ct)", StringComparison.Ordinal);
            var longTransaction = persistence.IndexOf(
                "connection.BeginTransactionAsync(IsolationLevel.Serializable, ct)", StringComparison.Ordinal);
            Assert.That(shortCommit, Is.GreaterThan(0));
            Assert.That(longTransaction, Is.GreaterThan(shortCommit),
                "A transição do Lote deve terminar antes de abrir a transação de Silver/Gold.");
            Assert.That(persistence, Does.Contain("DELETE FROM ingestao.lote_heartbeat"),
                "Finalização e remoção do heartbeat devem estar no mesmo commit.");
        });
    }

    [Test]
    public void Watchdogs_use_the_live_lease_expiration_and_match_the_fencing_token()
    {
        var root = FindRepositoryRoot();
        var watchdog = File.ReadAllText(Path.Combine(
            root, "Solution", "src", "Jornada.Operations.Maintenance.Worker", "PipelineWatchdog.cs"));
        var observability = File.ReadAllText(Path.Combine(
            root, "Solution", "database", "Jornada_HML_Observabilidade.sql"));

        Assert.Multiple(() =>
        {
            Assert.That(watchdog, Does.Contain("COALESCE(h.lease_expira_em,l.lease_expira_em)"));
            Assert.That(observability, Does.Contain("COALESCE(h.lease_expira_em,l.lease_expira_em)"));
            Assert.That(watchdog, Does.Contain("h.lease_id=l.lease_id"));
            Assert.That(observability, Does.Contain("h.lease_id=l.lease_id"));
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
