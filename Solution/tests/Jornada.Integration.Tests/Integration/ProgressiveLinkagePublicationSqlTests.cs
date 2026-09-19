using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class ProgressiveLinkagePublicationSqlTests
{
    [Test]
    public async Task Complete_no_candidate_run_promotes_initial_uuid_once_and_records_receipt()
    {
        var cs = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs)) Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION.");

        var db = new SqlConnectionStringBuilder(cs!).InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("dev", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Banco de integração deve conter Test, Dev ou Local.");

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        long observationId;
        long sourceId;
        Guid modelId;
        int modelVersion;

        await using (var select = connection.CreateCommand())
        {
            select.CommandText = """
                SELECT TOP(1) po.pessoa_observacao_id,po.pessoa_origem_id
                FROM silver.pessoa_observacao po
                JOIN identidade.vinculo_fonte vf
                  ON vf.pessoa_observacao_id=po.pessoa_observacao_id AND vf.ativo=1
                WHERE po.codigo_pessoa_origem='SEH006'
                  AND po.pessoa_origem_id IS NOT NULL
                  AND po.cpf IS NULL
                  AND vf.metodo_resolucao='PENDENTE_PROBABILISTICO'
                ORDER BY po.pessoa_observacao_id DESC;

                SELECT TOP(1) modelo_id,versao
                FROM identidade.modelo_linkage
                ORDER BY CASE WHEN status='ATIVO' THEN 0 ELSE 1 END,versao DESC;
                """;
            await using var reader = await select.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True, "Fixture precisa de SEH006 pendente.");
            observationId = reader.GetInt64(0);
            sourceId = reader.GetInt64(1);
            Assert.That(await reader.NextResultAsync(), Is.True);
            Assert.That(await reader.ReadAsync(), Is.True, "Fixture precisa de modelo de Linkage.");
            modelId = reader.GetGuid(0);
            modelVersion = reader.GetInt32(1);
        }

        Guid initialUuid;
        await using (var ensure = connection.CreateCommand())
        {
            ensure.CommandText = """
                BEGIN TRAN;
                DECLARE @t TABLE(initial_uuid UNIQUEIDENTIFIER,legacy_pessoa_uuid UNIQUEIDENTIFIER,estado VARCHAR(20),versao BIGINT);
                INSERT @t EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@source;
                SELECT initial_uuid FROM @t;
                COMMIT;
                """;
            ensure.Parameters.AddWithValue("@source", sourceId);
            initialUuid = (Guid)(await ensure.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("initial_uuid não criado."));
        }

        var runId = Guid.NewGuid();
        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText = """
                INSERT identidade.linkage_run(
                  linkage_run_id,modelo_id,modelo_versao,tipo_run,status,pessoa_observacao_id_filtro,
                  registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,sem_candidato_no_bloco,
                  iniciado_em)
                VALUES(
                  @run,@model,@model_version,'ON_DEMAND','PREPARANDO',@obs,
                  0,0,0,0,0,0,SYSDATETIMEOFFSET());

                UPDATE identidade.linkage_run
                   SET status='EXECUTANDO',
                       pessoa_observacao_id_high_watermark=@obs,
                       registros_elegiveis=1,
                       avaliados=1,
                       resolvidos=0,
                       nao_resolvidos=1,
                       conflitos=0,
                       sem_candidato_no_bloco=1
                 WHERE linkage_run_id=@run;

                INSERT identidade.linkage_run_item(linkage_run_id,pessoa_observacao_id)
                VALUES(@run,@obs);

                INSERT identidade.linkage_resultado(
                  linkage_run_id,modelo_id,modelo_versao,pessoa_observacao_id,
                  pessoa_uuid_resolvido,melhor_candidato_uuid,score_melhor,
                  segundo_candidato_uuid,score_segundo,margem,status,motivo,calculado_em,
                  resultado_publicacao,pessoa_uuid_publicado,status_publicacao,motivo_publicacao,
                  pessoa_origem_id_publicado,progressiva_versao,politica_publicacao_versao,
                  universo_referencia,publicado_em)
                VALUES(
                  @run,@model,@model_version,@obs,
                  NULL,NULL,0,NULL,NULL,NULL,'NAO_RESOLVIDO','SEM_CANDIDATO_NO_RULESET_BLOCKING',SYSDATETIMEOFFSET(),
                  'NOVA_IDENTIDADE',@initial,'RESOLVIDO','NOVA_IDENTIDADE_APOS_BUSCA_COMPLETA',
                  @source,NULL,'LINKAGE_PROGRESSIVE_PUBLICATION_V1',
                  CONCAT('TEST_RUN:',CONVERT(NVARCHAR(36),@run)),SYSDATETIMEOFFSET());
                """;
            setup.Parameters.AddWithValue("@run", runId);
            setup.Parameters.AddWithValue("@model", modelId);
            setup.Parameters.AddWithValue("@model_version", modelVersion);
            setup.Parameters.AddWithValue("@obs", observationId);
            setup.Parameters.AddWithValue("@source", sourceId);
            setup.Parameters.AddWithValue("@initial", initialUuid);
            await setup.ExecuteNonQueryAsync();
        }

        long firstVersion;
        long secondVersion;
        await using (var publish = connection.CreateCommand())
        {
            publish.CommandText = """
                BEGIN TRAN;
                DECLARE @v BIGINT;
                EXEC identidade.sp_publicar_resolucao_progressiva_linkage
                  @linkage_run_id=@run,@pessoa_observacao_id=@obs,@versao_resultado=@v OUTPUT;
                COMMIT;
                SELECT @v;
                """;
            publish.Parameters.AddWithValue("@run", runId);
            publish.Parameters.AddWithValue("@obs", observationId);
            firstVersion = Convert.ToInt64(await publish.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }

        await using (var replay = connection.CreateCommand())
        {
            replay.CommandText = """
                BEGIN TRAN;
                DECLARE @v BIGINT;
                EXEC identidade.sp_publicar_resolucao_progressiva_linkage
                  @linkage_run_id=@run,@pessoa_observacao_id=@obs,@versao_resultado=@v OUTPUT;
                COMMIT;
                SELECT @v;
                """;
            replay.Parameters.AddWithValue("@run", runId);
            replay.Parameters.AddWithValue("@obs", observationId);
            secondVersion = Convert.ToInt64(await replay.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }

        await using var verify = connection.CreateCommand();
        verify.CommandText = """
            SELECT p.initial_uuid,p.canonical_uuid,p.estado,p.versao,
                   (SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva_evento e
                     WHERE e.pessoa_origem_id=p.pessoa_origem_id AND e.linkage_run_id=@run),
                   (SELECT TOP(1) e.resultado FROM identidade.pessoa_origem_progressiva_evento e
                     WHERE e.pessoa_origem_id=p.pessoa_origem_id AND e.linkage_run_id=@run),
                   (SELECT TOP(1) e.universo_referencia FROM identidade.pessoa_origem_progressiva_evento e
                     WHERE e.pessoa_origem_id=p.pessoa_origem_id AND e.linkage_run_id=@run)
            FROM identidade.pessoa_origem_progressiva p
            WHERE p.pessoa_origem_id=@source;
            """;
        verify.Parameters.AddWithValue("@run", runId);
        verify.Parameters.AddWithValue("@source", sourceId);

        await using var result = await verify.ExecuteReaderAsync();
        Assert.That(await result.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.GetGuid(0), Is.EqualTo(initialUuid));
            Assert.That(result.GetGuid(1), Is.EqualTo(initialUuid));
            Assert.That(result.GetString(2), Is.EqualTo("REFERENCIA"));
            Assert.That(result.GetInt64(3), Is.EqualTo(firstVersion));
            Assert.That(secondVersion, Is.EqualTo(firstVersion));
            Assert.That(result.GetInt32(4), Is.EqualTo(1), "Replay do mesmo run não duplica recibo.");
            Assert.That(result.GetString(5), Is.EqualTo("NOVA_IDENTIDADE"));
            Assert.That(result.GetString(6), Does.Contain(runId.ToString("D")));
        });
    }
}
