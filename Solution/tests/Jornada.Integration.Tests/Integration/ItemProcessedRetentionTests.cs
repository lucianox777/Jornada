using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class ItemProcessedRetentionTests
{
    [Test]
    public async Task Retention_preserves_latest_retransmission_for_origin()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");

        var csb = new SqlConnectionStringBuilder(connectionString);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase) && !db.Contains("dev", StringComparison.OrdinalIgnoreCase) && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            long pessoaOrigemId;
            Guid lote1;
            Guid lote2;
            await using (var ids = connection.CreateCommand())
            {
                ids.Transaction = tx;
                ids.CommandText = """
                    SELECT TOP(1) pessoa_origem_id FROM silver.pessoa_origem ORDER BY pessoa_origem_id;
                    SELECT TOP(2) lote_id FROM ingestao.lote ORDER BY criado_em,lote_id;
                    """;
                await using var reader = await ids.ExecuteReaderAsync();
                Assert.That(await reader.ReadAsync(), Is.True);
                pessoaOrigemId = reader.GetInt64(0);
                Assert.That(await reader.NextResultAsync(), Is.True);
                Assert.That(await reader.ReadAsync(), Is.True); lote1 = reader.GetGuid(0);
                Assert.That(await reader.ReadAsync(), Is.True); lote2 = reader.GetGuid(0);
            }

            var code = "TEST_RET_" + Guid.NewGuid().ToString("N")[..12];
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT ingestao.item_processado(lote_id,classe_item,pessoa_origem_id,codigo_origem,resultado,versao_interna,conteudo_hash,data_referencia,processado_em)
                    VALUES(@l1,'PESSOA',@po,@c,'RETRANSMITIDO',1,REPLICATE('a',64),'2026-01-01T00:00:00+00:00','2026-01-01T01:00:00+00:00'),
                          (@l2,'PESSOA',@po,@c,'RETRANSMITIDO',1,REPLICATE('a',64),'2026-01-01T00:00:00+00:00','2026-01-02T01:00:00+00:00');
                    """;
                insert.Parameters.AddWithValue("@l1", lote1);
                insert.Parameters.AddWithValue("@l2", lote2);
                insert.Parameters.AddWithValue("@po", pessoaOrigemId);
                insert.Parameters.AddWithValue("@c", code);
                await insert.ExecuteNonQueryAsync();
            }

            await using (var proc = connection.CreateCommand())
            {
                proc.Transaction = tx;
                proc.CommandType = System.Data.CommandType.StoredProcedure;
                proc.CommandText = "ingestao.sp_consolidar_expurgar_item_processado";
                proc.Parameters.AddWithValue("@cutoff", new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
                proc.Parameters.AddWithValue("@max_rows", 100000);
                await proc.ExecuteNonQueryAsync();
            }

            await using var check = connection.CreateCommand();
            check.Transaction = tx;
            check.CommandText = """
                SELECT COUNT(*), MAX(processado_em)
                FROM ingestao.item_processado
                WHERE classe_item='PESSOA' AND pessoa_origem_id=@po AND codigo_origem=@c AND resultado='RETRANSMITIDO';
                """;
            check.Parameters.AddWithValue("@po", pessoaOrigemId);
            check.Parameters.AddWithValue("@c", code);
            await using var r = await check.ExecuteReaderAsync();
            Assert.That(await r.ReadAsync(), Is.True);
            Assert.That(r.GetInt32(0), Is.EqualTo(1));
            Assert.That(r.GetDateTimeOffset(1), Is.EqualTo(new DateTimeOffset(2026, 1, 2, 1, 0, 0, TimeSpan.Zero)));
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }
}
