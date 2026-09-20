using Microsoft.Data.SqlClient;
using System.Data;
using Jornada.Operational.Sql;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class LinkageProgressivePublicationSqlServerTests
{
    [Test]
    public async Task No_candidate_promotes_own_initial_uuid_and_keeps_raw_result_immutable()
    {
        var cs = RequireIntegrationConnection();
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        var source = await ReadProvisionalSourceAsync(connection);
        var model = await ReadModelAsync(connection);
        var runId = Guid.NewGuid();

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await InsertRunAsync(connection, tx, runId, model.ModelId, model.Version, source.ObservationId,
                rawResolved: false, noCandidate: true);

            await using (var result = connection.CreateCommand())
            {
                result.Transaction = tx;
                result.CommandText = """
                    INSERT identidade.linkage_resultado(
                        linkage_run_id,modelo_id,modelo_versao,pessoa_observacao_id,
                        pessoa_uuid_resolvido,melhor_candidato_uuid,score_melhor,segundo_candidato_uuid,score_segundo,margem,
                        status,motivo,calculado_em,
                        resultado_publicacao,pessoa_uuid_publicado,status_publicacao,motivo_publicacao,
                        pessoa_origem_id_publicado,politica_publicacao_versao,universo_referencia,publicado_em)
                    VALUES(
                        @run,@model,@version,@obs,
                        NULL,NULL,0,NULL,NULL,NULL,
                        N'NAO_RESOLVIDO',N'SEM_CANDIDATO_NO_RULESET_BLOCKING',SYSUTCDATETIME(),
                        N'NOVA_IDENTIDADE',@initial,N'RESOLVIDO',N'NOVA_IDENTIDADE_APOS_BUSCA_COMPLETA',
                        @source,N'LINKAGE_PROGRESSIVE_PUBLICATION_V1',N'RUN_COMPLETO_TESTE',SYSUTCDATETIME());
                    """;
                result.Parameters.AddWithValue("@run", runId);
                result.Parameters.AddWithValue("@model", model.ModelId);
                result.Parameters.AddWithValue("@version", model.Version);
                result.Parameters.AddWithValue("@obs", source.ObservationId);
                result.Parameters.AddWithValue("@initial", source.InitialUuid);
                result.Parameters.AddWithValue("@source", source.SourceId);
                await result.ExecuteNonQueryAsync();
            }

            long firstVersion;
            await using (var publish = connection.CreateCommand())
            {
                publish.Transaction = tx;
                publish.CommandText = """
                    DECLARE @v BIGINT;
                    EXEC identidade.sp_publicar_resolucao_progressiva_linkage
                         @linkage_run_id=@run,
                         @pessoa_observacao_id=@obs,
                         @versao_resultado=@v OUTPUT;
                    SELECT @v;
                    """;
                publish.Parameters.AddWithValue("@run", runId);
                publish.Parameters.AddWithValue("@obs", source.ObservationId);
                firstVersion = Convert.ToInt64(await publish.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
            }

            long repeatVersion;
            await using (var repeat = connection.CreateCommand())
            {
                repeat.Transaction = tx;
                repeat.CommandText = """
                    DECLARE @v BIGINT;
                    EXEC identidade.sp_publicar_resolucao_progressiva_linkage
                         @linkage_run_id=@run,
                         @pessoa_observacao_id=@obs,
                         @versao_resultado=@v OUTPUT;
                    SELECT @v;
                    """;
                repeat.Parameters.AddWithValue("@run", runId);
                repeat.Parameters.AddWithValue("@obs", source.ObservationId);
                repeatVersion = Convert.ToInt64(await repeat.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
            }

            await using (var verify = connection.CreateCommand())
            {
                verify.Transaction = tx;
                verify.CommandText = """
                    SELECT p.initial_uuid,p.canonical_uuid,p.estado,p.versao,
                           r.status,r.pessoa_uuid_resolvido,r.motivo,
                           r.resultado_publicacao,r.pessoa_uuid_publicado,r.status_publicacao,
                           (SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva_evento e
                             WHERE e.pessoa_origem_id=@source AND e.linkage_run_id=@run),
                           (SELECT TOP(1) e.resultado FROM identidade.pessoa_origem_progressiva_evento e
                             WHERE e.pessoa_origem_id=@source AND e.linkage_run_id=@run)
                    FROM identidade.pessoa_origem_progressiva p
                    JOIN identidade.linkage_resultado r
                      ON r.linkage_run_id=@run AND r.pessoa_observacao_id=@obs
                    WHERE p.pessoa_origem_id=@source;
                    """;
                verify.Parameters.AddWithValue("@source", source.SourceId);
                verify.Parameters.AddWithValue("@run", runId);
                verify.Parameters.AddWithValue("@obs", source.ObservationId);
                await using var reader = await verify.ExecuteReaderAsync();
                Assert.That(await reader.ReadAsync(), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(reader.GetGuid(0), Is.EqualTo(source.InitialUuid));
                    Assert.That(reader.GetGuid(1), Is.EqualTo(source.InitialUuid));
                    Assert.That(reader.GetString(2), Is.EqualTo("REFERENCIA"));
                    Assert.That(reader.GetInt64(3), Is.EqualTo(firstVersion));
                    Assert.That(repeatVersion, Is.EqualTo(firstVersion), "Retry da mesma publicação não pode avançar a versão.");
                    Assert.That(reader.GetString(4), Is.EqualTo("NAO_RESOLVIDO"), "Resultado bruto do scorer não pode ser reescrito.");
                    Assert.That(reader.IsDBNull(5), Is.True, "Resultado bruto sem candidato continua sem UUID resolvido.");
                    Assert.That(reader.GetString(6), Is.EqualTo("SEM_CANDIDATO_NO_RULESET_BLOCKING"));
                    Assert.That(reader.GetString(7), Is.EqualTo("NOVA_IDENTIDADE"));
                    Assert.That(reader.GetGuid(8), Is.EqualTo(source.InitialUuid));
                    Assert.That(reader.GetString(9), Is.EqualTo("RESOLVIDO"));
                    Assert.That(reader.GetInt32(10), Is.EqualTo(1), "Um run produz no máximo um recibo progressivo por origem.");
                    Assert.That(reader.GetString(11), Is.EqualTo("NOVA_IDENTIDADE"));
                });
            }
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    [Test]
    public async Task Probabilistic_conflict_enters_single_governed_queue_with_auditable_result_context()
    {
        var cs = RequireIntegrationConnection();
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        var source = await ReadProvisionalSourceAsync(connection);
        var model = await ReadModelAsync(connection);
        var runId = Guid.NewGuid();
        Guid candidate;
        await using (var candidateCommand = connection.CreateCommand())
        {
            candidateCommand.CommandText = "SELECT TOP(1) pessoa_uuid FROM identidade.pessoa WHERE pessoa_uuid<>@initial ORDER BY pessoa_uuid;";
            candidateCommand.Parameters.AddWithValue("@initial", source.InitialUuid);
            candidate = (Guid)(await candidateCommand.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("Fixture sem candidato alternativo."));
        }

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await InsertRunAsync(connection, tx, runId, model.ModelId, model.Version, source.ObservationId,
                rawResolved: false, noCandidate: false, rawConflict: true);

            long resultId;
            await using (var result = connection.CreateCommand())
            {
                result.Transaction = tx;
                result.CommandText = """
                    INSERT identidade.linkage_resultado(
                        linkage_run_id,modelo_id,modelo_versao,pessoa_observacao_id,
                        pessoa_uuid_resolvido,melhor_candidato_uuid,score_melhor,
                        segundo_candidato_uuid,score_segundo,margem,status,motivo,calculado_em,
                        resultado_publicacao,pessoa_uuid_publicado,status_publicacao,motivo_publicacao,
                        pessoa_origem_id_publicado,politica_publicacao_versao,universo_referencia,publicado_em)
                    OUTPUT INSERTED.linkage_resultado_id
                    VALUES(
                        @run,@model,@version,@obs,
                        NULL,@candidate,0.83000000,NULL,NULL,0.01000000,
                        N'CONFLITO',N'MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE',SYSUTCDATETIME(),
                        N'INDEFINIDA',NULL,N'CONFLITO',N'MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE',
                        @source,N'LINKAGE_PROGRESSIVE_PUBLICATION_V1',N'RUN_COMPLETO_TESTE',SYSUTCDATETIME());
                    """;
                result.Parameters.AddWithValue("@run", runId);
                result.Parameters.AddWithValue("@model", model.ModelId);
                result.Parameters.AddWithValue("@version", model.Version);
                result.Parameters.AddWithValue("@obs", source.ObservationId);
                result.Parameters.AddWithValue("@candidate", candidate);
                result.Parameters.AddWithValue("@source", source.SourceId);
                resultId = Convert.ToInt64(await result.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
            }

            for (var attempt = 0; attempt < 2; attempt++)
            {
                await using var queue = connection.CreateCommand();
                queue.Transaction = tx;
                queue.CommandText = "EXEC qualidade.sp_registrar_conflitos_linkage_publicados @linkage_run_id=@run_id;";
                queue.Parameters.AddWithValue("@run_id", runId);
                await queue.ExecuteNonQueryAsync();
            }

            await using var verify = connection.CreateCommand();
            verify.Transaction = tx;
            verify.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM qualidade.divergencia_gestor
                      WHERE pessoa_observacao_id=@obs
                        AND status=N'ABERTA'
                        AND tipo=N'DIVERGENCIA_IDENTIDADE'
                        AND linkage_resultado_id IS NOT NULL) fila,
                    d.linkage_resultado_id,
                    c.linkage_run_id,c.modelo_id,c.modelo_versao,
                    c.melhor_candidato_uuid,c.score_melhor,c.margem,c.raw_status,c.raw_motivo
                FROM qualidade.divergencia_gestor d
                JOIN qualidade.v_divergencia_linkage_contexto c
                  ON c.divergencia_id=d.divergencia_id
                WHERE d.pessoa_observacao_id=@obs
                  AND d.status=N'ABERTA'
                  AND d.linkage_resultado_id IS NOT NULL;
                """;
            verify.Parameters.AddWithValue("@obs", source.ObservationId);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reader.GetInt32(0), Is.EqualTo(1), "Replay do mesmo conflito não pode duplicar a fila.");
                Assert.That(reader.GetInt64(1), Is.EqualTo(resultId));
                Assert.That(reader.GetGuid(2), Is.EqualTo(runId));
                Assert.That(reader.GetGuid(3), Is.EqualTo(model.ModelId));
                Assert.That(reader.GetInt32(4), Is.EqualTo(model.Version));
                Assert.That(reader.GetGuid(5), Is.EqualTo(candidate));
                Assert.That(reader.GetDecimal(6), Is.EqualTo(0.83000000m));
                Assert.That(reader.GetDecimal(7), Is.EqualTo(0.01000000m));
                Assert.That(reader.GetString(8), Is.EqualTo("CONFLITO"));
                Assert.That(reader.GetString(9), Is.EqualTo("MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE"));
            });
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    [Test]
    public async Task Partial_provisional_shell_is_visible_in_gold_but_only_reference_receives_available_blocking_keys()
    {
        var cs = RequireIntegrationConnection();
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        var source = await ReadProvisionalSourceAsync(connection);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await using (var partial = connection.CreateCommand())
            {
                partial.Transaction = tx;
                partial.CommandText = """
                    UPDATE silver.pessoa_observacao
                       SET nome_completo=N'Maria Parcial',
                           nome_cmp=N'MARIA PARCIAL',
                           data_nascimento=NULL,
                           nome_mae=NULL,
                           nome_mae_cmp=NULL
                     WHERE pessoa_origem_id=@source;

                    EXEC identidade.sp_recompor_gold_pessoa @pessoa_uuid=@initial;
                    """;
                partial.Parameters.AddWithValue("@source", source.SourceId);
                partial.Parameters.AddWithValue("@initial", source.InitialUuid);
                await partial.ExecuteNonQueryAsync();
            }

            await BlockingProjectionPersistence.RefreshSqlServerAsync(
                connection, tx, source.InitialUuid, CancellationToken.None);

            await using (var provisional = connection.CreateCommand())
            {
                provisional.Transaction = tx;
                provisional.CommandText = """
                    SELECT g.estado_identidade,g.completude_nucleo,g.nome_completo,
                           g.data_nascimento,g.nome_mae,
                           (SELECT COUNT(*) FROM identidade.blocking_chave b WHERE b.pessoa_uuid=g.pessoa_uuid)
                    FROM gold.pessoa g
                    WHERE g.pessoa_uuid=@initial;
                    """;
                provisional.Parameters.AddWithValue("@initial", source.InitialUuid);
                await using var reader = await provisional.ExecuteReaderAsync();
                Assert.That(await reader.ReadAsync(), Is.True, "A casca progressiva deve existir na Gold mesmo com núcleo parcial.");
                Assert.Multiple(() =>
                {
                    Assert.That(reader.GetString(0), Is.EqualTo("PROVISORIA"));
                    Assert.That(reader.GetString(1), Is.EqualTo("PARCIAL"));
                    Assert.That(reader.GetString(2), Is.EqualTo("Maria Parcial"));
                    Assert.That(reader.IsDBNull(3), Is.True);
                    Assert.That(reader.IsDBNull(4), Is.True);
                    Assert.That(reader.GetInt32(5), Is.Zero,
                        "PROVISORIA não pode contaminar o corpus de candidate generation.");
                });
            }

            await using (var promote = connection.CreateCommand())
            {
                promote.Transaction = tx;
                promote.CommandText = """
                    DECLARE @v BIGINT;
                    EXEC identidade.sp_publicar_referencia_progressiva_deterministica
                         @pessoa_origem_id=@source,
                         @canonical_uuid=@initial,
                         @evidencia_referencia=N'TESTE_GOLD_PROGRESSIVA_PARCIAL',
                         @politica_versao=N'TEST_GOLD_PROGRESSIVA_V1',
                         @versao_resultado=@v OUTPUT;
                    EXEC identidade.sp_recompor_gold_pessoa @pessoa_uuid=@initial;
                    """;
                promote.Parameters.AddWithValue("@source", source.SourceId);
                promote.Parameters.AddWithValue("@initial", source.InitialUuid);
                await promote.ExecuteNonQueryAsync();
            }

            await BlockingProjectionPersistence.RefreshSqlServerAsync(
                connection, tx, source.InitialUuid, CancellationToken.None);

            await using var reference = connection.CreateCommand();
            reference.Transaction = tx;
            reference.CommandText = """
                SELECT g.estado_identidade,g.completude_nucleo,
                       (SELECT COUNT(*) FROM identidade.blocking_chave b WHERE b.pessoa_uuid=g.pessoa_uuid) total_keys,
                       (SELECT COUNT(*) FROM identidade.blocking_chave b
                         WHERE b.pessoa_uuid=g.pessoa_uuid
                           AND b.atributo IN(N'birth_day',N'birth_month',N'birth_year')) birth_keys
                FROM gold.pessoa g
                WHERE g.pessoa_uuid=@initial;
                """;
            reference.Parameters.AddWithValue("@initial", source.InitialUuid);
            await using var referenceReader = await reference.ExecuteReaderAsync();
            Assert.That(await referenceReader.ReadAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(referenceReader.GetString(0), Is.EqualTo("REFERENCIA"));
                Assert.That(referenceReader.GetString(1), Is.EqualTo("PARCIAL"),
                    "Resolver identidade não deve fabricar completude cadastral.");
                Assert.That(referenceReader.GetInt32(2), Is.GreaterThan(0),
                    "A referência deve projetar chaves derivadas do nome disponível.");
                Assert.That(referenceReader.GetInt32(3), Is.Zero,
                    "Data ausente não pode gerar chaves de nascimento sintéticas.");
            });
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    [Test]
    public async Task Existing_established_target_is_associated_without_using_initial_uuid_as_score_evidence()
    {
        var cs = RequireIntegrationConnection();
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        var source = await ReadProvisionalSourceAsync(connection);
        var model = await ReadModelAsync(connection);
        Guid target;
        await using (var anchor = connection.CreateCommand())
        {
            anchor.CommandText = "SELECT TOP(1) pessoa_uuid FROM identidade.cpf_ancora ORDER BY cpf;";
            target = (Guid)(await anchor.ExecuteScalarAsync() ?? throw new InvalidOperationException("Fixture sem âncora CPF."));
        }

        var runId = Guid.NewGuid();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await InsertRunAsync(connection, tx, runId, model.ModelId, model.Version, source.ObservationId,
                rawResolved: true, noCandidate: false);

            await using (var result = connection.CreateCommand())
            {
                result.Transaction = tx;
                result.CommandText = """
                    INSERT identidade.linkage_resultado(
                        linkage_run_id,modelo_id,modelo_versao,pessoa_observacao_id,
                        pessoa_uuid_resolvido,melhor_candidato_uuid,score_melhor,segundo_candidato_uuid,score_segundo,margem,
                        status,motivo,calculado_em,
                        resultado_publicacao,pessoa_uuid_publicado,status_publicacao,motivo_publicacao,
                        pessoa_origem_id_publicado,politica_publicacao_versao,universo_referencia,publicado_em)
                    VALUES(
                        @run,@model,@version,@obs,
                        @target,@target,0.99000000,NULL,NULL,NULL,
                        N'RESOLVIDO',NULL,SYSUTCDATETIME(),
                        N'ASSOCIACAO_EXISTENTE',@target,N'RESOLVIDO',N'ASSOCIACAO_EXISTENTE_LINKAGE',
                        @source,N'LINKAGE_PROGRESSIVE_PUBLICATION_V1',N'RUN_COMPLETO_TESTE',SYSUTCDATETIME());
                    """;
                result.Parameters.AddWithValue("@run", runId);
                result.Parameters.AddWithValue("@model", model.ModelId);
                result.Parameters.AddWithValue("@version", model.Version);
                result.Parameters.AddWithValue("@obs", source.ObservationId);
                result.Parameters.AddWithValue("@target", target);
                result.Parameters.AddWithValue("@source", source.SourceId);
                await result.ExecuteNonQueryAsync();
            }

            await using (var publish = connection.CreateCommand())
            {
                publish.Transaction = tx;
                publish.CommandText = """
                    DECLARE @v BIGINT;
                    EXEC identidade.sp_publicar_resolucao_progressiva_linkage
                         @linkage_run_id=@run,
                         @pessoa_observacao_id=@obs,
                         @versao_resultado=@v OUTPUT;
                    """;
                publish.Parameters.AddWithValue("@run", runId);
                publish.Parameters.AddWithValue("@obs", source.ObservationId);
                await publish.ExecuteNonQueryAsync();
            }

            await using var verify = connection.CreateCommand();
            verify.Transaction = tx;
            verify.CommandText = """
                SELECT p.initial_uuid,p.canonical_uuid,p.estado,
                       r.pessoa_uuid_resolvido,r.score_melhor,r.pessoa_uuid_publicado,
                       e.resultado,e.target_uuid
                FROM identidade.pessoa_origem_progressiva p
                JOIN identidade.linkage_resultado r ON r.linkage_run_id=@run AND r.pessoa_observacao_id=@obs
                JOIN identidade.pessoa_origem_progressiva_evento e
                  ON e.pessoa_origem_id=p.pessoa_origem_id AND e.linkage_run_id=@run
                WHERE p.pessoa_origem_id=@source;
                """;
            verify.Parameters.AddWithValue("@run", runId);
            verify.Parameters.AddWithValue("@obs", source.ObservationId);
            verify.Parameters.AddWithValue("@source", source.SourceId);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reader.GetGuid(0), Is.EqualTo(source.InitialUuid));
                Assert.That(reader.GetGuid(1), Is.EqualTo(target));
                Assert.That(reader.GetString(2), Is.EqualTo("REFERENCIA"));
                Assert.That(reader.GetGuid(3), Is.EqualTo(target), "Resultado bruto continua apontando o candidato do scorer.");
                Assert.That(reader.GetDecimal(4), Is.EqualTo(0.99000000m), "Score bruto permanece independente do initial_uuid.");
                Assert.That(reader.GetGuid(5), Is.EqualTo(target));
                Assert.That(reader.GetString(6), Is.EqualTo("ASSOCIACAO_EXISTENTE"));
                Assert.That(reader.GetGuid(7), Is.EqualTo(target));
            });
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    private static async Task InsertRunAsync(
        SqlConnection connection, SqlTransaction tx, Guid runId, Guid modelId, int modelVersion,
        long observationId, bool rawResolved, bool noCandidate, bool rawConflict = false)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT identidade.linkage_run(
                linkage_run_id,modelo_id,modelo_versao,tipo_run,status,
                pessoa_observacao_id_filtro,limite_solicitado,escopo_json,batch_size,max_parallelism,
                pessoa_observacao_id_high_watermark,registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,
                sem_candidato_no_bloco,solicitado_por,motivo,correlation_id,iniciado_em)
            VALUES(
                @run,@model,@version,N'ON_DEMAND',N'PREPARANDO',
                @obs,1,N'{"test":"progressive-publication"}',1,1,
                @obs,0,0,0,0,0,0,
                N'CI',N'progressive publication integration',NEWID(),SYSUTCDATETIME());

            UPDATE identidade.linkage_run
               SET status=N'EXECUTANDO',
                   registros_elegiveis=1,
                   avaliados=1,
                   resolvidos=@resolved,
                   nao_resolvidos=@unresolved,
                   conflitos=@conflicts,
                   sem_candidato_no_bloco=@no_candidate
             WHERE linkage_run_id=@run;

            INSERT identidade.linkage_run_item(linkage_run_id,pessoa_observacao_id)
            VALUES(@run,@obs);
            """;
        command.Parameters.AddWithValue("@run", runId);
        command.Parameters.AddWithValue("@model", modelId);
        command.Parameters.AddWithValue("@version", modelVersion);
        command.Parameters.AddWithValue("@obs", observationId);
        command.Parameters.AddWithValue("@resolved", rawResolved ? 1 : 0);
        command.Parameters.AddWithValue("@unresolved", rawResolved || rawConflict ? 0 : 1);
        command.Parameters.AddWithValue("@conflicts", rawConflict ? 1 : 0);
        command.Parameters.AddWithValue("@no_candidate", noCandidate ? 1 : 0);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<SourceFixture> ReadProvisionalSourceAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP(1) po.pessoa_observacao_id,po.pessoa_origem_id,p.initial_uuid
            FROM silver.pessoa_observacao po
            JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
            WHERE po.pessoa_origem_id IS NOT NULL
              AND po.cpf IS NULL
              AND p.estado=N'PROVISORIA'
            ORDER BY po.pessoa_observacao_id DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("Fixture sem origem persistente PROVISORIA e sem CPF.");
        return new SourceFixture(reader.GetInt64(0), reader.GetInt64(1), reader.GetGuid(2));
    }

    private static async Task<ModelFixture> ReadModelAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP(1) modelo_id,versao FROM identidade.modelo_linkage ORDER BY CASE WHEN status=N'ATIVO' THEN 0 ELSE 1 END,versao DESC;";
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("Fixture sem modelo de Linkage.");
        return new ModelFixture(reader.GetGuid(0), reader.GetInt32(1));
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");

        var db = new SqlConnectionStringBuilder(connectionString!).InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("dev", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Banco de integração deve conter Test, Dev ou Local.");

        return connectionString!;
    }

    private sealed record SourceFixture(long ObservationId,long SourceId,Guid InitialUuid);
    private sealed record ModelFixture(Guid ModelId,int Version);
}
