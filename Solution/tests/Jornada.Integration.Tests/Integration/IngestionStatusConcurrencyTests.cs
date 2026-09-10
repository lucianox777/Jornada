using System.Data;
using Jornada.Api;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class IngestionStatusConcurrencyTests
{
    private static readonly Guid EntregaId = Guid.Parse("20000000-0000-4000-8000-000000000001");
    private static readonly Guid LoteId = Guid.Parse("20000000-0000-4000-8000-000000000002");

    [Test]
    public async Task Status_polling_does_not_deadlock_with_processor_lote_then_entrega_lock_order()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareAsync(connectionString);

        var service = new SqlIngestionService(new OperationalSqlAdapter(connectionString), null!);
        var context = new AccessContext(Guid.Empty, AccessCredentialType.GESTOR, "SEHAB", "SEHAB", null, [], []);

        await using var writer = new SqlConnection(connectionString);
        await writer.OpenAsync();
        await using var tx = (SqlTransaction)await writer.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        await using (var lockLote = writer.CreateCommand())
        {
            lockLote.Transaction = tx;
            lockLote.CommandText = "UPDATE ingestao.lote SET atualizado_em=SYSDATETIMEOFFSET() WHERE lote_id=@lote_id;";
            lockLote.Parameters.AddWithValue("@lote_id", LoteId);
            Assert.That(await lockLote.ExecuteNonQueryAsync(), Is.EqualTo(1));
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var statusTask = service.GetStatusAsync(context, EntregaId, cts.Token);

        // Dá tempo para a leitura chegar à consulta de Lote. Na implementação antiga, a mesma
        // instrução ainda mantinha lock na Entrega, formando o ciclo com lote -> entrega do Processor.
        await Task.Delay(300, cts.Token);

        await using (var finishEntrega = writer.CreateCommand())
        {
            finishEntrega.Transaction = tx;
            finishEntrega.CommandText = "UPDATE ingestao.entrega SET ultima_atualizacao=SYSDATETIMEOFFSET() WHERE entrega_id=@entrega_id;";
            finishEntrega.Parameters.AddWithValue("@entrega_id", EntregaId);
            Assert.That(await finishEntrega.ExecuteNonQueryAsync(cts.Token), Is.EqualTo(1));
        }

        await tx.CommitAsync(cts.Token);
        var response = await statusTask;

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.EntregaId, Is.EqualTo(EntregaId));
        Assert.That(response.Status, Is.EqualTo("PROCESSANDO"));
    }

    [Test]
    public async Task Query_split_preserves_latest_lote_error()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareAsync(connectionString);

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE ingestao.lote
                   SET status='REJEITADO',erro_codigo='TESTE_STATUS_ERRO',atualizado_em=SYSDATETIMEOFFSET()
                 WHERE lote_id=@lote_id;
                UPDATE ingestao.entrega
                   SET status='REJEITADA',ultima_atualizacao=SYSDATETIMEOFFSET()
                 WHERE entrega_id=@entrega_id;
                """;
            command.Parameters.AddWithValue("@lote_id", LoteId);
            command.Parameters.AddWithValue("@entrega_id", EntregaId);
            await command.ExecuteNonQueryAsync();
        }

        var service = new SqlIngestionService(new OperationalSqlAdapter(connectionString), null!);
        var context = new AccessContext(Guid.Empty, AccessCredentialType.GESTOR, "SEHAB", "SEHAB", null, [], []);
        var response = await service.GetStatusAsync(context, EntregaId, CancellationToken.None);

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Status, Is.EqualTo("REJEITADA"));
        Assert.That(response.Erro, Is.EqualTo("TESTE_STATUS_ERRO"));
    }

    private static async Task PrepareAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ingestao.lote
               SET status='PROCESSANDO',erro_codigo=NULL,atualizado_em=SYSDATETIMEOFFSET()
             WHERE lote_id=@lote_id;
            UPDATE ingestao.entrega
               SET status='PROCESSANDO',ultima_atualizacao=SYSDATETIMEOFFSET()
             WHERE entrega_id=@entrega_id;
            """;
        command.Parameters.AddWithValue("@lote_id", LoteId);
        command.Parameters.AddWithValue("@entrega_id", EntregaId);
        await command.ExecuteNonQueryAsync();
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION.");
        var csb = new SqlConnectionStringBuilder(connectionString!);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("dev", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Banco de integração deve conter Test, Dev ou Local.");
        return connectionString!;
    }
}
