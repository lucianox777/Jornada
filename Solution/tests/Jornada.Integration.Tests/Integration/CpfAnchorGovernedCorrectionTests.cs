using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class CpfAnchorGovernedCorrectionTests
{
    [Test]
    public async Task Governed_correction_cannot_transfer_permanent_cpf_anchor()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "migrations", "20260907_Cpf_Ancora.sql"));

        long mapId;
        Guid anchorUuid;
        Guid otherUuid;
        long gestorId;
        await using (var fixture = connection.CreateCommand())
        {
            fixture.CommandText = """
                SELECT TOP(1) m.identity_map_id,a.pessoa_uuid,
                       (SELECT TOP(1) p.pessoa_uuid FROM identidade.pessoa p WHERE p.pessoa_uuid<>a.pessoa_uuid ORDER BY p.pessoa_uuid),
                       (SELECT TOP(1) g.gestor_id FROM ref.gestor g ORDER BY g.gestor_id)
                FROM identidade.identity_map m
                JOIN identidade.cpf_ancora a
                  ON a.cpf=CONVERT(CHAR(11),m.identificador) COLLATE Latin1_General_100_BIN2
                WHERE m.tipo='CPF'
                ORDER BY m.identity_map_id;
                """;
            await using var reader = await fixture.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True, "Fixture deve conter um CPF ancorado.");
            mapId = reader.GetInt64(0);
            anchorUuid = reader.GetGuid(1);
            Assert.That(reader.IsDBNull(2), Is.False, "Fixture deve conter outro UUID para testar tentativa de transferência.");
            otherUuid = reader.GetGuid(2);
            gestorId = reader.GetInt64(3);
        }

        var rejectedId = Guid.NewGuid();
        await using (var rejected = connection.CreateCommand())
        {
            rejected.CommandText = """
                INSERT identidade.correcao_identidade(
                    correcao_id,gestor_responsavel_id,identity_map_origem_id,pessoa_uuid_titular,
                    ato_referencia,justificativa,correlation_id)
                VALUES(@id,@gestor,@map,@titular,@ato,@justificativa,@correlation);
                """;
            rejected.Parameters.AddWithValue("@id", rejectedId);
            rejected.Parameters.AddWithValue("@gestor", gestorId);
            rejected.Parameters.AddWithValue("@map", mapId);
            rejected.Parameters.AddWithValue("@titular", otherUuid);
            rejected.Parameters.AddWithValue("@ato", "TESTE-ANCORA-CORRECAO-TRANSFERENCIA");
            rejected.Parameters.AddWithValue("@justificativa", "Tentativa sintética de transferir a âncora permanente.");
            rejected.Parameters.AddWithValue("@correlation", Guid.NewGuid());

            var ex = Assert.ThrowsAsync<SqlException>(async () => await rejected.ExecuteNonQueryAsync());
            Assert.That(ex!.Number, Is.EqualTo(51360));
        }

        await using (var verifyRejected = connection.CreateCommand())
        {
            verifyRejected.CommandText = "SELECT COUNT(*) FROM identidade.correcao_identidade WHERE correcao_id=@id;";
            verifyRejected.Parameters.AddWithValue("@id", rejectedId);
            Assert.That(Convert.ToInt32(await verifyRejected.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture), Is.Zero,
                "A tentativa rejeitada não pode deixar cabeçalho parcial de correção.");
        }

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            var acceptedId = Guid.NewGuid();
            await using var accepted = connection.CreateCommand();
            accepted.Transaction = tx;
            accepted.CommandText = """
                INSERT identidade.correcao_identidade(
                    correcao_id,gestor_responsavel_id,identity_map_origem_id,pessoa_uuid_titular,
                    ato_referencia,justificativa,correlation_id)
                VALUES(@id,@gestor,@map,@titular,@ato,@justificativa,@correlation);
                SELECT COUNT(*) FROM identidade.correcao_identidade WHERE correcao_id=@id;
                """;
            accepted.Parameters.AddWithValue("@id", acceptedId);
            accepted.Parameters.AddWithValue("@gestor", gestorId);
            accepted.Parameters.AddWithValue("@map", mapId);
            accepted.Parameters.AddWithValue("@titular", anchorUuid);
            accepted.Parameters.AddWithValue("@ato", "TESTE-ANCORA-CORRECAO-VALIDA");
            accepted.Parameters.AddWithValue("@justificativa", "Cabeçalho sintético coerente com a âncora permanente.");
            accepted.Parameters.AddWithValue("@correlation", Guid.NewGuid());
            Assert.That(Convert.ToInt32(await accepted.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(1),
                "Correção que preserva a âncora deve continuar permitida.");
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION.");
        var csb = new SqlConnectionStringBuilder(connectionString!);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("dev", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Banco de integração deve conter Test, Dev ou Local.");
        return connectionString!;
    }
}
