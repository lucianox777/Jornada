using System.Data;
using System.Globalization;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using NUnit.Framework;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration"), NonParallelizable]
public sealed class LinkageImplementationConferenceEvidenceTests
{
    private const string MethodVersion = "JORNADA_IMPLEMENTATION_CONFERENCE_STATE_VECTOR_V1";
    private const string Scope =
        "SCORER_POLICY_ONLY_STATES_AND_GUARD_INPUTS_PRECOMPUTED_COMPARATORS_OUT_OF_SCOPE";
    private const string ToleranceVersion = "TEST_ONLY_FROZEN_V1";
    private const decimal Tolerance = 0.000001m;

    [Test]
    public async Task Conference_evidence_is_append_only_rerunnable_and_latest_result_drives_assertion()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareAsync(connectionString);

        var modelId = Guid.NewGuid();
        var version = 910000 + Random.Shared.Next(1, 80000);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT identidade.modelo_linkage(
                    modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
                    deduplicacao_metodo,base_referencia,gerado_em,amostra_metodo)
                VALUES(
                    @id,@versao,'RASCUNHO','TEST_GOVERNANCE_V1','TEST_NORMALIZATION_V1',
                    'TEST_ONLY','gold.pessoa',SYSDATETIMEOFFSET(),'TEST_ONLY');
                """;
            insert.Parameters.AddWithValue("@id", modelId);
            insert.Parameters.AddWithValue("@versao", version);
            await insert.ExecuteNonQueryAsync();
        }

        var firstConforme = await RegisterAsync(
            connection, modelId, version, "CONFORME", 2, 0.0000005m, true, null, 1);
        Assert.That(firstConforme, Is.Not.EqualTo(Guid.Empty));

        var repeatedConforme = await RegisterAsync(
            connection, modelId, version, "CONFORME", 2, 0.0000005m, true, null, 1);
        Assert.That(repeatedConforme, Is.EqualTo(firstConforme));

        await AssertGateAsync(connection, modelId);

        await RegisterAsync(
            connection, modelId, version, "NAO_EXECUTADA", 0, null, false,
            "ENVIRONMENT_FAILURE", 2);

        var blocked = Assert.ThrowsAsync<SqlException>(async () =>
            await AssertGateAsync(connection, modelId));
        Assert.That(blocked!.Number, Is.EqualTo(51984));

        var secondConforme = await RegisterAsync(
            connection, modelId, version, "CONFORME", 3, 0.0000004m, true, null, 3);
        Assert.That(secondConforme, Is.Not.EqualTo(firstConforme));

        await AssertGateAsync(connection, modelId);

        await using (var mutate = connection.CreateCommand())
        {
            mutate.CommandText = """
                INSERT identidade.parametro_linkage(modelo_id,nome,valor)
                VALUES(@id,N'TEST_POST_CONFERENCE_MUTATION',1);
                """;
            mutate.Parameters.AddWithValue("@id", modelId);
            await mutate.ExecuteNonQueryAsync();
        }

        var stale = Assert.ThrowsAsync<SqlException>(async () =>
            await AssertGateAsync(connection, modelId));
        Assert.That(stale!.Number, Is.EqualTo(51989));

        await using (var immutable = connection.CreateCommand())
        {
            immutable.CommandText = """
                UPDATE auditoria.linkage_conferencia_evidencia
                SET motivo=N'alteração proibida'
                WHERE evidencia_id=@evidencia;
                """;
            immutable.Parameters.AddWithValue("@evidencia", secondConforme);
            var ex = Assert.ThrowsAsync<SqlException>(async () => await immutable.ExecuteNonQueryAsync());
            Assert.That(ex!.Number, Is.EqualTo(51971));
        }

        await using (var count = connection.CreateCommand())
        {
            count.CommandText = """
                SELECT COUNT(*)
                FROM auditoria.linkage_conferencia_evidencia
                WHERE modelo_id=@id;
                """;
            count.Parameters.AddWithValue("@id", modelId);
            Assert.That(Convert.ToInt32(await count.ExecuteScalarAsync(), CultureInfo.InvariantCulture), Is.EqualTo(3));
        }
    }

    [Test]
    public async Task Divergent_evidence_without_primary_divergence_is_rejected()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareAsync(connectionString);

        var modelId = Guid.NewGuid();
        var version = 920000 + Random.Shared.Next(1, 70000);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT identidade.modelo_linkage(
                    modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
                    deduplicacao_metodo,base_referencia,gerado_em,amostra_metodo)
                VALUES(
                    @id,@versao,'RASCUNHO','TEST_GOVERNANCE_V1','TEST_NORMALIZATION_V1',
                    'TEST_ONLY','gold.pessoa',SYSDATETIMEOFFSET(),'TEST_ONLY');
                """;
            insert.Parameters.AddWithValue("@id", modelId);
            insert.Parameters.AddWithValue("@versao", version);
            await insert.ExecuteNonQueryAsync();
        }

        var ex = Assert.ThrowsAsync<SqlException>(async () =>
            await RegisterAsync(
                connection, modelId, version, "DIVERGENTE", 2, null, true,
                "PAIR_LLR_DIVERGENCE", 4));

        Assert.That(ex!.Number, Is.EqualTo(51981));
    }

    [Test]
    public async Task Evidence_table_contains_only_aggregate_governance_fields()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareAsync(connectionString);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sys.columns
            WHERE object_id=OBJECT_ID(N'auditoria.linkage_conferencia_evidencia');
            """;

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("candidatos_avaliados"));
            Assert.That(names, Does.Contain("max_llr_par_observado"));
            Assert.That(names, Does.Contain("report_sha256"));
            Assert.That(names, Does.Not.Contain("candidate_id"));
            Assert.That(names, Does.Not.Contain("cpf"));
            Assert.That(names, Does.Not.Contain("nome"));
            Assert.That(names, Does.Not.Contain("data_nascimento"));
            Assert.That(names, Does.Not.Contain("score_par"));
        });
    }

    private static async Task<Guid> RegisterAsync(
        SqlConnection connection,
        Guid modelId,
        int version,
        string status,
        int candidates,
        decimal? maxObserved,
        bool sameDecision,
        string? reason,
        byte hashSeed)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @e UNIQUEIDENTIFIER;
            EXEC auditoria.sp_registrar_conferencia_linkage
                @modelo_id=@modelo_id,
                @modelo_versao=@modelo_versao,
                @metodo_versao=@metodo_versao,
                @escopo=@escopo,
                @tolerancia_versao=@tolerancia_versao,
                @max_llr_par_permitido=@max_llr_par_permitido,
                @status=@status,
                @candidatos_avaliados=@candidatos_avaliados,
                @max_llr_par_observado=@max_llr_par_observado,
                @max_log_odds_observado=@max_log_odds_observado,
                @mesma_decisao_final=@mesma_decisao_final,
                @mesmo_top1=@mesmo_top1,
                @spearman=@spearman,
                @motivo=@motivo,
                @validacao_estatistica=@validacao_estatistica,
                @request_sha256=@request_sha256,
                @report_sha256=@report_sha256,
                @evidencia_id=@e OUTPUT;
            SELECT @e;
            """;

        command.Parameters.AddWithValue("@modelo_id", modelId);
        command.Parameters.AddWithValue("@modelo_versao", version);
        command.Parameters.AddWithValue("@metodo_versao", MethodVersion);
        command.Parameters.AddWithValue("@escopo", Scope);
        command.Parameters.AddWithValue("@tolerancia_versao", ToleranceVersion);
        command.Parameters.AddWithValue("@max_llr_par_permitido", Tolerance);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@candidatos_avaliados", candidates);
        command.Parameters.AddWithValue("@max_llr_par_observado", (object?)maxObserved ?? DBNull.Value);
        command.Parameters.AddWithValue("@max_log_odds_observado", maxObserved is null ? DBNull.Value : 0.0000005m);
        command.Parameters.AddWithValue("@mesma_decisao_final", sameDecision);
        command.Parameters.AddWithValue("@mesmo_top1", sameDecision);
        command.Parameters.AddWithValue(
            "@spearman",
            (object?)(sameDecision && candidates > 0 ? 1m : null) ?? DBNull.Value);
        command.Parameters.AddWithValue("@motivo", (object?)reason ?? DBNull.Value);
        command.Parameters.AddWithValue("@validacao_estatistica", "NOT_ASSESSED_ISSUE_31");
        command.Parameters.Add("@request_sha256", SqlDbType.Binary, 32).Value =
            Enumerable.Repeat(hashSeed, 32).ToArray();
        command.Parameters.Add("@report_sha256", SqlDbType.Binary, 32).Value =
            Enumerable.Repeat((byte)(hashSeed + 100), 32).ToArray();

        var result = await command.ExecuteScalarAsync();
        return (Guid)result!;
    }

    private static async Task AssertGateAsync(SqlConnection connection, Guid modelId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC auditoria.sp_assert_conferencia_linkage_conforme
                @modelo_id=@modelo_id,
                @metodo_versao=@metodo_versao,
                @tolerancia_versao=@tolerancia_versao,
                @max_llr_par_permitido=@max_llr_par_permitido;
            """;
        command.Parameters.AddWithValue("@modelo_id", modelId);
        command.Parameters.AddWithValue("@metodo_versao", MethodVersion);
        command.Parameters.AddWithValue("@tolerancia_versao", ToleranceVersion);
        command.Parameters.AddWithValue("@max_llr_par_permitido", Tolerance);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task PrepareAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
    }

    private static string RequireIntegrationConnection() =>
        Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION")
        ?? throw new InvalidOperationException("JORNADA_TEST_SQL_CONNECTION não configurada.");
}
