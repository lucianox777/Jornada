using System.Data;
using System.Security.Cryptography;
using Jornada.Bronze.Maintenance.Worker;
using Jornada.Bronze.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class BronzeMaintenanceTests
{
    [Test]
    public async Task Orphan_gc_deletes_only_when_object_reference_lock_is_exclusive_and_count_is_zero()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");

        var csb = new SqlConnectionStringBuilder(connectionString);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase) && !db.Contains("dev", StringComparison.OrdinalIgnoreCase) && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.");

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
            await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["ConnectionStrings:Jornada"] = connectionString
        }).Build();
        var repository = new BronzeMaintenanceRepository(config);
        var root = Path.Combine(Path.GetTempPath(), "jornada-bronze-gc-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemBronzeObjectStore(root);
            var bytes = "orphan bronze"u8.ToArray();
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            await using var source = new MemoryStream(bytes, writable:false);
            var reference = await store.PutIfAbsentAsync(hash, source, bytes.Length, CancellationToken.None);
            var candidate = new BronzeStoredObject(reference.ObjectKey, reference.Length, DateTimeOffset.UtcNow.AddDays(-2));

            var deleted = await repository.DeleteIfStillUnreferencedAsync(candidate, store, 1, CancellationToken.None);
            Assert.That(deleted.Deleted, Is.True);
            Assert.ThrowsAsync<BronzeObjectNotFoundException>(async () =>
            {
                await using var _ = await store.OpenReadAsync(reference.ObjectKey, CancellationToken.None);
            });
        }
        finally { try { Directory.Delete(root, recursive:true); } catch { } }
    }

    [Test]
    public async Task Shared_reference_lock_blocks_orphan_delete_until_api_reference_window_finishes()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");

        var csb = new SqlConnectionStringBuilder(connectionString);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase) && !db.Contains("dev", StringComparison.OrdinalIgnoreCase) && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.");

        await using (var schemaConnection = new SqlConnection(connectionString))
        {
            await schemaConnection.OpenAsync();
            var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
            await SqlBatchRunner.ExecuteFileAsync(schemaConnection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["ConnectionStrings:Jornada"] = connectionString
        }).Build();
        var repository = new BronzeMaintenanceRepository(config);
        var root = Path.Combine(Path.GetTempPath(), "jornada-bronze-gc-lock-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemBronzeObjectStore(root);
            var bytes = "protected orphan"u8.ToArray();
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            await using var source = new MemoryStream(bytes, writable:false);
            var reference = await store.PutIfAbsentAsync(hash, source, bytes.Length, CancellationToken.None);
            var candidate = new BronzeStoredObject(reference.ObjectKey, reference.Length, DateTimeOffset.UtcNow.AddDays(-2));

            var holderCsb = new SqlConnectionStringBuilder(connectionString) { Pooling = false, Enlist = false };
            await using var holder = new SqlConnection(holderCsb.ConnectionString);
            await holder.OpenAsync();
            await using (var lockCommand = holder.CreateCommand())
            {
                lockCommand.CommandText = "DECLARE @rc int; EXEC @rc=sys.sp_getapplock @Resource=@r,@LockMode='Shared',@LockOwner='Session',@LockTimeout=0; SELECT @rc;";
                lockCommand.Parameters.Add(new SqlParameter("@r", SqlDbType.NVarChar,255) { Value = BronzeObjectCoordination.LockResourceForSha256(hash) });
                Assert.That(Convert.ToInt32(await lockCommand.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture), Is.GreaterThanOrEqualTo(0));
            }

            var deletedWhileShared = await repository.DeleteIfStillUnreferencedAsync(candidate, store, 0, CancellationToken.None);
            Assert.That(deletedWhileShared.Deleted, Is.False);
            Assert.That(deletedWhileShared.LockMiss, Is.True);
            await using var stillThere = await store.OpenReadAsync(reference.ObjectKey, CancellationToken.None);
        }
        finally { try { Directory.Delete(root, recursive:true); } catch { } }
    }
}
