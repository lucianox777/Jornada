using System.Data;
using System.Security.Cryptography;
using System.Text;
using Jornada.Bronze.Storage;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Linkage.Runner;
using Jornada.Operational.Sql;
using Jornada.Pipeline.Coordination;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jornada.Tests.Unit;

/// <summary>
/// E2E físico e opt-in da função REAL ProbabilisticLinkageBatchRunner.RunAsync.
/// Não usa a semente TEST selada nem declara paridade de produção: a ligação
/// de modelo V9 é explicitamente injetada apenas no banco descartável.
/// </summary>
[TestFixture, Category("SQLSmoke")]
public sealed class V9FullSchemaNonPublishingRunnerTests
{
    [Test]
    public async Task Full_schema_RunAsync_200_two_batches_no_publication_and_second_batch_failure()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_V9_FULL_SCHEMA_NONPUBLISH_SQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Ignore("Smoke full-schema somente em JornadaHarness descartável explícita.");
            return;
        }
        if (new SqlConnectionStringBuilder(connectionString).InitialCatalog != "JornadaHarness")
            throw new InvalidOperationException("E2E só aceita JornadaHarness, nunca banco de operação.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        // Guard before ANY mutation; a clone of the small ledger is not a full operational schema.
        await using (var guard = new SqlCommand("""
            IF DB_NAME()<>N'JornadaHarness' THROW 52800,'Somente JornadaHarness.',1;
            IF OBJECT_ID(N'identidade.linkage_run',N'U') IS NULL
               OR OBJECT_ID(N'identidade.linkage_resultado',N'U') IS NULL
               OR OBJECT_ID(N'gold.pessoa',N'U') IS NULL
               OR OBJECT_ID(N'serving.registro_integrado',N'U') IS NULL
               OR OBJECT_ID(N'identidade.modelo_linkage_v9_operacional',N'U') IS NULL
               OR OBJECT_ID(N'identidade.v_vinculo_corrente',N'V') IS NULL
               THROW 52801,'Esquema operacional integral exigido.',1;
            IF EXISTS (SELECT 1 FROM silver.pessoa_observacao)
               OR EXISTS (SELECT 1 FROM identidade.modelo_linkage)
               OR EXISTS (SELECT 1 FROM identidade.linkage_run)
               OR EXISTS (SELECT 1 FROM gold.pessoa)
               OR EXISTS (SELECT 1 FROM serving.registro_integrado)
               THROW 52802,'E2E exige SQL novo/vazio sem dados operacionais.',1;
            """, connection))
            await guard.ExecuteNonQueryAsync();

        // The full 4-TF, 81-cell snapshot is losslessly stored and reloaded
        // through the production-side V9 SQL loader. No winning parameters are refitted.
        var winner = V9FittedSnapshotTests.CompleteTf4OperationalWinner();
        var stored = await V9OperationalSqlModelConverter.MaterializeOnceAsync(connection, winner);
        var pinned = await V9OperationalPinnedCohortRuntime.LoadPinnedAsync(
            connection, stored.ModelId, stored.SourceSnapshotSha256);
        Assert.That(pinned.FrozenWinner.SerializeUtf8(), Is.EqualTo(winner.SerializeUtf8()));
        Assert.That(stored.Active, Is.False);
        Assert.That(stored.Published, Is.False);

        // The current legacy linkage_run FK requires a metadata anchor in
        // identidade.modelo_linkage. This is NOT a conversion of TF4 to legacy
        // parameters: the scorer below reads ONLY the SQL-pinned V9 snapshot.
        // This binding is restricted to disposable SQL and Publish=false.
        await using (var anchor = new SqlCommand("""
            INSERT identidade.modelo_linkage(
                modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
                deduplicacao_metodo,base_referencia,snapshot_referencia,gerado_em)
            VALUES(
                @id,1,N'RASCUNHO',@algorithm,@normalization,
                N'V9_PINNED_NONPUBLISH_E2E',N'SYNTHETIC_FULL_SCHEMA',
                @sha,SYSDATETIMEOFFSET());
            """, connection))
        {
            anchor.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = stored.ModelId;
            anchor.Parameters.Add("@algorithm", SqlDbType.NVarChar, 120).Value =
                LinkageParameterCatalog.CompositeNominalDecisionEvidenceAlgorithmVersion;
            anchor.Parameters.Add("@normalization", SqlDbType.NVarChar, 80).Value =
                IdentityComparison.NormalizationVersion;
            anchor.Parameters.Add("@sha", SqlDbType.Char, 64).Value = stored.SourceSnapshotSha256;
            Assert.That(await anchor.ExecuteNonQueryAsync(), Is.EqualTo(1));
        }

        await InsertSyntheticUniverseAsync(connection, 200);
        var before = await ReadPublicationFingerprintAsync(connection);
        var model = new ProbabilisticLinkageModelRef(
            stored.ModelId, 1, LinkageParameterCatalog.CompositeNominalDecisionEvidenceAlgorithmVersion,
            winner.Payload.Threshold, winner.Payload.ConflictMargin)
        {
            BlockingContract = new(
                "V9_SYNTHETIC_NONPUBLISH_" + winner.ContentSha256, winner.ContentSha256,
                pinned.FrozenWinner.Payload.ProjectionSchemaVersion,
                pinned.FrozenWinner.Payload.ProjectionFingerprintSha256)
        };
        var sql = new OperationalSqlAdapter(connectionString);
        var root = Path.Combine(Path.GetTempPath(), "jornada-v9-nonpublish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            ProbabilisticLinkageBatchRunner MakeRunner(int failAfter) =>
                new(new ConfigurationBuilder().Build(), sql,
                    new PinnedV9DisposableLinkage(sql, model,
                        V9FittedSnapshotExecutionRuntime.Load(
                            pinned.FrozenWinner.SerializeUtf8(), winner.ContentSha256), failAfter),
                    new SqlPipelineCoordinator(sql), new Dt05ReplaySql(sql),
                    new Dt05ReplayManifestPublisher(new FileSystemBronzeObjectStore(root), root),
                    new Dt05CandidateStateSnapshotPublisher(sql, root),
                    new Dt05BlockingProjectionSnapshotPublisher(sql, root),
                    new Dt05HistoricalCandidateStateVerifier(root),
                    new Dt05HistoricalBlockingProjectionVerifier(root),
                    NullLogger<ProbabilisticLinkageBatchRunner>.Instance);

            static ProbabilisticLinkageRunRequest Request() =>
                new(LinkageRunType.MODEL_VALIDATION, 1, null, null, null,
                    100, 1, null, "V9_HARNESS", "NO_PUBLISH_FULL_SCHEMA_E2E",
                    Guid.NewGuid(), false);

            var first = await MakeRunner(int.MaxValue).RunAsync(Request(), CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(first.Status, Is.EqualTo(LinkageRunStatus.CONCLUIDO_SEM_PUBLICACAO));
                Assert.That(first.ModeloId, Is.EqualTo(stored.ModelId));
                Assert.That(first.Elegiveis, Is.EqualTo(200));
                Assert.That(first.Avaliados, Is.EqualTo(200));
                Assert.That(first.Resolvidos + first.NaoResolvidos + first.Conflitos, Is.EqualTo(200));
                Assert.That(first.PublicadoEm, Is.Null);
            });
            await AssertRunRowsAsync(connection, first.RunId, "CONCLUIDO_SEM_PUBLICACAO", 200);
            Assert.That(await ReadPublicationFingerprintAsync(connection), Is.EqualTo(before),
                "O runner sem publicação não pode alterar Gold, Serving nem vínculo corrente.");

            // The real batch transaction commits batch #1 before a deliberately
            // injected exception in batch #2. It MUST NOT fake global rollback.
            // With MaxParallelism=1, the 101st score fails deterministically.
            var failedRunner = MakeRunner(100);
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await failedRunner.RunAsync(Request(), CancellationToken.None));
            await using (var terminal = new SqlCommand("""
                SELECT TOP (1) linkage_run_id FROM identidade.linkage_run
                WHERE status=N'FALHOU' AND modelo_id=@id
                ORDER BY iniciado_em DESC;
                """, connection))
            {
                terminal.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = stored.ModelId;
                var id = await terminal.ExecuteScalarAsync();
                Assert.That(id, Is.InstanceOf<Guid>());
                await AssertRunRowsAsync(connection, (Guid)id!, "FALHOU", 100);
            }
            Assert.That(await ReadPublicationFingerprintAsync(connection), Is.EqualTo(before),
                "Falha no segundo lote não pode expor run parcial em Gold/Serving/vínculo.");
            await using (var status = new SqlCommand(
                "SELECT status FROM identidade.modelo_linkage WHERE modelo_id=@id", connection))
            {
                status.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = stored.ModelId;
                Assert.That(await status.ExecuteScalarAsync(), Is.EqualTo("RASCUNHO"));
            }
            TestContext.WriteLine("V9_FULL_SCHEMA_REAL_RUNASYNC_NONPUBLISH_PASS: 200 rows; batches 100+100; failure 100/200; TEST 100k blocked");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task InsertSyntheticUniverseAsync(SqlConnection connection, int count)
    {
        // Canonical data contracts are inserted, not fake copies of linkage tables.
        // This fixture deliberately has NO candidate in Gold. It tests the
        // full RunAsync materialization/batches/transactions/nonpublication path;
        // positive TF4 candidate parity remains a separate prerequisite.
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using (var setup = new SqlCommand("""
            INSERT ref.gestor(codigo,nome) VALUES(N'V9TEST',N'Disposable V9 TEST harness');
            DECLARE @g bigint=SCOPE_IDENTITY();
            INSERT ref.sistema_origem(gestor_id,codigo,nome)
                VALUES(@g,N'V9TEST',N'Disposable V9 TEST harness');
            DECLARE @s bigint=SCOPE_IDENTITY();
            INSERT ref.base_pessoa_origem(
                codigo,nome,gestor_custodiante_id,escopo,confianca_identidade)
                VALUES(N'V9TEST',N'Disposable V9 TEST harness',@g,N'PRIVADA',N'NAO_HOMOLOGADA');
            DECLARE @b bigint=SCOPE_IDENTITY();
            INSERT ref.sistema_origem_base_pessoa(sistema_origem_id,base_pessoa_origem_id,padrao)
                VALUES(@s,@b,1);
            INSERT ref.gestor_pessoa_versao(
                gestor_id,versao,vigencia_inicio,pessoa_schema_ref,status)
                VALUES(@g,1,'20260101',N'isolated-v9-synthetic',N'RASCUNHO');
            DECLARE @v bigint=SCOPE_IDENTITY();
            INSERT ingestao.entrega(
                entrega_id,gestor_id,sistema_origem_id,gestor_pessoa_versao_id,
                idempotency_key,payload_sha256,bytes_recebidos,status,data_referencia,recebido_em)
                VALUES(@delivery,@g,@s,@v,N'v9-test-fresh',REPLICATE('0',64),0,
                N'PROCESSADA',SYSUTCDATETIME(),SYSUTCDATETIME());
            INSERT ingestao.lote(lote_id,entrega_id,lote_seq,lote_total,status)
                VALUES(@batch,@delivery,1,1,N'PROCESSADO');
            SELECT @g,@s,@b;
            """, connection, transaction))
        {
            setup.Parameters.Add("@delivery", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
            setup.Parameters.Add("@batch", SqlDbType.UniqueIdentifier).Value = BatchId;
            await using var reader = await setup.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            GestorId = reader.GetInt64(0);
            SistemaId = reader.GetInt64(1);
            BaseId = reader.GetInt64(2);
        }

        for (var i = 1; i <= count; i++)
        {
            var code = "V9TEST_" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
            var person = "PESSOA " + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + " SILVA";
            var mother = "MARIA " + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + " COSTA";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
            await using var add = new SqlCommand("""
                INSERT silver.pessoa_origem(sistema_origem_id,base_pessoa_origem_id,codigo_pessoa_origem)
                    VALUES(@system,@base,@code);
                DECLARE @source bigint=SCOPE_IDENTITY();
                INSERT silver.pessoa_observacao(
                    pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
                    cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
                    VALUES(@source,@batch,@gestor,@code,1,@hash,N'SEM_CPF',
                        @name,@name_cmp,@birth,@mother,@mother_cmp,SYSUTCDATETIME());
                DECLARE @observation bigint=SCOPE_IDENTITY();
                EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@source;
                SELECT @observation;
                """, connection, transaction);
            add.Parameters.Add("@system", SqlDbType.BigInt).Value = SistemaId;
            add.Parameters.Add("@base", SqlDbType.BigInt).Value = BaseId;
            add.Parameters.Add("@gestor", SqlDbType.BigInt).Value = GestorId;
            add.Parameters.Add("@batch", SqlDbType.UniqueIdentifier).Value = BatchId;
            add.Parameters.Add("@code", SqlDbType.NVarChar, 120).Value = code;
            add.Parameters.Add("@hash", SqlDbType.Char, 64).Value = hash;
            add.Parameters.Add("@name", SqlDbType.NVarChar, 400).Value = person;
            add.Parameters.Add("@name_cmp", SqlDbType.NVarChar, 400).Value = IdentityComparison.NormalizeText(person);
            add.Parameters.Add("@mother", SqlDbType.NVarChar, 400).Value = mother;
            add.Parameters.Add("@mother_cmp", SqlDbType.NVarChar, 400).Value = IdentityComparison.NormalizeText(mother);
            add.Parameters.Add("@birth", SqlDbType.Date).Value = new DateTime(1980, 5, 6);
            await using var reader = await add.ExecuteReaderAsync();
            while (reader.FieldCount != 1 && await reader.NextResultAsync()) { }
            Assert.That(reader.FieldCount, Is.EqualTo(1));
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.That(reader.GetInt64(0), Is.GreaterThan(0));
        }
        await transaction.CommitAsync();
    }

    private static readonly Guid BatchId = Guid.NewGuid();
    private static long GestorId;
    private static long SistemaId;
    private static long BaseId;

    private static async Task<string> ReadPublicationFingerprintAsync(SqlConnection connection)
    {
        await using var cmd = new SqlCommand("""
            SELECT CONCAT(
              (SELECT COUNT_BIG(*) FROM gold.pessoa),'|',
              (SELECT COUNT_BIG(*) FROM serving.registro_integrado),'|',
              (SELECT COUNT_BIG(*) FROM identidade.v_vinculo_corrente));
            """, connection);
        return (string)(await cmd.ExecuteScalarAsync()
            ?? throw new InvalidDataException("Fingerprint de Gold/Serving ausente."));
    }

    private static async Task AssertRunRowsAsync(SqlConnection connection, Guid runId,
        string expectedStatus, int expectedRows)
    {
        await using var cmd = new SqlCommand("""
            SELECT r.status, r.avaliados,
                   (SELECT COUNT_BIG(*) FROM identidade.linkage_resultado
                    WHERE linkage_run_id=r.linkage_run_id)
            FROM identidade.linkage_run r WHERE r.linkage_run_id=@id;
            """, connection);
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = runId;
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo(expectedStatus));
        Assert.That(reader.GetInt64(1), Is.EqualTo(expectedRows));
        Assert.That(reader.GetInt64(2), Is.EqualTo(expectedRows));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    private sealed class PinnedV9DisposableLinkage(
        IOperationalSqlAdapter sql,
        ProbabilisticLinkageModelRef model,
        V9FittedSnapshotExecutionRuntime runtime,
        int failAfter) : IProbabilisticIdentityLinkage
    {
        private int scored;

        public Task<ProbabilisticLinkageModelRef> GetActiveModelAsync(CancellationToken ct) =>
            throw new InvalidOperationException("E2E V9 proíbe modelo ATIVO e fallback.");

        public Task<ProbabilisticLinkageModelRef> GetModelByVersionAsync(int version, CancellationToken ct) =>
            version == model.Version ? Task.FromResult(model)
                : throw new InvalidOperationException("Versão de modelo V9 não pinada.");

        public async Task<ProbabilisticLinkageDecision> ResolveWithoutCpfAsync(
            IdentityObservation observation, Guid modelId, CancellationToken ct)
        {
            if (modelId != model.ModelId || observation.Cpf is not null)
                throw new InvalidDataException("Modelo V9 alterado ou CPF inesperado.");
            if (Interlocked.Increment(ref scored) > failAfter)
                throw new InvalidOperationException("V9_SYNTHETIC_SECOND_BATCH_INJECTED_FAILURE");
            await using var connection = await sql.OpenAsync(ct);
            await using var db = connection.CreateCommand();
            db.CommandText = "SELECT DB_NAME();";
            if (!string.Equals((string?)await db.ExecuteScalarAsync(ct), "JornadaHarness",
                    StringComparison.Ordinal))
                throw new InvalidOperationException("E2E V9 fora de JornadaHarness.");
            await using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 120;
            var query = runtime.BuildCandidateUuidQuery(
                cmd, observation, runtime.ProjectionSchemaVersion, runtime.ProjectionFingerprintSha256);
            cmd.CommandText = "SELECT g.pessoa_uuid,g.nome_completo,g.data_nascimento,g.nome_mae " +
                "FROM (" + query + ") c JOIN gold.pessoa g ON g.pessoa_uuid=c.pessoa_uuid " +
                "WHERE g.estado_identidade=N'REFERENCIA' ORDER BY g.pessoa_uuid;";
            var candidates = new List<V9SnapshotCandidate>();
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                    candidates.Add(new(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                        reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2)),
                        reader.IsDBNull(3) ? null : reader.GetString(3)));
            return runtime.ResolveRetrievedCandidatesPreview(
                modelId, observation, new(runtime.SnapshotSha256,
                    runtime.ProjectionSchemaVersion, runtime.ProjectionFingerprintSha256,
                    true, candidates));
        }
    }
}
