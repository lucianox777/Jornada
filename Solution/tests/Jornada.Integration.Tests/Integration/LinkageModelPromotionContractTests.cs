using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class LinkageModelPromotionContractTests
{
    [Test]
    public async Task Sql_server_migration_materializes_v5_v6_promotion_contract_in_isolated_database()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(
            connection,
            Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(
            connection,
            Path.Combine(databaseDir, "migrations", "20260915_Linkage_Model_Promotion_Contract.sql"));

        await using var command = new SqlCommand(
            "SELECT OBJECT_DEFINITION(OBJECT_ID('identidade.tr_modelo_linkage_promotion_contract'));",
            connection);
        var definition = Convert.ToString(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Multiple(() =>
        {
            Assert.That(definition, Is.Not.Null.And.Not.Empty);
            Assert.That(definition, Does.Contain("FELLEGI_SUNTER_SEMANTIC_BIRTH_V5"));
            Assert.That(definition, Does.Contain("FELLEGI_SUNTER_DECISION_EVIDENCE_V6"));
            Assert.That(definition, Does.Contain("SCORING_BIRTH_SEMANTIC_EVIDENCE_V5"));
            Assert.That(definition, Does.Contain("SCORING_DECISION_EVIDENCE_V6"));
            Assert.That(definition, Does.Contain("CONFLICT_MARGIN_LOG_ODDS"));
            Assert.That(definition, Does.Contain("M_NOME_MAE_MISSING"));
            Assert.That(definition, Does.Contain("U_NOME_MAE_MISSING"));
        });
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");

        var csb = new SqlConnectionStringBuilder(connectionString);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("dev", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.");

        return connectionString;
    }
}
