using System.Data;
using Jornada.Linkage.Runner;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration"), NonParallelizable]
public sealed class ProbabilisticLinkageIncrementalEligibilitySqlServerTests
{
    [Test]
    public async Task Incremental_marks_prior_probabilistic_conflict_eligible_after_candidate_universe_changes()
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
            string observationNameKey;
            await using (var fixture = connection.CreateCommand())
            {
                fixture.Transaction = tx;
                fixture.CommandText = """
                    DECLARE @obs BIGINT=(
                        SELECT TOP(1) po.pessoa_observacao_id
                        FROM silver.pessoa_observacao po
                        JOIN identidade.v_vinculo_corrente vc
                          ON vc.pessoa_observacao_id=po.pessoa_observacao_id
                        WHERE po.cpf IS NULL
                          AND vc.metodo_resolucao=N'LINKAGE_PROBABILISTICO'
                          AND vc.status=N'CONFLITO'
                          AND po.nome_cmp IS NOT NULL
                        ORDER BY po.pessoa_observacao_id);

                    SELECT po.pessoa_observacao_id,po.nome_cmp
                    FROM silver.pessoa_observacao po
                    WHERE po.pessoa_observacao_id=@obs;

                    SELECT TOP(1) g.pessoa_uuid
                    FROM gold.pessoa g
                    WHERE g.estado_identidade=N'REFERENCIA'
                      AND NOT EXISTS(
                          SELECT 1
                          FROM identidade.blocking_chave b
                          WHERE b.pessoa_uuid=g.pessoa_uuid
                            AND b.atributo=N'name_full'
                            AND b.valor_normalizado=(
                                SELECT nome_cmp FROM silver.pessoa_observacao WHERE pessoa_observacao_id=@obs)
                            AND b.vigencia_fim IS NULL)
                    ORDER BY g.pessoa_uuid;
                    """;
                await using var reader = await fixture.ExecuteReaderAsync();
                Assert.That(await reader.ReadAsync(), Is.True, "Seed deve conter conflito probabilístico sem CPF.");
                observationId = reader.GetInt64(0);
                observationNameKey = reader.GetString(1);
                Assert.That(await reader.NextResultAsync(), Is.True);
                Assert.That(await reader.ReadAsync(), Is.True, "Seed deve conter referência ainda fora da chave de blocking da observação.");
                candidateUuid = reader.GetGuid(0);
            }

            // Este teste congela apenas a elegibilidade SQL do INCREMENTAL após mudança do lado candidato.
            // Ele não executa um segundo RunAsync nem prova resolução ponta a ponta.
            // A referência passa a compartilhar a chave name_full da observação antiga sem reversioná-la.
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

                    INSERT identidade.blocking_chave(
                        pessoa_uuid,normalizacao_versao,atributo,valor_normalizado,
                        semantica_temporal,vigencia_inicio,vigencia_fim)
                    VALUES(
                        @uuid,@normalizacao,N'name_full',@name_key,
                        N'VERSIONED_ALIAS',SYSUTCDATETIME(),NULL);
                    """;
                candidateSideChange.Parameters.AddWithValue("@uuid", candidateUuid);
                candidateSideChange.Parameters.Add(new SqlParameter("@name_key", SqlDbType.NVarChar, 500) { Value = observationNameKey });
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
                    "Mudança apenas no universo candidato deve tornar o conflito probabilístico elegível no próximo INCREMENTAL; este teste não executa o run completo até a resolução.");
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
