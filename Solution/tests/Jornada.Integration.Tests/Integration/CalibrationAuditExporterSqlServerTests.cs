using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Evaluation;
using Microsoft.Data.SqlClient;
using NUnit.Framework;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration"), NonParallelizable]
public sealed class CalibrationAuditExporterSqlServerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Test]
    public async Task SqlServer_export_roundtrips_through_the_typed_exchange_contract()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareAsync(connectionString);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var exporter = new CalibrationAuditExporter(connection, 60);
        var document = await exporter.ExportAsync(null, CancellationToken.None);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var imported = LinkageCalibrationAuditRoundTrip.Import(json);

        Assert.DoesNotThrow(() =>
            LinkageCalibrationAuditRoundTrip.VerifyEquivalent(document, imported));

        Assert.Multiple(() =>
        {
            Assert.That(document.Model.Status, Is.EqualTo("ATIVO"));
            Assert.That(document.SchemaVersion, Is.EqualTo(1));
            Assert.That(document.InterchangeContract.UProbabilitySemantics,
                Is.EqualTo(LinkageCalibrationAuditExchangePolicy.UProbabilitySemantics));
            Assert.That(document.InterchangeContract.SplinkDefaultRandomPairUEquivalent, Is.False);
            Assert.That(document.InterchangeContract.NominalNameUSource,
                Is.EqualTo(LinkageCalibrationAuditExchangePolicy.ResolveNominalUSource(document.Parameters, false)));
            Assert.That(document.InterchangeContract.NominalMotherNameUSource,
                Is.EqualTo(LinkageCalibrationAuditExchangePolicy.ResolveNominalUSource(document.Parameters, true)));
            Assert.That(document.TermFrequency.RuntimeEnabled, Is.False);
        });
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
