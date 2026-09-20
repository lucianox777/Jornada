using System.Data;
using System.Globalization;
using Jornada.Contracts;
using Jornada.Linkage.Conference;
using Jornada.Linkage.Evaluation;
using Microsoft.Data.SqlClient;
using NUnit.Framework;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration"), NonParallelizable]
public sealed class GovernedImplementationConferenceCommandTests
{
    private const decimal TestTolerance = 0.000001m;

    [Test]
    public void Unfrozen_tolerance_is_rejected_before_any_database_connection()
    {
        var tolerance = new ImplementationConferenceToleranceContract(
            "UNFROZEN",
            "UNFROZEN",
            null);

        var ex = Assert.ThrowsAsync<ConferencePreconditionException>(async () =>
            await GovernedImplementationConferenceCommand.ExecuteAsync(
                "Server=invalid.invalid;Database=NeverOpen;User Id=x;Password=y;TrustServerCertificate=True",
                Guid.NewGuid(),
                tolerance,
                5,
                "test"));

        Assert.That(ex!.Code, Is.EqualTo("TOLERANCE_NOT_FROZEN"));
    }

    [Test]
    public async Task Governed_command_conferences_real_sql_model_and_is_idempotent()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareAsync(connectionString);

        var modelId = Guid.NewGuid();
        var version = 960000 + Random.Shared.Next(1, 30000);

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await EnsureActiveFrequencyReferenceAsync(connection);
            await InsertModelAsync(connection, modelId, version);
        }

        var tolerance = new ImplementationConferenceToleranceContract(
            "TEST_ONLY_FROZEN_V1",
            "FROZEN",
            TestTolerance);

        var first = await GovernedImplementationConferenceCommand.ExecuteAsync(
            connectionString,
            modelId,
            tolerance,
            60,
            "integration-test");
        var second = await GovernedImplementationConferenceCommand.ExecuteAsync(
            connectionString,
            modelId,
            tolerance,
            60,
            "integration-test");

        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.EqualTo(ImplementationConferenceStatus.CONFORME));
            Assert.That(first.ScenarioCount, Is.EqualTo(7));
            Assert.That(first.CandidatesEvaluated, Is.EqualTo(208));
            Assert.That(first.SameFinalDecision, Is.True);
            Assert.That(first.MaxObservedPairLlrDifference, Is.Not.Null.And.LessThanOrEqualTo(TestTolerance));
            Assert.That(first.EvidenceId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(second.EvidenceId, Is.EqualTo(first.EvidenceId));
            Assert.That(second.RequestSha256, Is.EqualTo(first.RequestSha256));
            Assert.That(second.ReportSha256, Is.EqualTo(first.ReportSha256));
        });

        await using var verify = new SqlConnection(connectionString);
        await verify.OpenAsync();

        await using var command = verify.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*),MAX(status),MAX(candidatos_avaliados),
                   MAX(DATALENGTH(modelo_snapshot_sha256)),
                   MAX(DATALENGTH(request_sha256)),
                   MAX(DATALENGTH(report_sha256))
            FROM auditoria.linkage_conferencia_evidencia
            WHERE modelo_id=@id;
            """;
        command.Parameters.AddWithValue("@id", modelId);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetInt32(0), Is.EqualTo(1));
            Assert.That(reader.GetString(1), Is.EqualTo("CONFORME"));
            Assert.That(reader.GetInt32(2), Is.EqualTo(208));
            Assert.That(reader.GetInt32(3), Is.EqualTo(32));
            Assert.That(reader.GetInt32(4), Is.EqualTo(32));
            Assert.That(reader.GetInt32(5), Is.EqualTo(32));
        });
    }

    private static async Task InsertModelAsync(
        SqlConnection connection,
        Guid modelId,
        int version)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using (var model = new SqlCommand(
                """
                INSERT identidade.modelo_linkage(
                    modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
                    deduplicacao_metodo,base_referencia,registros_lidos,pessoas_unicas,
                    gerado_em,amostra_metodo,amostra_pool_tamanho,amostra_m_tamanho,amostra_u_tamanho)
                VALUES(
                    @id,@versao,'RASCUNHO','FELLEGI_SUNTER_DECISION_EVIDENCE_V6',
                    'IDENTITY_NORMALIZATION_V1','TEST_ONLY','gold.pessoa',1,1,
                    SYSDATETIMEOFFSET(),'TEST_ONLY',1,1,1);
                """,
                connection,
                transaction))
            {
                model.Parameters.AddWithValue("@id", modelId);
                model.Parameters.AddWithValue("@versao", version);
                await model.ExecuteNonQueryAsync();
            }

            foreach (var (name, value) in CompleteParameters())
            {
                await using var parameter = new SqlCommand(
                    """
                    INSERT identidade.parametro_linkage(modelo_id,nome,valor)
                    VALUES(@id,@nome,@valor);
                    """,
                    connection,
                    transaction);
                parameter.Parameters.AddWithValue("@id", modelId);
                parameter.Parameters.AddWithValue("@nome", name);
                var valueParameter = parameter.Parameters.Add("@valor", SqlDbType.Decimal);
                valueParameter.Precision = 30;
                valueParameter.Scale = 12;
                valueParameter.Value = value;
                await parameter.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync();
            throw;
        }
    }

    private static IReadOnlyDictionary<string, decimal> CompleteParameters()
    {
        var parameters = new SortedDictionary<string, decimal>(StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .001m,
            [LinkageParameterCatalog.PriorBlockMin] = .000001m,
            [LinkageParameterCatalog.PriorBlockMax] = .25m,
            [LinkageParameterCatalog.Threshold] = .80m,
            [LinkageParameterCatalog.ConflictMargin] = .05m,
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.LogOddsConflictMargin] = .05m,
            [LinkageParameterCatalog.BirthSemanticEvidenceScoring] = 1m,
            [LinkageParameterCatalog.DualThresholdConflictGuard] = 1m,
            [LinkageParameterCatalog.DualThresholdConflictFloorV2] = 1m,
            [LinkageParameterCatalog.DualThresholdConflictFloor] = .70m,
            [LinkageParameterCatalog.NonUniqueDemographicExactGuard] = 1m,
            ["M_NOME_MAE_MISSING"] = .10m,
            ["U_NOME_MAE_MISSING"] = .20m,

            ["M_NOME_EXACT"] = .70m,
            ["M_NOME_HIGH"] = .20m,
            ["M_NOME_MEDIUM"] = .08m,
            ["M_NOME_LOW"] = .02m,
            ["U_NOME_EXACT"] = .01m,
            ["U_NOME_HIGH"] = .03m,
            ["U_NOME_MEDIUM"] = .16m,
            ["U_NOME_LOW"] = .80m,

            ["M_NOME_MAE_EXACT"] = .63m,
            ["M_NOME_MAE_HIGH"] = .18m,
            ["M_NOME_MAE_MEDIUM"] = .072m,
            ["M_NOME_MAE_LOW"] = .018m,
            ["U_NOME_MAE_EXACT"] = .01m,
            ["U_NOME_MAE_HIGH"] = .03m,
            ["U_NOME_MAE_MEDIUM"] = .16m,
            ["U_NOME_MAE_LOW"] = .60m
        };

        foreach (var state in BirthDateSemanticEvidence.States)
        {
            parameters[$"M_NASCIMENTO_SEMANTICO_{state}"] = .142857142857m;
            parameters[$"U_NASCIMENTO_SEMANTICO_{state}"] = .142857142857m;
        }

        return parameters;
    }

    private static async Task EnsureActiveFrequencyReferenceAsync(SqlConnection connection)
    {
        await using (var exists = connection.CreateCommand())
        {
            exists.CommandText =
                "SELECT COUNT(*) FROM ref.frequencia_nome_versao WHERE status='ATIVA';";
            if (Convert.ToInt32(await exists.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0)
                return;
        }

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            long versionId;
            await using (var version = connection.CreateCommand())
            {
                version.Transaction = transaction;
                version.CommandText = """
                    INSERT ref.frequencia_nome_versao(
                        codigo,fonte,edicao,data_referencia,status)
                    OUTPUT INSERTED.frequencia_nome_versao_id
                    VALUES(@codigo,N'CI',N'conference','2022-01-01','CARREGANDO');
                    """;
                version.Parameters.AddWithValue(
                    "@codigo",
                    $"CI-CONFERENCE-{Guid.NewGuid():N}");
                versionId = Convert.ToInt64(await version.ExecuteScalarAsync());
            }

            await using (var rows = connection.CreateCommand())
            {
                rows.Transaction = transaction;
                rows.CommandText = """
                    INSERT ref.frequencia_nome(
                      frequencia_nome_versao_id,tipo,valor,valor_normalizado,sexo,
                      periodo_nascimento,escopo_geografico,uf_codigo,
                      municipio_codigo,frequencia)
                    VALUES
                      (@id,'NOME',N'ANA',N'ANA','TODOS','TODOS','BRASIL','00','0000000',1),
                      (@id,'SOBRENOME',N'SILVA',N'SILVA','TODOS','TODOS','BRASIL','00','0000000',1);
                    """;
                rows.Parameters.AddWithValue("@id", versionId);
                await rows.ExecuteNonQueryAsync();
            }

            await using (var publish = connection.CreateCommand())
            {
                publish.Transaction = transaction;
                publish.CommandType = CommandType.StoredProcedure;
                publish.CommandText = "ref.sp_publicar_frequencia_nome_versao";
                publish.Parameters.AddWithValue("@frequencia_nome_versao_id", versionId);
                publish.Parameters.Add("@conteudo_sha256", SqlDbType.Binary, 32).Value =
                    Enumerable.Repeat((byte)0x6C, 32).ToArray();
                await publish.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task PrepareAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(
            connection,
            Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
    }

    private static string RequireIntegrationConnection() =>
        Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION")
        ?? throw new InvalidOperationException(
            "JORNADA_TEST_SQL_CONNECTION não configurada.");
}
