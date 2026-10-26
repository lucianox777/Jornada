using System.Data;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

/// <summary>
/// Small disposable-only SQL proof. No external Gold, REF active, or historic TEST.
/// Set JORNADA_DT10_V9_HARNESS_CONNECTION only for a newly created LocalDB
/// JornadaHarness bearing the explicit V9 Gold development marker.
/// </summary>
[TestFixture, Category("Integration"), Category("DT10V9"), NonParallelizable]
public sealed class Dt10V9LowerCanonicalLocalDbTests
{
    [Test]
    public async Task Disabled_policy_rejects_lower_and_experimental_reasons_without_ledger_then_legacy_replays()
    {
        var cs = Environment.GetEnvironmentVariable("JORNADA_DT10_V9_HARNESS_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs))
            Assert.Ignore("Requires an explicitly provisioned, disposable LocalDB JornadaHarness.");
        var builder = new SqlConnectionStringBuilder(cs!);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(builder.InitialCatalog, Is.EqualTo("JornadaHarness").IgnoreCase);
            Assert.That(builder.DataSource, Does.StartWith("(localdb)\\").IgnoreCase);
        }));

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();
        await using (var guard = connection.CreateCommand())
        {
            guard.CommandText = """
                SELECT CASE WHEN DB_NAME()=N'JornadaHarness'
                  AND COALESCE(TRY_CONVERT(INT,SERVERPROPERTY(N'IsLocalDB')),0)=1
                  AND EXISTS(SELECT 1 FROM sys.extended_properties
                    WHERE class=0 AND name=N'Jornada.EnvironmentProfile'
                      AND CONVERT(NVARCHAR(200),value)=N'Development')
                  AND EXISTS(SELECT 1 FROM sys.extended_properties
                    WHERE class=0 AND name=N'Jornada.V9.ExperimentalHarness'
                      AND CONVERT(NVARCHAR(200),value)=N'PINNED_FIT_PREVIEW_NO_ACTIVATION')
                  AND OBJECT_ID(N'identidade.v9_dt10_lower_policy',N'U') IS NOT NULL
                  AND OBJECT_ID(N'identidade.sp_publicar_resolucao_progressiva_linkage_lote',N'P') IS NOT NULL
                  THEN 1 ELSE 0 END;
                """;
            Assert.That(Convert.ToInt32(await guard.ExecuteScalarAsync()), Is.EqualTo(1),
                "The test must NEVER initialize or modify an unowned database.");
        }

        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
        await using (var scaleCheck = connection.CreateCommand())
        {
            scaleCheck.CommandText =
                "SELECT COUNT_BIG(*) FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SCALE-%';";
            if (Convert.ToInt64(await scaleCheck.ExecuteScalarAsync()) == 0)
                await SqlBatchRunner.ExecuteFileWithSqlCmdVariablesAsync(
                    connection, Path.Combine(databaseDir, "Jornada_Dev_SyntheticScale.sql"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["SCALE_PEOPLE"] = "1000", ["SCALE_PAIRED"] = "2",
                        ["SCALE_PENDING"] = "1000", ["SCALE_SEED"] = "991",
                        ["SCALE_COLLISION_MODULO"] = "37",
                        ["SCALE_BIRTH_SHIFT_MODULO"] = "29"
                    });
        }

        long sourceId;
        long observationId;
        await using (var find = connection.CreateCommand())
        {
            find.CommandText = """
                SELECT TOP(1) o.pessoa_origem_id,po.pessoa_observacao_id
                FROM silver.pessoa_origem o
                JOIN silver.pessoa_observacao po ON po.pessoa_origem_id=o.pessoa_origem_id
                LEFT JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=o.pessoa_origem_id
                WHERE o.codigo_pessoa_origem LIKE N'SCALE-%'
                  AND po.cpf IS NULL AND (p.estado=N'PROVISORIA' OR p.pessoa_origem_id IS NULL)
                ORDER BY po.pessoa_observacao_id DESC;
                """;
            await using var reader = await find.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            sourceId = reader.GetInt64(0);
            observationId = reader.GetInt64(1);
        }

        await using (var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await using var ensure = connection.CreateCommand();
            ensure.Transaction = tx;
            ensure.CommandText = "EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@source;";
            ensure.Parameters.AddWithValue("@source", sourceId);
            await ensure.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }

        Guid initial;
        Guid model;
        int version;
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = """
                SELECT TOP(1) p.initial_uuid,m.modelo_id,m.versao
                FROM identidade.pessoa_origem_progressiva p
                CROSS JOIN identidade.modelo_linkage m
                WHERE p.pessoa_origem_id=@source
                ORDER BY CASE WHEN m.status=N'ATIVO' THEN 0 ELSE 1 END,m.versao DESC;
                """;
            read.Parameters.AddWithValue("@source", sourceId);
            await using var reader = await read.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            initial = reader.GetGuid(0);
            model = reader.GetGuid(1);
            version = reader.GetInt32(2);
        }

        // No policy row is seeded by the migration. Even a manually supplied
        // reason cannot promote an initial UUID while the feature is off.
        foreach (var (reason, expected) in new[] {
            ("LIMIAR_INFERIOR_V9_GOVERNADO", 51830),
            ("LIMIAR_INFERIOR_GOLD_TRAIN_EXPERIMENTAL", 51810)
        })
        {
            var run = Guid.NewGuid();
            await SeedRunAsync(connection, run, model, version, sourceId, observationId,
                initial, reason);
            var error = await TryPublishAsync(connection, run);
            Assert.That(error, Is.EqualTo(expected));
            Assert.That(await CountEventsAsync(connection, run), Is.Zero);
        }

        // Unchanged no-candidate path must still publish exactly once,
        // including when the same EXECUTANDO run is replayed.
        var legacy = Guid.NewGuid();
        await SeedRunAsync(connection, legacy, model, version, sourceId, observationId,
            initial, "SEM_CANDIDATO_NO_RULESET_BLOCKING");
        Assert.That(await TryPublishAsync(connection, legacy), Is.Null);
        Assert.That(await CountEventsAsync(connection, legacy), Is.EqualTo(1));
        Assert.That(await TryPublishAsync(connection, legacy), Is.Null);
        Assert.That(await CountEventsAsync(connection, legacy), Is.EqualTo(1));
    }

    private static async Task SeedRunAsync(SqlConnection sql, Guid run, Guid model, int version,
        long source, long observation, Guid initial, string reason)
    {
        await using var cmd = sql.CreateCommand();
        cmd.CommandText = """
            INSERT identidade.linkage_run(
              linkage_run_id,modelo_id,modelo_versao,tipo_run,status,limite_solicitado,
              escopo_json,batch_size,max_parallelism,pessoa_observacao_id_high_watermark,
              registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,
              sem_candidato_no_bloco,solicitado_por,motivo,correlation_id,iniciado_em)
            VALUES(@run,@model,@version,N'FULL',N'EXECUTANDO',1,N'{"test":"dt10-v9"}',
              1,1,@obs,1,1,0,1,0,0,N'CI',N'DT10 V9 disposable',NEWID(),SYSUTCDATETIME());
            INSERT identidade.linkage_run_item(linkage_run_id,pessoa_observacao_id)
              VALUES(@run,@obs);
            INSERT identidade.linkage_resultado(
              linkage_run_id,modelo_id,modelo_versao,pessoa_observacao_id,
              pessoa_uuid_resolvido,melhor_candidato_uuid,score_melhor,
              segundo_candidato_uuid,score_segundo,margem,status,motivo,calculado_em,
              resultado_publicacao,pessoa_uuid_publicado,status_publicacao,motivo_publicacao,
              pessoa_origem_id_publicado,politica_publicacao_versao,universo_referencia,publicado_em)
            VALUES(@run,@model,@version,@obs,NULL,NULL,0,NULL,NULL,NULL,N'NAO_RESOLVIDO',
              @reason,SYSUTCDATETIME(),N'NOVA_IDENTIDADE',@initial,N'RESOLVIDO',
              N'NOVA_IDENTIDADE_LIMIAR_INFERIOR_V9_GOVERNADO',@source,
              N'V9_DT10_LOWER_TEST_DISABLED',N'RUN_COMPLETO_DT10',SYSUTCDATETIME());
            """;
        cmd.Parameters.AddWithValue("@run", run);
        cmd.Parameters.AddWithValue("@model", model);
        cmd.Parameters.AddWithValue("@version", version);
        cmd.Parameters.AddWithValue("@source", source);
        cmd.Parameters.AddWithValue("@obs", observation);
        cmd.Parameters.AddWithValue("@initial", initial);
        cmd.Parameters.AddWithValue("@reason", reason);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int?> TryPublishAsync(SqlConnection sql, Guid run)
    {
        await using var tx = (SqlTransaction)await sql.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await using var cmd = sql.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandTimeout = 90;
            cmd.CommandText =
                "EXEC identidade.sp_publicar_resolucao_progressiva_linkage_lote @linkage_run_id=@run;";
            cmd.Parameters.AddWithValue("@run", run);
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
            return null;
        }
        catch (SqlException ex)
        {
            if (tx.Connection is not null) await tx.RollbackAsync();
            return ex.Number;
        }
    }

    private static async Task<long> CountEventsAsync(SqlConnection sql, Guid run)
    {
        await using var cmd = sql.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT_BIG(*) FROM identidade.pessoa_origem_progressiva_evento WHERE linkage_run_id=@run;";
        cmd.Parameters.AddWithValue("@run", run);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
