namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class OperationalMonitorLeaseHeartbeatContractTests
{
    [Test]
    public void Active_lots_use_the_live_heartbeat_of_the_current_fenced_lease()
    {
        var root = FindRepositoryRoot();
        var monitor = File.ReadAllText(Path.Combine(
            root, "Solution", "src", "Jornada.Api", "OperationalMonitor.cs"));
        var start = monitor.IndexOf("SELECT TOP(20) l.lote_id", StringComparison.Ordinal);
        var end = monitor.IndexOf("SELECT TOP(20) e.entrega_id", start, StringComparison.Ordinal);

        Assert.That(start, Is.GreaterThanOrEqualTo(0), "Monitor deve consultar os lotes ativos.");
        Assert.That(end, Is.GreaterThan(start));
        var sql = monitor[start..end];

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("COALESCE(h.heartbeat_em,l.heartbeat_em) AS heartbeat_em"),
                "O heartbeat atual prevalece; snapshot do Lote so quando nao houver lease correspondente.");
            Assert.That(sql, Does.Contain("LEFT JOIN ingestao.lote_heartbeat h"));
            Assert.That(sql, Does.Contain("h.lote_id=l.lote_id"));
            Assert.That(sql, Does.Contain("h.lease_id=l.lease_id"),
                "Um heartbeat de lease anterior nunca deve ser exibido como o atual.");
            Assert.That(sql, Does.Contain("h.lease_owner=l.lease_owner"));
            Assert.That(sql, Does.Contain("l.status IN(N'VALIDANDO',N'PROCESSANDO')"));
            Assert.That(sql, Does.Contain("ORDER BY l.lease_adquirido_em,l.lote_id"));
            Assert.That(monitor, Does.Contain("ReadNullableDateTimeOffset(reader, 9)"),
                "A coluna de heartbeat deve continuar na mesma posicao do DTO da API.");
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

        throw new DirectoryNotFoundException("Raiz do repositorio Jornada nao encontrada.");
    }
}
