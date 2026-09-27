using System.Data;
using Microsoft.Data.SqlClient;
using Jornada.Operational.Sql;

namespace Jornada.Tests.Integration;

/// <summary>
/// DT-05 narrow ledger acceptance: four waves over the same observation,
/// with the third wave carrying a late-CPF evidence marker and the fourth changing only score. This is a SQL
/// semantic-ledger test, not proof of a full Processor/Runner CPF replay.
/// </summary>
[TestFixture, Category("Integration"), NonParallelizable]
public sealed class Dt05SemanticThreeWavesSqlServerTests
{
    [Test]
    public async Task Four_waves_ignore_unchanged_and_score_only_retries_without_losing_raw_results()
    {
        var cs = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs))
            Assert.Ignore("JORNADA_TEST_SQL_CONNECTION required.");
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();
        var database = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, database);
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(database, "Jornada_Seed_Dev.sql"));

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            long observationId;
            Guid modelId, initialUuid;
            int version;
            long sourceId;
            await using (var fixture = connection.CreateCommand())
            {
                fixture.Transaction = tx;
                fixture.CommandText = """
                    SELECT TOP(1) po.pessoa_observacao_id,po.pessoa_origem_id,p.initial_uuid,
                           m.modelo_id,m.versao
                    FROM silver.pessoa_observacao po
                    JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
                    CROSS JOIN (SELECT TOP(1) modelo_id,versao FROM identidade.modelo_linkage
                                ORDER BY CASE WHEN status=N'ATIVO' THEN 0 ELSE 1 END,versao DESC) m
                    WHERE po.cpf IS NULL AND p.estado=N'PROVISORIA'
                    ORDER BY po.pessoa_observacao_id DESC;
                    """;
                await using var reader = await fixture.ExecuteReaderAsync();
                Assert.That(await reader.ReadAsync(), Is.True, "Seed precisa de observação sem CPF.");
                observationId = reader.GetInt64(0);
                sourceId = reader.GetInt64(1);
                initialUuid = reader.GetGuid(2);
                modelId = reader.GetGuid(3);
                version = reader.GetInt32(4);
            }

            var runIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            var resultIds = new long[4];
            for (var wave = 0; wave < 4; wave++)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = tx;
                command.CommandText = """
                    INSERT identidade.linkage_run(
                      linkage_run_id,modelo_id,modelo_versao,tipo_run,status,
                      pessoa_observacao_id_filtro,limite_solicitado,escopo_json,batch_size,max_parallelism,
                      pessoa_observacao_id_high_watermark,registros_elegiveis,avaliados,resolvidos,
                      nao_resolvidos,conflitos,sem_candidato_no_bloco,solicitado_por,motivo,
                      correlation_id,iniciado_em)
                    VALUES(@run,@model,@version,N'ON_DEMAND',N'PREPARANDO',
                      @obs,1,N'{"test":"dt05-three-waves"}',1,1,
                      @obs,1,1,0,1,0,1,N'CI',N'DT05 three waves',NEWID(),SYSUTCDATETIME());
                    UPDATE identidade.linkage_run SET status=N'EXECUTANDO' WHERE linkage_run_id=@run;
                    INSERT identidade.linkage_run_item(linkage_run_id,pessoa_observacao_id)
                    VALUES(@run,@obs);
                    INSERT identidade.linkage_resultado(
                      linkage_run_id,modelo_id,modelo_versao,pessoa_observacao_id,
                      pessoa_uuid_resolvido,melhor_candidato_uuid,score_melhor,
                      segundo_candidato_uuid,score_segundo,margem,status,motivo,calculado_em,
                      resultado_publicacao,pessoa_uuid_publicado,status_publicacao,motivo_publicacao,
                      pessoa_origem_id_publicado,politica_publicacao_versao,universo_referencia,publicado_em)
                    OUTPUT INSERTED.linkage_resultado_id
                    VALUES(@run,@model,@version,@obs,
                      NULL,NULL,@score,NULL,NULL,NULL,N'NAO_RESOLVIDO',
                      N'SEM_CANDIDATO_NO_RULESET_BLOCKING',SYSUTCDATETIME(),
                      N'NOVA_IDENTIDADE',@initial,N'RESOLVIDO',@reason,
                      @source,N'LINKAGE_PROGRESSIVE_PUBLICATION_V1',N'RUN_COMPLETO_TESTE',SYSUTCDATETIME());
                    """;
                command.Parameters.AddWithValue("@run", runIds[wave]);
                command.Parameters.AddWithValue("@model", modelId);
                command.Parameters.AddWithValue("@version", version);
                command.Parameters.AddWithValue("@obs", observationId);
                command.Parameters.AddWithValue("@initial", initialUuid);
                command.Parameters.AddWithValue("@source", sourceId);
                command.Parameters.AddWithValue("@score", wave == 3 ? 0.75 : 0.0);
                // The late-CPF marker changes semantic publication evidence in wave 3;
                // actual CPF version ingestion is a separate end-to-end acceptance gate.
                command.Parameters.AddWithValue("@reason", wave >= 2
                    ? "CPF_TARDIO_EVIDENCIA_CONFIRMADA"
                    : "NOVA_IDENTIDADE_APOS_BUSCA_COMPLETA");
                resultIds[wave] = Convert.ToInt64(
                    await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);

                for (var retry = 0; retry < 2; retry++)
                {
                    await using var ledger = connection.CreateCommand();
                    ledger.Transaction = tx;
                    ledger.CommandText = "EXEC identidade.sp_registrar_transicoes_linkage_run @run;";
                    ledger.Parameters.AddWithValue("@run", runIds[wave]);
                    await ledger.ExecuteNonQueryAsync();
                }
                await using var verify = connection.CreateCommand();
                verify.Transaction = tx;
                verify.CommandText = """
                    SELECT COUNT_BIG(*),COUNT(DISTINCT assinatura_sha256)
                    FROM identidade.linkage_transicao_semantica
                    WHERE pessoa_observacao_id=@obs AND linkage_run_id IN (@run1,@run2,@run3,@run4);
                    """;
                verify.Parameters.AddWithValue("@obs", observationId);
                verify.Parameters.AddWithValue("@run1", runIds[0]);
                verify.Parameters.AddWithValue("@run2", runIds[1]);
                verify.Parameters.AddWithValue("@run3", runIds[2]);
                verify.Parameters.AddWithValue("@run4", runIds[3]);
                await using var reader = await verify.ExecuteReaderAsync();
                Assert.That(await reader.ReadAsync(), Is.True);
                Assert.That(reader.GetInt64(0), Is.EqualTo(wave >= 2 ? 2 : 1),
                    $"Wave {wave + 1} must not duplicate unchanged semantic signatures.");
                Assert.That(reader.GetInt32(1), Is.EqualTo(wave == 2 ? 2 : 1));
            }

            await using var final = connection.CreateCommand();
            final.Transaction = tx;
            final.CommandText = """
                SELECT t.transicao_tipo,t.assinatura_sha256,t.assinatura_anterior_sha256
                FROM identidade.linkage_transicao_semantica t
                WHERE t.pessoa_observacao_id=@obs
                  AND t.linkage_run_id IN (@first,@second,@third,@fourth)
                ORDER BY t.transicao_id;
                SELECT COUNT_BIG(*),COUNT(DISTINCT linkage_run_id)
                FROM identidade.linkage_resultado
                WHERE linkage_resultado_id IN (@r1,@r2,@r3,@r4);
                """;
            final.Parameters.AddWithValue("@obs", observationId);
            final.Parameters.AddWithValue("@first", runIds[0]);
            final.Parameters.AddWithValue("@second", runIds[1]);
            final.Parameters.AddWithValue("@third", runIds[2]);
            final.Parameters.AddWithValue("@fourth", runIds[3]);
            final.Parameters.AddWithValue("@r1", resultIds[0]);
            final.Parameters.AddWithValue("@r2", resultIds[1]);
            final.Parameters.AddWithValue("@r3", resultIds[2]);
            final.Parameters.AddWithValue("@r4", resultIds[3]);
            await using var result = await final.ExecuteReaderAsync();
            Assert.That(await result.ReadAsync(), Is.True);
            Assert.That(result.GetString(0), Is.EqualTo("INICIAL"));
            var firstSignature = (byte[])result[1];
            Assert.That(result.IsDBNull(2), Is.True);
            Assert.That(await result.ReadAsync(), Is.True);
            Assert.That(result.GetString(0), Is.EqualTo("ALTERACAO_SEMANTICA"));
            Assert.That((byte[])result[2], Is.EqualTo(firstSignature));
            Assert.That((byte[])result[1], Is.Not.EqualTo(firstSignature));
            Assert.That(await result.ReadAsync(), Is.False);
            Assert.That(await result.NextResultAsync(), Is.True);
            Assert.That(await result.ReadAsync(), Is.True);
            Assert.That(result.GetInt64(0), Is.EqualTo(4), "Every wave retains its raw result.");
            Assert.That(result.GetInt32(1), Is.EqualTo(4));
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }
}
