using System.Data;
using Jornada.Linkage.Runner;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration"), NonParallelizable]
public sealed class ProbabilisticLinkageIncrementalEligibilitySqlServerTests
{
    [Test]
    public async Task Incremental_includes_prior_probabilistic_conflict_after_candidate_universe_changes()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var databaseDir = Path.Combine(FindRepositoryRoot(), "Solution", "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            long observationId;
            Guid candidateUuid;
            await using (var fixture = connection.CreateCommand())
            {
                fixture.Transaction = tx;
                fixture.CommandText = """
                    SELECT TOP(1) po.pessoa_observacao_id
                    FROM silver.pessoa_observacao po
                    JOIN identidade.v_vinculo_corrente vc
                      ON vc.pessoa_observacao_id=po.pessoa_observacao_id
                    WHERE po.cpf IS NULL
                      AND vc.metodo_resolucao=N'LINKAGE_PROBABILISTICO'
                      AND vc.status=N'CONFLITO'
                    ORDER BY po.pessoa_observacao_id;

                    SELECT TOP(1) pessoa_uuid
                    FROM gold.pessoa
                    WHERE estado_identidade=N'REFERENCIA'
                    ORDER BY pessoa_uuid;
                    """;
                await using var reader = await fixture.ExecuteReaderAsync();
                Assert.That(await reader.ReadAsync(), Is.True, "Seed deve conter conflito probabilístico sem CPF.");
                observationId = reader.GetInt64(0);
                Assert.That(await reader.NextResultAsync(), Is.True);
                Assert.That(await reader.ReadAsync(), Is.True, "Seed deve conter candidato canônico.");
                candidateUuid = reader.GetGuid(0);
            }

            // Simula somente a mudança causal do lado candidato: a observação antiga não é versionada.
            await using (var candidateSideChange = connection.CreateCommand())
            {
                candidateSideChange.Transaction = tx;
                candidateSideChange.CommandText = """
                    DECLARE @normalizacao NVARCHAR(80)=(
                        SELECT TOP(1) normalizacao_versao
                        FROM identidade.modelo_linkage
                        WHERE status=N'ATIVO'
                        ORDER BY versao DESC);

                    IF @normalizacao IS NULL
                        SELECT @normalizacao=N'JORNADA_IDENTITY_NORMALIZATION_V1';

                    IF NOT EXISTS(
                        SELECT 1 FROM identidade.blocking_chave
                        WHERE pessoa_uuid=@uuid
                          AND normalizacao_versao=@normalizacao
                          AND atributo=N'name_full'
                          AND valor_normalizado=N'CANDIDATE_SIDE_CHANGE_TEST'
                          AND vigencia_fim IS NULL)
                    INSERT identidade.blocking_chave(
                        pessoa_uuid,normalizacao_versao,atributo,valor_normalizado,
                        semantica_temporal,vigencia_inicio,vigencia_fim)
                    VALUES(
                        @uuid,@normalizacao,N'name_full',N'CANDIDATE_SIDE_CHANGE_TEST',
                        N'VERSIONED_ALIAS',SYSUTCDATETIME(),NULL);
                    """;
                candidateSideChange.Parameters.AddWithValue("@uuid", candidateUuid);
                await candidateSideChange.ExecuteNonQueryAsync();
            }

            var beforeVersionCount = await CountObservationVersionsAsync(connection, tx, observationId);

            await using var eligibility = connection.CreateCommand();
            eligibility.Transaction = tx;
            eligibility.CommandText = $"""
                SELECT COUNT_BIG(*)
                {ProbabilisticLinkageBatchRunner.EligibleFromWhereSql()}
                  AND po.pessoa_observacao_id=@target;
                """;
            eligibility.Parameters.AddWithValue("@high_watermark", long.MaxValue);
            eligibility.Parameters.Add(new SqlParameter("@pessoa_observacao_id", SqlDbType.BigInt) { Value = DBNull.Value });
            eligibility.Parameters.Add(new SqlParameter("@gestor_codigo", SqlDbType.NVarChar, 30) { Value = DBNull.Value });
            eligibility.Parameters.Add(new SqlParameter("@desde", SqlDbType.DateTimeOffset) { Value = DBNull.Value });
            eligibility.Parameters.Add(new SqlParameter("@mode", SqlDbType.NVarChar, 30) { Value = "INCREMENTAL" });
            eligibility.Parameters.AddWithValue("@target", observationId);

            var eligible = Convert.ToInt64(await eligibility.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
            var afterVersionCount = await CountObservationVersionsAsync(connection, tx, observationId);

            Assert.Multiple(() =>
            {
                Assert.That(eligible, Is.EqualTo(1),
                    "Mudança apenas no universo candidato deve recolocar conflito probabilístico no INCREMENTAL.");
                Assert.That(afterVersionCount, Is.EqualTo(beforeVersionCount),
                    "Reavaliação não pode depender de nova versão da observação antiga.");
            });
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    private static async Task<long> CountObservationVersionsAsync(
        SqlConnection connection,
        SqlTransaction tx,
        long observationId)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT COUNT_BIG(*)
            FROM silver.pessoa_observacao
            WHERE pessoa_origem_id=(
                SELECT pessoa_origem_id
                FROM silver.pessoa_observacao
                WHERE pessoa_observacao_id=@obs);
            """;
        command.Parameters.AddWithValue("@obs", observationId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Solution", "database", "Jornada_Fase1.sql")))
                return current.FullName;
            current = current.Parent;
        }

        Assert.Fail("Raiz do repositório Jornada não encontrada.");
        return string.Empty;
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar integração SQL Server.");
        return connectionString!;
    }
}
