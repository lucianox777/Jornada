using System.Data;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class OperationalAtomicityTests
{
    [Test]
    public async Task Recalculate_delivery_is_atomic_when_called_directly_without_outer_transaction()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await ApplyDdlAndSeedAsync(connection);

        Guid entregaId;
        bool expectedCompleteness;
        await using (var pick = connection.CreateCommand())
        {
            pick.CommandText = """
                SELECT TOP(1) ri.entrega_id,CONVERT(bit,COALESCE(c.entrega_completa,0))
                FROM serving.registro_integrado ri
                LEFT JOIN ingestao.v_entrega_completude c ON c.entrega_id=ri.entrega_id
                ORDER BY ri.entrega_id;
                """;
            await using var reader = await pick.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True, "Seed deve possuir ao menos um registro Serving.");
            entregaId = reader.GetGuid(0);
            expectedCompleteness = reader.GetBoolean(1);
        }

        var forcedBefore = !expectedCompleteness;
        await using (var force = connection.CreateCommand())
        {
            force.CommandText = "UPDATE serving.registro_integrado SET entrega_completa=@v WHERE entrega_id=@id;";
            force.Parameters.AddWithValue("@v", forcedBefore);
            force.Parameters.AddWithValue("@id", entregaId);
            Assert.That(await force.ExecuteNonQueryAsync(), Is.GreaterThan(0));
        }

        const string trigger = "ingestao.tr_test_atomic_recalcular_entrega";
        try
        {
            await ExecuteNonQueryAsync(connection, $"""
                CREATE OR ALTER TRIGGER {trigger} ON ingestao.entrega AFTER UPDATE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted WHERE entrega_id=CONVERT(uniqueidentifier,'{entregaId:D}'))
                    THROW 51983,'Falha injetada para provar rollback de sp_recalcular_entrega.',1;
                END;
                """);

            await using var command = connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "ingestao.sp_recalcular_entrega";
            command.Parameters.AddWithValue("@entrega_id", entregaId);
            var ex = Assert.ThrowsAsync<SqlException>(async () => await command.ExecuteNonQueryAsync());
            Assert.That(ex!.Number, Is.EqualTo(51983));

            await using var verify = connection.CreateCommand();
            verify.CommandText = "SELECT COUNT(*) FROM serving.registro_integrado WHERE entrega_id=@id AND entrega_completa=@v;";
            verify.Parameters.AddWithValue("@id", entregaId);
            verify.Parameters.AddWithValue("@v", forcedBefore);
            Assert.That(Convert.ToInt32(await verify.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture), Is.GreaterThan(0),
                "A atualização Serving anterior à falha deve ter sido revertida pela própria procedure.");
        }
        finally
        {
            await DropTriggerAsync(connection, trigger);
        }
    }

    [Test]
    public async Task Synchronize_fact_assignment_is_atomic_when_called_directly_without_outer_transaction()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await ApplyDdlAndSeedAsync(connection);

        long observationId;
        long benefitId;
        DateTimeOffset updatedBefore;
        await using (var pick = connection.CreateCommand())
        {
            pick.CommandText = """
                SELECT TOP(1) ro.pessoa_observacao_id,b.beneficio_concedido_id,b.atualizado_em
                FROM gold.beneficio_concedido b
                JOIN silver.registro_observacao ro ON ro.registro_observacao_id=b.registro_observacao_id
                WHERE EXISTS(SELECT 1 FROM serving.registro_integrado ri WHERE ri.registro_observacao_id=ro.registro_observacao_id)
                ORDER BY b.beneficio_concedido_id;
                """;
            await using var reader = await pick.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True, "Seed deve possuir benefício também materializado em Serving.");
            observationId = reader.GetInt64(0);
            benefitId = reader.GetInt64(1);
            updatedBefore = reader.GetDateTimeOffset(2);
        }

        const string trigger = "serving.tr_test_atomic_sync_facts";
        try
        {
            await ExecuteNonQueryAsync(connection, $"""
                CREATE OR ALTER TRIGGER {trigger} ON serving.registro_integrado AFTER UPDATE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(
                    SELECT 1 FROM inserted i
                    JOIN silver.registro_observacao ro ON ro.registro_observacao_id=i.registro_observacao_id
                    WHERE ro.pessoa_observacao_id={observationId})
                    THROW 51984,'Falha injetada para provar rollback de sincronização factual.',1;
                END;
                """);

            await using var command = connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "identidade.sp_sincronizar_atribuicao_fatos";
            command.Parameters.AddWithValue("@pessoa_observacao_id", observationId);
            var ex = Assert.ThrowsAsync<SqlException>(async () => await command.ExecuteNonQueryAsync());
            Assert.That(ex!.Number, Is.EqualTo(51984));

            await using var verify = connection.CreateCommand();
            verify.CommandText = "SELECT atualizado_em FROM gold.beneficio_concedido WHERE beneficio_concedido_id=@id;";
            verify.Parameters.AddWithValue("@id", benefitId);
            var after = (DateTimeOffset)(await verify.ExecuteScalarAsync())!;
            Assert.That(after, Is.EqualTo(updatedBefore),
                "A atualização Gold anterior à falha em Serving deve ter sido revertida pela própria procedure.");
        }
        finally
        {
            await DropTriggerAsync(connection, trigger);
        }
    }

    private static async Task ApplyDdlAndSeedAsync(SqlConnection connection)
    {
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
    }

    private static async Task ExecuteNonQueryAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropTriggerAsync(SqlConnection connection, string twoPartName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"IF OBJECT_ID(N'{twoPartName}',N'TR') IS NOT NULL DROP TRIGGER {twoPartName};";
        await command.ExecuteNonQueryAsync();
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION.");
        var csb = new SqlConnectionStringBuilder(connectionString!);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase) && !db.Contains("dev", StringComparison.OrdinalIgnoreCase) && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Banco de integração deve conter Test, Dev ou Local.");
        return connectionString!;
    }
}
