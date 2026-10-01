using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using Jornada.Operational.Sql;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[Category("DT10Evidence")]
[NonParallelizable]
public sealed class DT10PublicationEvidenceSqlServerTests
{
    [Test]
    public async Task Concurrent_overlapping_publishers_are_serialized_without_duplicates_or_unhandled_deadlock()
    {
        var cs = RequireSyntheticEvidenceConnection();
        await using var setup = new SqlConnection(cs);
        await setup.OpenAsync();
        await PrepareAsync(setup);
        var model = await ReadModelAsync(setup);
        var sources = await ReadProvisionalSourcesAsync(setup, 2);
        Assert.That(sources, Has.Count.EqualTo(2));

        var runA = Guid.NewGuid();
        var runB = Guid.NewGuid();
        await SeedRunAsync(setup, runA, model, sources, Publication.NewIdentity);
        await SeedRunAsync(setup, runB, model, sources, Publication.NewIdentity);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var a = PublishAsync(cs, runA, gate.Task);
        var b = PublishAsync(cs, runB, gate.Task);
        gate.SetResult();
        var outcomes = await Task.WhenAll(a, b);

        Assert.Multiple(() =>
        {
            Assert.That(outcomes, Has.All.Matches<PublishOutcome>(x => x.ErrorNumber != 1205));
            Assert.That(outcomes, Has.All.Matches<PublishOutcome>(x => x.ErrorNumber is null));
        });

        await using var verify = new SqlConnection(cs);
        await verify.OpenAsync();
        foreach (var source in sources)
        {
            await using var command = verify.CreateCommand();
            command.CommandText = """
                SELECT p.canonical_uuid,p.estado,
                       COUNT_BIG(DISTINCT e.evento_id),COUNT_BIG(DISTINCT e.linkage_run_id)
                FROM identidade.pessoa_origem_progressiva p
                LEFT JOIN identidade.pessoa_origem_progressiva_evento e
                  ON e.pessoa_origem_id=p.pessoa_origem_id AND e.linkage_run_id IN(@a,@b)
                WHERE p.pessoa_origem_id=@source
                GROUP BY p.canonical_uuid,p.estado;
                """;
            command.Parameters.AddWithValue("@a", runA);
            command.Parameters.AddWithValue("@b", runB);
            command.Parameters.AddWithValue("@source", source.SourceId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reader.GetGuid(0), Is.EqualTo(source.InitialUuid));
                Assert.That(reader.GetString(1), Is.EqualTo("REFERENCIA"));
                Assert.That(reader.GetInt64(2), Is.EqualTo(1));
                Assert.That(reader.GetInt64(3), Is.EqualTo(1));
            });
        }
    }

    [Test]
    public async Task Concurrent_different_results_for_same_origin_preserve_first_committed_reference_and_reject_conflicting_rewrite()
    {
        var cs = RequireSyntheticEvidenceConnection();
        await using var setup = new SqlConnection(cs);
        await setup.OpenAsync();
        await PrepareAsync(setup);
        var model = await ReadModelAsync(setup);
        var source = (await ReadProvisionalSourcesAsync(setup, 1)).Single();
        var target = await ReadEstablishedTargetAsync(setup, source.InitialUuid);

        var runReference = Guid.NewGuid();
        var runDifferent = Guid.NewGuid();
        await SeedRunAsync(setup, runReference, model, [source], Publication.NewIdentity);
        await SeedRunAsync(setup, runDifferent, model, [source], Publication.Associate(target));

        await using var first = new SqlConnection(cs);
        await first.OpenAsync();
        await using var tx = (SqlTransaction)await first.BeginTransactionAsync(IsolationLevel.Serializable);
        await ExecuteBatchAsync(first, tx, runReference);

        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = Task.Run(async () =>
        {
            secondStarted.SetResult();
            return await PublishAsync(cs, runDifferent, Task.CompletedTask);
        });
        await secondStarted.Task;
        await Task.Delay(750);
        Assert.That(second.IsCompleted, Is.False, "O segundo publicador deve aguardar o applock transacional.");
        await tx.CommitAsync();

        var outcome = await second;
        Assert.Multiple(() =>
        {
            Assert.That(outcome.ErrorNumber, Is.EqualTo(51807));
            Assert.That(outcome.ErrorNumber, Is.Not.EqualTo(1205));
        });

        await using var verify = new SqlConnection(cs);
        await verify.OpenAsync();
        await using var command = verify.CreateCommand();
        command.CommandText = """
            SELECT p.canonical_uuid,p.estado,
                   (SELECT COUNT_BIG(*) FROM identidade.pessoa_origem_progressiva_evento e
                     WHERE e.pessoa_origem_id=@source AND e.linkage_run_id IN(@a,@b))
            FROM identidade.pessoa_origem_progressiva p
            WHERE p.pessoa_origem_id=@source;
            """;
        command.Parameters.AddWithValue("@source", source.SourceId);
        command.Parameters.AddWithValue("@a", runReference);
        command.Parameters.AddWithValue("@b", runDifferent);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetGuid(0), Is.EqualTo(source.InitialUuid));
            Assert.That(reader.GetString(1), Is.EqualTo("REFERENCIA"));
            Assert.That(reader.GetInt64(2), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Injected_failure_after_ledger_insert_rolls_back_entire_batch_and_leaves_no_half_written_ledger()
    {
        var cs = RequireSyntheticEvidenceConnection();
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();
        await PrepareAsync(connection);
        var model = await ReadModelAsync(connection);
        var sources = await ReadProvisionalSourcesAsync(connection, 2);
        var run = Guid.NewGuid();
        await SeedRunAsync(connection, run, model, sources, Publication.NewIdentity);

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await using (var inject = connection.CreateCommand())
            {
                inject.Transaction = tx;
                inject.CommandText = """
                    CREATE TRIGGER identidade.tr_dt10_test_fail_after_event_insert
                    ON identidade.pessoa_origem_progressiva_evento
                    AFTER INSERT AS
                    BEGIN
                        SET NOCOUNT ON;
                        IF TRY_CONVERT(INT,SESSION_CONTEXT(N'DT10_FAIL_AFTER_LEDGER'))=1
                            THROW 51998, 'DT10_TEST_INJECTED_FAILURE_AFTER_LEDGER_INSERT', 1;
                    END;
                    """;
                await inject.ExecuteNonQueryAsync();
            }
            await using (var arm = connection.CreateCommand())
            {
                arm.Transaction = tx;
                arm.CommandText = "EXEC sys.sp_set_session_context @key=N'DT10_FAIL_AFTER_LEDGER',@value=1;";
                await arm.ExecuteNonQueryAsync();
            }

            var ex = Assert.ThrowsAsync<SqlException>(async () => await ExecuteBatchAsync(connection, tx, run));
            Assert.That(ex!.Number, Is.EqualTo(51998));
            await using var state = connection.CreateCommand();
            state.Transaction = tx;
            state.CommandText = "SELECT XACT_STATE();";
            Assert.That(Convert.ToInt32(await state.ExecuteScalarAsync(), CultureInfo.InvariantCulture), Is.EqualTo(-1));
        }
        finally
        {
            if (tx.Connection is not null) await tx.RollbackAsync();
        }

        await using var verify = new SqlConnection(cs);
        await verify.OpenAsync();
        await using var command = verify.CreateCommand();
        command.CommandText = """
            SELECT
              (SELECT COUNT_BIG(*) FROM identidade.pessoa_origem_progressiva_evento WHERE linkage_run_id=@run),
              (SELECT COUNT_BIG(*) FROM identidade.linkage_resultado WHERE linkage_run_id=@run AND progressiva_versao IS NOT NULL),
              (SELECT COUNT_BIG(*) FROM identidade.pessoa_origem_progressiva p
                 JOIN silver.pessoa_observacao po ON po.pessoa_origem_id=p.pessoa_origem_id
                WHERE po.pessoa_observacao_id IN
                      (SELECT pessoa_observacao_id FROM identidade.linkage_run_item WHERE linkage_run_id=@run)
                  AND (p.estado<>N'PROVISORIA' OR p.canonical_uuid IS NOT NULL)),
              CASE WHEN OBJECT_ID(N'identidade.tr_dt10_test_fail_after_event_insert',N'TR') IS NULL THEN 0 ELSE 1 END;
            """;
        command.Parameters.AddWithValue("@run", run);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetInt64(0), Is.Zero);
            Assert.That(reader.GetInt64(1), Is.Zero);
            Assert.That(reader.GetInt64(2), Is.Zero);
            Assert.That(reader.GetInt32(3), Is.Zero);
        });
    }

    private static async Task PrepareAsync(SqlConnection connection)
    {
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
        await EnsureDt10ScaleAsync(connection, databaseDir);
    }

    private static async Task EnsureDt10ScaleAsync(SqlConnection connection, string databaseDir)
    {
        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT CASE WHEN EXISTS(SELECT 1 FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SCALE-%') THEN 1 ELSE 0 END;";
        var scaleExists = Convert.ToInt32(await exists.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 1;

        if (!scaleExists)
            await SqlBatchRunner.ExecuteFileWithSqlCmdVariablesAsync(
            connection,
            Path.Combine(databaseDir, "Jornada_Dev_SyntheticScale.sql"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["SCALE_PEOPLE"] = "1000",
                ["SCALE_PAIRED"] = "2",
                ["SCALE_PENDING"] = "1000",
                ["SCALE_SEED"] = "355",
                ["SCALE_COLLISION_MODULO"] = "37",
                ["SCALE_BIRTH_SHIFT_MODULO"] = "29"
            });

        while (true)
        {
            await using var pending = connection.CreateCommand();
            pending.CommandText = "SELECT TOP (1000) o.pessoa_origem_id FROM silver.pessoa_origem o LEFT JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=o.pessoa_origem_id WHERE p.pessoa_origem_id IS NULL ORDER BY o.pessoa_origem_id;";
            var ids = new List<long>();
            await using (var reader = await pending.ExecuteReaderAsync())
                while (await reader.ReadAsync()) ids.Add(reader.GetInt64(0));
            if (ids.Count == 0) break;

            foreach (var id in ids)
            {
                await using var ensure = connection.CreateCommand();
                ensure.CommandText = "EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@source_id;";
                ensure.Parameters.AddWithValue("@source_id", id);
                await ensure.ExecuteNonQueryAsync();
            }
        }
    }

    private static async Task<PublishOutcome> PublishAsync(string cs, Guid run, Task gate)
    {
        await gate;
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await ExecuteBatchAsync(connection, tx, run);
            await tx.CommitAsync();
            return new(null);
        }
        catch (SqlException ex)
        {
            if (tx.Connection is not null) await tx.RollbackAsync();
            return new(ex.Number);
        }
    }

    private static async Task ExecuteBatchAsync(SqlConnection connection, SqlTransaction tx, Guid run)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandTimeout = 75;
        command.CommandText = "EXEC identidade.sp_publicar_resolucao_progressiva_linkage_lote @linkage_run_id=@run;";
        command.Parameters.AddWithValue("@run", run);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedRunAsync(SqlConnection connection, Guid run, ModelFixture model,
        IReadOnlyList<SourceFixture> sources, Publication publication)
    {
        await using var header = connection.CreateCommand();
        header.CommandText = """
            INSERT identidade.linkage_run(
                linkage_run_id,modelo_id,modelo_versao,tipo_run,status,limite_solicitado,escopo_json,batch_size,max_parallelism,
                pessoa_observacao_id_high_watermark,registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,
                sem_candidato_no_bloco,solicitado_por,motivo,correlation_id,iniciado_em)
            VALUES(@run,@model,@version,N'FULL',N'EXECUTANDO',@count,N'{"test":"dt10-evidence"}',@count,1,
                   @high,@count,@count,@resolved,@unresolved,0,@noCandidate,N'CI',N'DT10 evidence',NEWID(),SYSUTCDATETIME());
            """;
        header.Parameters.AddWithValue("@run", run);
        header.Parameters.AddWithValue("@model", model.ModelId);
        header.Parameters.AddWithValue("@version", model.Version);
        header.Parameters.AddWithValue("@count", sources.Count);
        header.Parameters.AddWithValue("@high", sources.Max(x => x.ObservationId));
        header.Parameters.AddWithValue("@resolved", publication.Kind == "ASSOCIACAO_EXISTENTE" ? sources.Count : 0);
        header.Parameters.AddWithValue("@unresolved", publication.Kind == "NOVA_IDENTIDADE" ? sources.Count : 0);
        header.Parameters.AddWithValue("@noCandidate", publication.Kind == "NOVA_IDENTIDADE" ? sources.Count : 0);
        await header.ExecuteNonQueryAsync();

        foreach (var source in sources)
        {
            await using var item = connection.CreateCommand();
            item.CommandText = "INSERT identidade.linkage_run_item(linkage_run_id,pessoa_observacao_id) VALUES(@run,@obs);";
            item.Parameters.AddWithValue("@run", run);
            item.Parameters.AddWithValue("@obs", source.ObservationId);
            await item.ExecuteNonQueryAsync();

            await using var result = connection.CreateCommand();
            result.CommandText = publication.Kind == "NOVA_IDENTIDADE" ? """
                INSERT identidade.linkage_resultado(
                    linkage_run_id,modelo_id,modelo_versao,pessoa_observacao_id,pessoa_uuid_resolvido,
                    melhor_candidato_uuid,score_melhor,segundo_candidato_uuid,score_segundo,margem,status,motivo,calculado_em,
                    resultado_publicacao,pessoa_uuid_publicado,status_publicacao,motivo_publicacao,pessoa_origem_id_publicado,
                    politica_publicacao_versao,universo_referencia,publicado_em)
                VALUES(@run,@model,@version,@obs,NULL,NULL,0,NULL,NULL,NULL,N'NAO_RESOLVIDO',
                       N'SEM_CANDIDATO_NO_RULESET_BLOCKING',SYSUTCDATETIME(),N'NOVA_IDENTIDADE',@initial,N'RESOLVIDO',
                       N'NOVA_IDENTIDADE_APOS_BUSCA_COMPLETA',@source,N'LINKAGE_PROGRESSIVE_PUBLICATION_V1',
                       N'RUN_COMPLETO_DT10',SYSUTCDATETIME());
                """ : """
                INSERT identidade.linkage_resultado(
                    linkage_run_id,modelo_id,modelo_versao,pessoa_observacao_id,pessoa_uuid_resolvido,
                    melhor_candidato_uuid,score_melhor,segundo_candidato_uuid,score_segundo,margem,status,motivo,calculado_em,
                    resultado_publicacao,pessoa_uuid_publicado,status_publicacao,motivo_publicacao,pessoa_origem_id_publicado,
                    politica_publicacao_versao,universo_referencia,publicado_em)
                VALUES(@run,@model,@version,@obs,@target,@target,0.99,NULL,NULL,NULL,N'RESOLVIDO',NULL,SYSUTCDATETIME(),
                       N'ASSOCIACAO_EXISTENTE',@target,N'RESOLVIDO',N'ASSOCIACAO_EXISTENTE_LINKAGE',@source,
                       N'LINKAGE_PROGRESSIVE_PUBLICATION_V1',N'RUN_COMPLETO_DT10',SYSUTCDATETIME());
                """;
            result.Parameters.AddWithValue("@run", run);
            result.Parameters.AddWithValue("@model", model.ModelId);
            result.Parameters.AddWithValue("@version", model.Version);
            result.Parameters.AddWithValue("@obs", source.ObservationId);
            result.Parameters.AddWithValue("@source", source.SourceId);
            result.Parameters.AddWithValue("@initial", source.InitialUuid);
            result.Parameters.AddWithValue("@target", (object?)publication.Target ?? DBNull.Value);
            await result.ExecuteNonQueryAsync();
        }
    }

    private static async Task<List<SourceFixture>> ReadProvisionalSourcesAsync(SqlConnection connection, int count)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP(@count) po.pessoa_observacao_id,po.pessoa_origem_id,p.initial_uuid
            FROM silver.pessoa_observacao po
            JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
            WHERE po.pessoa_origem_id IS NOT NULL AND po.cpf IS NULL AND p.estado=N'PROVISORIA'
            ORDER BY po.pessoa_observacao_id DESC;
            """;
        command.Parameters.AddWithValue("@count", count);
        var rows = new List<SourceFixture>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetGuid(2)));
        return rows;
    }

    private static async Task<Guid> ReadEstablishedTargetAsync(SqlConnection connection, Guid exclude)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP(1) pessoa_uuid FROM identidade.cpf_ancora WHERE pessoa_uuid<>@exclude ORDER BY cpf;";
        command.Parameters.AddWithValue("@exclude", exclude);
        return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Fixture sem referência estabelecida alternativa."));
    }

    private static async Task<ModelFixture> ReadModelAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP(1) modelo_id,versao FROM identidade.modelo_linkage ORDER BY CASE WHEN status=N'ATIVO' THEN 0 ELSE 1 END,versao DESC;";
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("Fixture sem modelo de Linkage.");
        return new(reader.GetGuid(0), reader.GetInt32(1));
    }

    private static string RequireSyntheticEvidenceConnection()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("JORNADA_DT10_EVIDENCE"), "1", StringComparison.Ordinal))
            Assert.Pass("DT-10 pesado/medição permanece desligado por padrão; defina JORNADA_DT10_EVIDENCE=1 para executar a evidência.");
        var cs = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs)) Assert.Fail("Defina JORNADA_TEST_SQL_CONNECTION para JornadaSyntheticDev.");
        var db = new SqlConnectionStringBuilder(cs!).InitialCatalog;
        Assert.That(db, Is.EqualTo("JornadaSyntheticDev").IgnoreCase,
            "DT-10 evidence só pode executar no banco sintético isolado JornadaSyntheticDev; JornadaLocal é proibido.");
        return cs!;
    }

    private sealed record SourceFixture(long ObservationId, long SourceId, Guid InitialUuid);
    private sealed record ModelFixture(Guid ModelId, int Version);
    private sealed record PublishOutcome(int? ErrorNumber);
    private sealed record Publication(string Kind, Guid? Target)
    {
        public static Publication NewIdentity { get; } = new("NOVA_IDENTIDADE", null);
        public static Publication Associate(Guid target) => new("ASSOCIACAO_EXISTENTE", target);
    }
}
