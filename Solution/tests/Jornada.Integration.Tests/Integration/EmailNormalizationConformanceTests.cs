using System.Text.Json;
using Jornada.Processor.Worker;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class EmailNormalizationConformanceTests
{
    private static readonly JsonSerializerOptions VectorJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private sealed record EmailVector(string Id, string Input, bool Valid, string? Expected);
    private sealed record EmailVectorFile(
        string Rule,
        int[] EnvelopeTrimUnicodeCodePoints,
        string Lowercase,
        string UnicodeNormalization,
        EmailVector[] Cases);

    [Test]
    public async Task Sql_and_processor_email_v2_match_the_same_conformance_vectors()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var ddl = Path.Combine(AppContext.BaseDirectory, "database", "Jornada_Fase1.sql");
        await SqlBatchRunner.ExecuteFileAsync(connection, ddl);
        var vectors = LoadEmailVectors();

        foreach (var vector in vectors.Cases)
        {
            string? sqlValue;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT ref.fn_email_canonico_v2(@valor);";
                command.Parameters.AddWithValue("@valor", vector.Input);
                var result = await command.ExecuteScalarAsync();
                sqlValue = result is null or DBNull ? null : Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture);
            }

            string? csharpValue = null;
            Exception? csharpError = null;
            try { csharpValue = TransversalAttributeInstanceKey.Compute("MULTI", vectors.Rule, vector.Input); }
            catch (InvalidDataException ex) { csharpError = ex; }

            if (vector.Valid)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(sqlValue, Is.EqualTo(vector.Expected), $"SQL: {vector.Id}");
                    Assert.That(csharpError, Is.Null, $"C#: {vector.Id}");
                    Assert.That(csharpValue, Is.EqualTo(vector.Expected), $"C#: {vector.Id}");
                    Assert.That(sqlValue, Is.EqualTo(csharpValue), $"SQL/C#: {vector.Id}");
                });
            }
            else
            {
                Assert.Multiple(() =>
                {
                    Assert.That(sqlValue, Is.Null, $"SQL deveria falhar fechado: {vector.Id}");
                    Assert.That(csharpError, Is.TypeOf<InvalidDataException>(), $"C# deveria falhar fechado: {vector.Id}");
                });
            }
        }
    }

    private static EmailVectorFile LoadEmailVectors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "email", "email-canonico-v2.json");
        return JsonSerializer.Deserialize<EmailVectorFile>(File.ReadAllText(path), VectorJsonOptions) ?? throw new InvalidDataException("Vetores de conformidade EMAIL_CANONICO_V2 inválidos.");
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
