using System.Data;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

/// <summary>
/// Physical fail-closed DT-10 evidence on the disposable SQL integration database.
/// No connection to operational databases is permitted.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class LinkageNoCandidateLiteralGuardsSqlServerTests
{
    private string _cs = null!;
    private Model _model = null!;
    private Guid _candidate;
    private IReadOnlyList<Source> _sources = null!;

    [OneTimeSetUp]
    public async Task PrepareAsync()
    {
        _cs = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION")
            ?? throw new InvalidOperationException("Disposable SQL integration fixture required.");
        var catalog = new SqlConnectionStringBuilder(_cs).InitialCatalog;
        if (string.IsNullOrWhiteSpace(catalog) ||
            (!catalog.Contains("Test", StringComparison.OrdinalIgnoreCase) &&
             !catalog.Contains("Dev", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Physical evidence requires an isolated Test/Dev database.");
        await using var connection = new SqlConnection(_cs);
        await connection.OpenAsync();
        var dir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, dir);
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(dir, "Jornada_Seed_Dev.sql"));

        // DEV seed contains two CPF-less origins but no progressive initial UUIDs.
        // Materialize them using the installed procedure on the disposable database.
        await using (var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await using var ensure = connection.CreateCommand();
            ensure.Transaction = tx;
            ensure.CommandText = """
                DECLARE @a BIGINT = (SELECT TOP(1) pessoa_origem_id FROM silver.pessoa_observacao
                                    WHERE codigo_pessoa_origem=N'SEH005' AND cpf IS NULL);
                DECLARE @b BIGINT = (SELECT TOP(1) pessoa_origem_id FROM silver.pessoa_observacao
                                    WHERE codigo_pessoa_origem=N'SEH006' AND cpf IS NULL);
                IF @a IS NULL OR @b IS NULL OR @a=@b
                    THROW 51836,'Missing two distinct CPF-less synthetic DEV origins.',1;
                EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@a;
                EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@b;
                """;
            await ensure.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT TOP(1) modelo_id,versao FROM identidade.modelo_linkage ORDER BY CASE WHEN status=N'ATIVO' THEN 0 ELSE 1 END,versao DESC;";
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("Missing synthetic Linkage model.");
            _model = new Model(reader.GetGuid(0), reader.GetInt32(1));
        }
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT TOP(1) pessoa_uuid FROM identidade.pessoa ORDER BY pessoa_uuid;";
            _candidate = (Guid)(await cmd.ExecuteScalarAsync() ??
                throw new InvalidOperationException("Missing FK-valid synthetic candidate."));
        }
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                WITH distinct_origins AS (
                    SELECT po.pessoa_observacao_id,po.pessoa_origem_id,p.initial_uuid,
                           ROW_NUMBER() OVER(PARTITION BY po.pessoa_origem_id
                               ORDER BY po.pessoa_observacao_id DESC) AS rn
                    FROM silver.pessoa_observacao po
                    JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
                    WHERE po.pessoa_origem_id IS NOT NULL AND po.cpf IS NULL
                      AND po.codigo_pessoa_origem IN (N'SEH005',N'SEH006')
                      AND p.estado=N'PROVISORIA' AND p.canonical_uuid IS NULL
                )
                SELECT TOP(2) pessoa_observacao_id,pessoa_origem_id,initial_uuid
                FROM distinct_origins WHERE rn=1 ORDER BY pessoa_observacao_id DESC;
                """;
            var rows = new List<Source>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rows.Add(new Source(reader.GetInt64(0), reader.GetInt64(1), reader.GetGuid(2)));
            _sources = rows;
        }
        if (_sources.Count != 2 || _sources[0].SourceId == _sources[1].SourceId)
            throw new InvalidOperationException("Mixed-batch proof requires two distinct provisional origins.");
    }

    [TestCase("SEMXCANDIDATO_", false)]
    [TestCase("SEM_CANDIDATOX", false)]
    [TestCase("SEMXCANDIDATO_", true)]
    [TestCase("SEM_CANDIDATOX", true)]
    public async Task Wildcard_lookalikes_raise_51810(string reason, bool batch)
    {
        var run = Guid.NewGuid();
        await SeedAsync(run, (_sources[0], reason, null, null));
        await RejectAndCheckAsync(run, new[] { _sources[0] }, batch, 51810);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task Legitimate_reason_with_best_or_second_candidate_raises_51835(bool second, bool batch)
    {
        var run = Guid.NewGuid();
        await SeedAsync(run, (_sources[0], "SEM_CANDIDATO_NO_RULESET_BLOCKING",
            second ? null : _candidate, second ? _candidate : null));
        await RejectAndCheckAsync(run, new[] { _sources[0] }, batch, 51835);
    }

    [Test]
    public async Task Mixed_batch_fails_before_any_event_or_state_change()
    {
        var run = Guid.NewGuid();
        await SeedAsync(run,
            (_sources[0], "SEM_CANDIDATO_NO_RULESET_BLOCKING", null, null),
            (_sources[1], "SEM_CANDIDATO_NO_RULESET_BLOCKING", _candidate, null));
        await RejectAndCheckAsync(run, _sources, true, 51835);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Genuine_absence_and_replay_are_idempotent(bool batch)
    {
        var run = Guid.NewGuid();
        await SeedAsync(run, (_sources[0], "SEM_CANDIDATO_NO_RULESET_BLOCKING", null, null));
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await PublishAsync(conn, tx, run, _sources[0], batch);
            var first = await StateAsync(conn, tx, run, _sources[0]);
            Assert.Multiple((Action)(() =>
            {
                Assert.That(first.Status, Is.EqualTo("REFERENCIA"));
                Assert.That(first.Uuid, Is.EqualTo(_sources[0].InitialUuid));
                Assert.That(first.Events, Is.EqualTo(1));
                Assert.That(first.Version, Is.GreaterThan(0));
                Assert.That(first.ProgressiveVersion, batch ? Is.Not.Null : Is.Null);
            }));
            await PublishAsync(conn, tx, run, _sources[0], batch);
            Assert.That(await StateAsync(conn, tx, run, _sources[0]), Is.EqualTo(first));
        }
        finally
        {
            if (tx.Connection is not null) await tx.RollbackAsync();
        }
    }

    private async Task RejectAndCheckAsync(Guid run, IReadOnlyList<Source> sources, bool batch, int error)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        var before = new List<Snapshot>();
        foreach (var source in sources) before.Add(await StateAsync(conn, null, run, source));
        await using (var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            try
            {
                var ex = Assert.ThrowsAsync<SqlException>((Func<Task>)(async () =>
                    await PublishAsync(conn, tx, run, sources[0], batch)));
                Assert.That(ex!.Number, Is.EqualTo(error));
            }
            finally
            {
                // XACT_ABORT can doom the transaction; always roll back before verifying.
                if (tx.Connection is not null) await tx.RollbackAsync();
            }
        }
        await using var verify = new SqlConnection(_cs);
        await verify.OpenAsync();
        for (var i = 0; i < sources.Count; i++)
        {
            var after = await StateAsync(verify, null, run, sources[i]);
            Assert.That(before[i].Status, Is.EqualTo("PROVISORIA"));
            Assert.That(before[i].Events, Is.Zero);
            Assert.That(after, Is.EqualTo(before[i]), "Failure must preserve state, version, and zero events.");
        }
    }

    private async Task SeedAsync(Guid run, params (Source Source, string Reason, Guid? Best, Guid? Second)[] rows)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT identidade.linkage_run(
                    linkage_run_id,modelo_id,modelo_versao,tipo_run,status,
                    pessoa_observacao_id_filtro,limite_solicitado,escopo_json,batch_size,max_parallelism,
                    pessoa_observacao_id_high_watermark,registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,
                    sem_candidato_no_bloco,solicitado_por,motivo,correlation_id,iniciado_em)
                VALUES(@run,@model,@version,N'ON_DEMAND',N'PREPARANDO',
                    @obs,@count,N'{"test":"no-candidate-literal-physical"}',1,1,
                    @obs,0,0,0,0,0,0,N'CI',N'no-candidate physical guard',NEWID(),SYSUTCDATETIME());
                UPDATE identidade.linkage_run SET status=N'EXECUTANDO',
                    registros_elegiveis=@count,avaliados=@count,nao_resolvidos=@count,
                    sem_candidato_no_bloco=@count WHERE linkage_run_id=@run;
                """;
            cmd.Parameters.AddWithValue("@run", run);
            cmd.Parameters.AddWithValue("@model", _model.Id);
            cmd.Parameters.AddWithValue("@version", _model.Version);
            cmd.Parameters.AddWithValue("@obs", rows[0].Source.ObservationId);
            cmd.Parameters.AddWithValue("@count", rows.Length);
            await cmd.ExecuteNonQueryAsync();
        }
        foreach (var row in rows)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT identidade.linkage_run_item(linkage_run_id,pessoa_observacao_id) VALUES(@run,@obs);
                INSERT identidade.linkage_resultado(
                    linkage_run_id,modelo_id,modelo_versao,pessoa_observacao_id,
                    pessoa_uuid_resolvido,melhor_candidato_uuid,score_melhor,segundo_candidato_uuid,score_segundo,margem,
                    status,motivo,calculado_em,
                    resultado_publicacao,pessoa_uuid_publicado,status_publicacao,motivo_publicacao,
                    pessoa_origem_id_publicado,politica_publicacao_versao,universo_referencia,publicado_em)
                VALUES(@run,@model,@version,@obs,
                    NULL,@best,0,@second,CASE WHEN @second IS NULL THEN NULL ELSE 0.8 END,NULL,
                    N'NAO_RESOLVIDO',@reason,SYSUTCDATETIME(),
                    N'NOVA_IDENTIDADE',@initial,N'RESOLVIDO',N'NOVA_IDENTIDADE_APOS_BUSCA_COMPLETA',
                    @source,N'LINKAGE_PROGRESSIVE_PUBLICATION_V1',N'RUN_COMPLETO_TESTE',SYSUTCDATETIME());
                """;
            cmd.Parameters.AddWithValue("@run", run);
            cmd.Parameters.AddWithValue("@model", _model.Id);
            cmd.Parameters.AddWithValue("@version", _model.Version);
            cmd.Parameters.AddWithValue("@obs", row.Source.ObservationId);
            cmd.Parameters.AddWithValue("@source", row.Source.SourceId);
            cmd.Parameters.AddWithValue("@initial", row.Source.InitialUuid);
            cmd.Parameters.AddWithValue("@reason", row.Reason);
            cmd.Parameters.AddWithValue("@best", (object?)row.Best ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@second", (object?)row.Second ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task PublishAsync(SqlConnection conn, SqlTransaction tx, Guid run, Source source, bool batch)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = batch
            ? "EXEC identidade.sp_publicar_resolucao_progressiva_linkage_lote @linkage_run_id=@run;"
            : "DECLARE @v BIGINT; EXEC identidade.sp_publicar_resolucao_progressiva_linkage @linkage_run_id=@run,@pessoa_observacao_id=@obs,@versao_resultado=@v OUTPUT;";
        cmd.Parameters.AddWithValue("@run", run);
        if (!batch) cmd.Parameters.AddWithValue("@obs", source.ObservationId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<Snapshot> StateAsync(SqlConnection conn, SqlTransaction? tx, Guid run, Source source)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT p.estado,p.canonical_uuid,p.versao,
                   (SELECT COUNT_BIG(*) FROM identidade.pessoa_origem_progressiva_evento e
                     WHERE e.pessoa_origem_id=@source AND e.linkage_run_id=@run),
                   r.progressiva_versao,r.status,r.motivo,r.resultado_publicacao
            FROM identidade.pessoa_origem_progressiva p
            JOIN identidade.linkage_resultado r ON r.linkage_run_id=@run AND r.pessoa_observacao_id=@obs
            WHERE p.pessoa_origem_id=@source;
            """;
        cmd.Parameters.AddWithValue("@source", source.SourceId);
        cmd.Parameters.AddWithValue("@run", run);
        cmd.Parameters.AddWithValue("@obs", source.ObservationId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("Missing persisted synthetic evidence.");
        return new Snapshot(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetInt64(2), reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetInt64(4),
            reader.GetString(5), reader.GetString(6), reader.GetString(7));
    }

    private sealed record Source(long ObservationId, long SourceId, Guid InitialUuid);
    private sealed record Model(Guid Id, int Version);
    private sealed record Snapshot(string Status, Guid? Uuid, long Version, long Events,
        long? ProgressiveVersion, string RawStatus, string RawReason, string Publication);
}
