using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class LinkageDecisionEvidenceSchemaTests
{
    [Test]
    public async Task V6_margin_storage_and_promotion_gate_are_materialized_in_sql_server()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var column = new SqlCommand(
            """
            SELECT precision, scale
            FROM sys.columns
            WHERE object_id=OBJECT_ID('identidade.linkage_resultado')
              AND name='margem';
            """, connection))
        await using (var reader = await column.ExecuteReaderAsync())
        {
            Assert.That(await reader.ReadAsync(), Is.True, "identidade.linkage_resultado.margem deve existir.");
            Assert.Multiple(() =>
            {
                Assert.That(reader.GetByte(0), Is.EqualTo(19));
                Assert.That(reader.GetByte(1), Is.EqualTo(8));
            });
        }

        await using var scoreConstraint = new SqlCommand(
            """
            SELECT definition
            FROM sys.check_constraints
            WHERE parent_object_id=OBJECT_ID('identidade.linkage_resultado')
              AND name='ck_linkage_resultado_scores';
            """, connection);
        var scoreDefinition = Convert.ToString(
            await scoreConstraint.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(scoreDefinition, Is.Not.Empty);
            Assert.That(scoreDefinition, Does.Match(@"(?i)margem\]?\s*>=\s*\(?0"));
            Assert.That(scoreDefinition, Does.Not.Match(@"(?i)margem\]?\s*<=\s*\(?1"),
                "Margem V6 é diferença de log-odds e não pode continuar limitada ao intervalo de posterior.");
        });

        await using var trigger = new SqlCommand(
            "SELECT OBJECT_DEFINITION(OBJECT_ID('identidade.tr_modelo_linkage_promotion_contract'));", connection);
        var definition = Convert.ToString(await trigger.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);

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
