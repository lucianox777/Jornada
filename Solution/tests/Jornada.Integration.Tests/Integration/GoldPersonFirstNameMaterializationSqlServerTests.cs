using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class GoldPersonFirstNameMaterializationSqlServerTests
{
    [Test]
    public async Task First_name_is_persisted_recomputed_and_versioned_without_inferring_surnames()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "migrations", "20260912_Gold_Pessoa_Primeiro_Nome.sql"));

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            var personId = Guid.NewGuid();
            await using (var seed = connection.CreateCommand())
            {
                seed.Transaction = tx;
                seed.CommandText = """
                    INSERT identidade.pessoa(pessoa_uuid,status) VALUES(@uuid,'ATIVO');
                    INSERT gold.pessoa(
                        pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,
                        fontes_distintas,estado_concordancia,atualizado_em)
                    VALUES(
                        @uuid,NULL,'SEM_CPF',N'  Maria   Clara da Silva  ','1990-01-02',N'Ana Silva',
                        1,'BASELINE_FONTE_UNICA',SYSDATETIMEOFFSET());
                    """;
                seed.Parameters.AddWithValue("@uuid", personId);
                await seed.ExecuteNonQueryAsync();
            }

            await AssertProjectionAsync(connection, tx, personId, "Maria");

            await using (var update = connection.CreateCommand())
            {
                update.Transaction = tx;
                update.CommandText = "UPDATE gold.pessoa SET nome_completo=@nome WHERE pessoa_uuid=@uuid;";
                update.Parameters.AddWithValue("@uuid", personId);
                update.Parameters.AddWithValue("@nome", "\tJoão\r\nPedro dos Santos");
                await update.ExecuteNonQueryAsync();
            }

            await AssertProjectionAsync(connection, tx, personId, "João");

            await using (var metadata = connection.CreateCommand())
            {
                metadata.Transaction = tx;
                metadata.CommandText = """
                    SELECT c.is_persisted,COLUMNPROPERTY(OBJECT_ID('gold.pessoa'),'nome','IsComputed')
                    FROM sys.computed_columns c
                    WHERE c.object_id=OBJECT_ID('gold.pessoa') AND c.name='nome';
                    """;
                await using var reader = await metadata.ExecuteReaderAsync();
                Assert.That(await reader.ReadAsync(), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(reader.GetBoolean(0), Is.True, "nome deve ser materializado como computed PERSISTED.");
                    Assert.That(reader.GetInt32(1), Is.EqualTo(1), "nome deve continuar sendo coluna computada, não valor mantido manualmente.");
                });
            }

            await using (var forbiddenVersion = connection.CreateCommand())
            {
                forbiddenVersion.Transaction = tx;
                forbiddenVersion.CommandText = "UPDATE gold.pessoa SET nome_semantica_versao='OUTRA_REGRA' WHERE pessoa_uuid=@uuid;";
                forbiddenVersion.Parameters.AddWithValue("@uuid", personId);
                var ex = Assert.ThrowsAsync<SqlException>(async () => await forbiddenVersion.ExecuteNonQueryAsync());
                Assert.That(ex, Is.Not.Null, "A versão semântica materializada não pode divergir silenciosamente da regra física vigente.");
            }
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    private static async Task AssertProjectionAsync(SqlConnection connection, SqlTransaction tx, Guid personId, string expectedName)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT nome,nome_semantica_versao FROM gold.pessoa WHERE pessoa_uuid=@uuid;";
        command.Parameters.AddWithValue("@uuid", personId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo(expectedName));
            Assert.That(reader.GetString(1), Is.EqualTo("IBGE_CENSO_2022_NOMES_PUBLICACAO_V1"));
        });
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");
        var csb = new SqlConnectionStringBuilder(connectionString);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase) &&
            !db.Contains("dev", StringComparison.OrdinalIgnoreCase) &&
            !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.");
        return connectionString!;
    }
}
