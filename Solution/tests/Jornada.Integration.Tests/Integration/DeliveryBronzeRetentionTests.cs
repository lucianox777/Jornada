using System.Security.Cryptography;
using Jornada.Bronze.Storage;
using Jornada.Operations.Maintenance.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class DeliveryBronzeRetentionTests
{
    [Test]
    public async Task Expired_delivery_loses_payload_but_keeps_sql_lineage()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION.");
        var csb = new SqlConnectionStringBuilder(connectionString);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase) && !db.Contains("dev", StringComparison.OrdinalIgnoreCase) && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Banco de integração deve conter Test, Dev ou Local.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        Guid entregaId;
        await using (var pick = connection.CreateCommand())
        {
            pick.CommandText = "SELECT TOP(1) entrega_id FROM ingestao.entrega WHERE status='PROCESSADA' ORDER BY recebido_em;";
            entregaId = (Guid)(await pick.ExecuteScalarAsync() ?? throw new AssertionException("Seed sem Entrega PROCESSADA."));
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes("jornada-retention-test-" + Guid.NewGuid().ToString("N"));
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var tempRoot = Path.Combine(Path.GetTempPath(), "jornada-bronze-retention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var objectStore = new FileSystemBronzeObjectStore(tempRoot);
            var objectKey = objectStore.BuildObjectKey(sha);
            await using (var ms = new MemoryStream(bytes, writable: false))
                await objectStore.PutIfAbsentAsync(sha, ms, bytes.Length, CancellationToken.None);

            await using (var prepare = connection.CreateCommand())
            {
                prepare.CommandText = """
                    UPDATE ingestao.entrega SET recebido_em=DATEADD(DAY,-100,SYSUTCDATETIME()),ultima_atualizacao=SYSUTCDATETIME(),payload_sha256=@sha,bytes_recebidos=@len,status='PROCESSADA' WHERE entrega_id=@id;
                    UPDATE bronze.entrega_arquivo SET objeto_chave=@key,payload_sha256=@sha,tamanho_bytes=@len,recebido_em=DATEADD(DAY,-100,SYSUTCDATETIME()),estado_armazenamento='DISPONIVEL',expurgo_iniciado_em=NULL,expurgado_em=NULL,retencao_motivo=NULL WHERE entrega_id=@id;
                    """;
                prepare.Parameters.AddWithValue("@id", entregaId); prepare.Parameters.AddWithValue("@sha", sha);
                prepare.Parameters.AddWithValue("@key", objectKey); prepare.Parameters.AddWithValue("@len", bytes.Length);
                await prepare.ExecuteNonQueryAsync();
            }

            var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:Jornada"] = connectionString }).Build();
            var worker = new DeliveryBronzeRetentionWorker(cfg, objectStore,
                Options.Create(new DeliveryBronzeRetentionOptions { Enabled=true, RetentionDays=30, MaxRowsPerCycle=100, IntervalMinutes=60, ObjectLockTimeoutSeconds=1 }),
                NullLogger<DeliveryBronzeRetentionWorker>.Instance);
            await worker.RunCycleAsync(new DeliveryBronzeRetentionOptions { Enabled=true, RetentionDays=30, MaxRowsPerCycle=100, IntervalMinutes=60, ObjectLockTimeoutSeconds=1 }, CancellationToken.None);

            await using var verify = connection.CreateCommand();
            verify.CommandText = "SELECT estado_armazenamento,expurgado_em,(SELECT COUNT(*) FROM ingestao.entrega WHERE entrega_id=@id) FROM bronze.entrega_arquivo WHERE entrega_id=@id;";
            verify.Parameters.AddWithValue("@id", entregaId);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reader.GetString(0), Is.EqualTo("EXPURGADO"));
                Assert.That(reader.IsDBNull(1), Is.False);
                Assert.That(reader.GetInt32(2), Is.EqualTo(1), "A Entrega permanece no SQL para auditoria/linhagem.");
            });
            Assert.ThrowsAsync<BronzeObjectNotFoundException>(async () => await objectStore.OpenReadAsync(objectKey, CancellationToken.None));
        }
        finally { try { Directory.Delete(tempRoot, true); } catch { } }
    }

    [Test]
    public async Task Shared_object_is_preserved_while_another_delivery_remains_available()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION.");
        var csb = new SqlConnectionStringBuilder(connectionString);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase) && !db.Contains("dev", StringComparison.OrdinalIgnoreCase) && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Banco de integração deve conter Test, Dev ou Local.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        var ids = new List<Guid>();
        await using (var pick = connection.CreateCommand())
        {
            pick.CommandText = "SELECT TOP(2) entrega_id FROM ingestao.entrega WHERE status='PROCESSADA' ORDER BY recebido_em,entrega_id;";
            await using var reader = await pick.ExecuteReaderAsync();
            while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));
        }
        Assert.That(ids.Count, Is.GreaterThanOrEqualTo(2), "Fixture DEV deve possuir ao menos duas Entregas PROCESSADA para provar retenção Bronze compartilhada.");

        var bytes = System.Text.Encoding.UTF8.GetBytes("jornada-retention-shared-" + Guid.NewGuid().ToString("N"));
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var tempRoot = Path.Combine(Path.GetTempPath(), "jornada-bronze-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var store = new FileSystemBronzeObjectStore(tempRoot);
            var objectKey = store.BuildObjectKey(sha);
            await using (var ms = new MemoryStream(bytes, writable: false))
                await store.PutIfAbsentAsync(sha, ms, bytes.Length, CancellationToken.None);

            await using (var prepare = connection.CreateCommand())
            {
                prepare.CommandText = """
                    UPDATE ingestao.entrega SET recebido_em=DATEADD(DAY,-100,SYSUTCDATETIME()),ultima_atualizacao=SYSUTCDATETIME(),payload_sha256=@sha,bytes_recebidos=@len,status='PROCESSADA' WHERE entrega_id=@old;
                    UPDATE bronze.entrega_arquivo SET objeto_chave=@key,payload_sha256=@sha,tamanho_bytes=@len,recebido_em=DATEADD(DAY,-100,SYSUTCDATETIME()),estado_armazenamento='DISPONIVEL',expurgo_iniciado_em=NULL,expurgado_em=NULL,retencao_motivo=NULL WHERE entrega_id=@old;
                    UPDATE ingestao.entrega SET recebido_em=SYSUTCDATETIME(),ultima_atualizacao=SYSUTCDATETIME(),payload_sha256=@sha,bytes_recebidos=@len,status='PROCESSADA' WHERE entrega_id=@live;
                    UPDATE bronze.entrega_arquivo SET objeto_chave=@key,payload_sha256=@sha,tamanho_bytes=@len,recebido_em=SYSUTCDATETIME(),estado_armazenamento='DISPONIVEL',expurgo_iniciado_em=NULL,expurgado_em=NULL,retencao_motivo=NULL WHERE entrega_id=@live;
                    """;
                prepare.Parameters.AddWithValue("@old", ids[0]); prepare.Parameters.AddWithValue("@live", ids[1]);
                prepare.Parameters.AddWithValue("@sha", sha); prepare.Parameters.AddWithValue("@key", objectKey); prepare.Parameters.AddWithValue("@len", bytes.Length);
                await prepare.ExecuteNonQueryAsync();
            }

            var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:Jornada"] = connectionString }).Build();
            var opts = new DeliveryBronzeRetentionOptions { Enabled=true, RetentionDays=30, MaxRowsPerCycle=100, IntervalMinutes=60, ObjectLockTimeoutSeconds=1 };
            var worker = new DeliveryBronzeRetentionWorker(cfg, store, Options.Create(opts), NullLogger<DeliveryBronzeRetentionWorker>.Instance);
            await worker.RunCycleAsync(opts, CancellationToken.None);

            await using var verify = connection.CreateCommand();
            verify.CommandText = "SELECT entrega_id,estado_armazenamento FROM bronze.entrega_arquivo WHERE entrega_id IN(@old,@live) ORDER BY CASE WHEN entrega_id=@old THEN 0 ELSE 1 END;";
            verify.Parameters.AddWithValue("@old", ids[0]); verify.Parameters.AddWithValue("@live", ids[1]);
            await using var r = await verify.ExecuteReaderAsync();
            Assert.That(await r.ReadAsync(), Is.True); Assert.That(r.GetString(1), Is.EqualTo("EXPURGADO"));
            Assert.That(await r.ReadAsync(), Is.True); Assert.That(r.GetString(1), Is.EqualTo("DISPONIVEL"));
            await using var stillThere = await store.OpenReadAsync(objectKey, CancellationToken.None);
            Assert.That(stillThere.Length, Is.EqualTo(bytes.Length), "Objeto compartilhado deve permanecer enquanto houver referência DISPONIVEL.");
        }
        finally { try { Directory.Delete(tempRoot, true); } catch { } }
    }

}
