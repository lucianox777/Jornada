using System.IO.Compression;
using System.Security.Cryptography;
using Jornada.Bronze.Storage;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Jornada.Processor.Worker;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class BronzeReplayInvariantTests
{
    private const string FixtureCpf = "70819234532";
    private const string FixtureRecord = "E2E-AA01-2026-000001";

    [Test]
    public async Task Bronze_and_identity_ledger_reconstruct_current_silver_gold_and_serving_projections()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareDatabaseAsync(connectionString);

        var bronzeRoot = Path.Combine(Path.GetTempPath(), "jornada-bronze-replay", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bronzeRoot);
        try
        {
            var payload = BuildFixtureZip();
            var sha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
            var store = new FileSystemBronzeObjectStore(bronzeRoot);
            await using (var write = new MemoryStream(payload, writable: false))
                await store.PutIfAbsentAsync(sha256, write, payload.LongLength, CancellationToken.None);
            var objectKey = store.BuildObjectKey(sha256);
            var fileName = $"ENTREGA_SEHAB_SEHAB_v2_{sha256}.zip";

            var (deliveryId, lotId) = await PointSeedDeliveryAtBronzeAsync(
                connectionString, sha256, objectKey, fileName, payload.LongLength);

            var repository = CreateRepository(connectionString);
            var parser = new IngestionPackageParser(AppContext.BaseDirectory, new ProcessorOptions());

            var first = await repository.ReserveNextAsync("bronze-replay-first", TimeSpan.FromMinutes(2), CancellationToken.None);
            Assert.That(first, Is.Not.Null);
            Assert.That(first!.LoteId, Is.EqualTo(lotId));
            await ProcessFromBronzeAsync(repository, parser, store, first);

            var canonicalUuid = await ScalarGuidAsync(connectionString,
                "SELECT pessoa_uuid FROM identidade.cpf_ancora WHERE cpf=@cpf", ("@cpf", FixtureCpf));
            Assert.That(canonicalUuid, Is.Not.EqualTo(Guid.Empty));

            var firstFactObservation = await ScalarLongAsync(connectionString,
                "SELECT registro_observacao_id FROM silver.registro_observacao WHERE lote_id=@lote AND codigo_registro_origem=@codigo",
                ("@lote", lotId), ("@codigo", FixtureRecord));
            Assert.That(firstFactObservation, Is.GreaterThan(0));
            await AssertCurrentProjectionAsync(connectionString, deliveryId, lotId, canonicalUuid);
            await store.VerifyAsync(objectKey, sha256, payload.LongLength, CancellationToken.None);

            await RemoveDerivedProjectionAndReopenAsync(
                connectionString, deliveryId, lotId, canonicalUuid, firstFactObservation);

            Assert.That(await ScalarCountAsync(connectionString,
                "SELECT COUNT(*) FROM silver.registro_observacao WHERE lote_id=@lote AND codigo_registro_origem=@codigo",
                ("@lote", lotId), ("@codigo", FixtureRecord)), Is.Zero);
            Assert.That(await ScalarCountAsync(connectionString,
                "SELECT COUNT(*) FROM gold.pessoa WHERE pessoa_uuid=@uuid", ("@uuid", canonicalUuid)), Is.Zero);
            Assert.That(await ScalarCountAsync(connectionString,
                "SELECT COUNT(*) FROM gold.beneficio_concedido WHERE entrega_id=@entrega AND codigo_registro_origem=@codigo",
                ("@entrega", deliveryId), ("@codigo", FixtureRecord)), Is.Zero);
            Assert.That(await ScalarCountAsync(connectionString,
                "SELECT COUNT(*) FROM serving.registro_integrado WHERE entrega_id=@entrega AND codigo_registro_origem=@codigo",
                ("@entrega", deliveryId), ("@codigo", FixtureRecord)), Is.Zero);

            var replay = await repository.ReserveNextAsync("bronze-replay-second", TimeSpan.FromMinutes(2), CancellationToken.None);
            Assert.That(replay, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(replay!.LoteId, Is.EqualTo(lotId));
                Assert.That(replay.ObjetoChave, Is.EqualTo(objectKey));
                Assert.That(replay.PayloadSha256, Is.EqualTo(sha256));
            });
            await ProcessFromBronzeAsync(repository, parser, store, replay!);

            await store.VerifyAsync(objectKey, sha256, payload.LongLength, CancellationToken.None);
            var replayUuid = await ScalarGuidAsync(connectionString,
                "SELECT pessoa_uuid FROM identidade.cpf_ancora WHERE cpf=@cpf", ("@cpf", FixtureCpf));
            var replayFactObservation = await ScalarLongAsync(connectionString,
                "SELECT registro_observacao_id FROM silver.registro_observacao WHERE lote_id=@lote AND codigo_registro_origem=@codigo",
                ("@lote", lotId), ("@codigo", FixtureRecord));

            Assert.Multiple(() =>
            {
                Assert.That(replayUuid, Is.EqualTo(canonicalUuid), "Replay não pode trocar UUID canônico já publicado.");
                Assert.That(replayFactObservation, Is.GreaterThan(0));
                Assert.That(replayFactObservation, Is.Not.EqualTo(firstFactObservation),
                    "A observação factual removida deve ter sido reconstruída a partir do Bronze.");
            });
            await AssertCurrentProjectionAsync(connectionString, deliveryId, lotId, canonicalUuid);
        }
        finally
        {
            try { Directory.Delete(bronzeRoot, recursive: true); } catch { }
        }
    }

    private static async Task ProcessFromBronzeAsync(
        SqlProcessorRepository repository,
        IngestionPackageParser parser,
        FileSystemBronzeObjectStore store,
        ReservedBatch batch)
    {
        await using var stream = await store.OpenReadAsync(batch.ObjetoChave, CancellationToken.None);
        var package = parser.Parse(batch, stream);
        await repository.PersistValidatedAsync(batch, package, CancellationToken.None);
    }

    private static byte[] BuildFixtureZip()
    {
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "fixtures", "ingestao", "AA01_v2");
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[] { "manifest.json", "pessoas.jsonl", "registros.jsonl" })
            {
                var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                entry.LastWriteTime = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
                using var target = entry.Open();
                using var source = File.OpenRead(Path.Combine(fixtureRoot, name));
                source.CopyTo(target);
            }
        }
        return output.ToArray();
    }

    private static async Task<(Guid DeliveryId, Guid LotId)> PointSeedDeliveryAtBronzeAsync(
        string connectionString, string sha256, string objectKey, string fileName, long length)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @entrega uniqueidentifier='20000000-0000-4000-8000-000000000001';
            DECLARE @lote uniqueidentifier='20000000-0000-4000-8000-000000000002';
            DECLARE @gestor bigint=(SELECT gestor_id FROM ref.gestor WHERE codigo='SEHAB');
            DECLARE @gpv bigint=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gestor AND versao=2);
            IF @gpv IS NULL THROW 51000, 'Fixture AA01_v2 exige Pessoa schema v2 para SEHAB.', 1;
            DELETE FROM ingestao.item_processado WHERE lote_id=@lote;
            UPDATE ingestao.entrega
               SET gestor_pessoa_versao_id=@gpv,payload_sha256=@sha,bytes_recebidos=@bytes,status='RECEBIDA',
                   data_referencia='2026-08-27T00:00:00-03:00',ultima_atualizacao=SYSUTCDATETIME()
             WHERE entrega_id=@entrega;
            UPDATE bronze.entrega_arquivo
               SET nome_arquivo=@nome,objeto_chave=@chave,payload_sha256=@sha,tamanho_bytes=@bytes,
                   estado_armazenamento='DISPONIVEL'
             WHERE entrega_id=@entrega;
            UPDATE ingestao.lote
               SET status='PENDENTE',erro_codigo=NULL,qtd_pessoas=0,qtd_registros=0,tentativa_count=0,
                   lease_id=NULL,lease_owner=NULL,lease_adquirido_em=NULL,lease_expira_em=NULL,heartbeat_em=NULL,
                   ultima_tentativa_em=NULL,proxima_tentativa_em=NULL,poison_em=NULL,atualizado_em=SYSUTCDATETIME()
             WHERE lote_id=@lote;
            SELECT @entrega,@lote;
            """;
        command.Parameters.AddWithValue("@sha", sha256);
        command.Parameters.AddWithValue("@bytes", length);
        command.Parameters.AddWithValue("@nome", fileName);
        command.Parameters.AddWithValue("@chave", objectKey);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        return (reader.GetGuid(0), reader.GetGuid(1));
    }

    private static async Task RemoveDerivedProjectionAndReopenAsync(
        string connectionString, Guid deliveryId, Guid lotId, Guid canonicalUuid, long factObservationId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM serving.registro_integrado
             WHERE entrega_id=@entrega AND codigo_registro_origem=@codigo;
            DELETE FROM gold.beneficio_concedido
             WHERE entrega_id=@entrega AND codigo_registro_origem=@codigo;
            DELETE FROM qualidade.qc_registro_resultado WHERE registro_observacao_id=@registro;
            DELETE FROM qualidade.divergencia_gestor WHERE registro_observacao_id=@registro;
            DELETE FROM silver.registro_observacao WHERE registro_observacao_id=@registro;
            DELETE FROM gold.pessoa WHERE pessoa_uuid=@uuid;
            DELETE FROM ingestao.item_processado WHERE lote_id=@lote;
            UPDATE ingestao.lote
               SET status='PENDENTE',erro_codigo=NULL,qtd_pessoas=0,qtd_registros=0,
                   lease_id=NULL,lease_owner=NULL,lease_adquirido_em=NULL,lease_expira_em=NULL,heartbeat_em=NULL,
                   proxima_tentativa_em=NULL,atualizado_em=SYSUTCDATETIME()
             WHERE lote_id=@lote;
            UPDATE ingestao.entrega SET status='RECEBIDA',ultima_atualizacao=SYSUTCDATETIME()
             WHERE entrega_id=@entrega;
            """;
        command.Parameters.AddWithValue("@entrega", deliveryId);
        command.Parameters.AddWithValue("@lote", lotId);
        command.Parameters.AddWithValue("@uuid", canonicalUuid);
        command.Parameters.AddWithValue("@registro", factObservationId);
        command.Parameters.AddWithValue("@codigo", FixtureRecord);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static async Task AssertCurrentProjectionAsync(
        string connectionString, Guid deliveryId, Guid lotId, Guid canonicalUuid)
    {
        Assert.That(await ScalarCountAsync(connectionString,
            "SELECT COUNT(*) FROM silver.registro_observacao WHERE lote_id=@lote AND codigo_registro_origem=@codigo",
            ("@lote", lotId), ("@codigo", FixtureRecord)), Is.EqualTo(1));
        Assert.That(await ScalarCountAsync(connectionString,
            "SELECT COUNT(*) FROM gold.pessoa WHERE pessoa_uuid=@uuid AND cpf=@cpf",
            ("@uuid", canonicalUuid), ("@cpf", FixtureCpf)), Is.EqualTo(1));
        Assert.That(await ScalarCountAsync(connectionString,
            "SELECT COUNT(*) FROM gold.beneficio_concedido WHERE entrega_id=@entrega AND codigo_registro_origem=@codigo AND pessoa_uuid=@uuid AND status_analitico='VIGENTE'",
            ("@entrega", deliveryId), ("@codigo", FixtureRecord), ("@uuid", canonicalUuid)), Is.EqualTo(1));
        Assert.That(await ScalarCountAsync(connectionString,
            "SELECT COUNT(*) FROM serving.registro_integrado WHERE entrega_id=@entrega AND codigo_registro_origem=@codigo AND pessoa_uuid=@uuid AND status_analitico='VIGENTE'",
            ("@entrega", deliveryId), ("@codigo", FixtureRecord), ("@uuid", canonicalUuid)), Is.EqualTo(1));
    }

    private static SqlProcessorRepository CreateRepository(string connectionString) =>
        new(new OperationalSqlAdapter(connectionString),
            new RegistryQualityEngine(new IRegistryQualityEvaluator[]
            {
                new PositiveGrantedValueRegistryQcEvaluator("AA01", 1),
                new PositiveGrantedValueRegistryQcEvaluator("POT1", 1)
            }));

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");
        var db = new SqlConnectionStringBuilder(connectionString).InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("dev", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.");
        return connectionString!;
    }

    private static async Task PrepareDatabaseAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
        await using var reset = connection.CreateCommand();
        reset.CommandText = """
            UPDATE ingestao.lote
               SET status='PROCESSADO',erro_codigo=NULL,lease_id=NULL,lease_owner=NULL,lease_adquirido_em=NULL,
                   heartbeat_em=NULL,lease_expira_em=NULL,proxima_tentativa_em=NULL,poison_em=NULL,atualizado_em=SYSUTCDATETIME();
            UPDATE ingestao.entrega SET status='PROCESSADA',ultima_atualizacao=SYSUTCDATETIME();
            """;
        await reset.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarCountAsync(string connectionString, string sql, params (string Name, object Value)[] parameters) =>
        checked((int)await ScalarLongAsync(connectionString, sql, parameters));

    private static async Task<long> ScalarLongAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<Guid> ScalarGuidAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        var result = await command.ExecuteScalarAsync();
        return result is Guid guid ? guid : Guid.Empty;
    }
}
