using System.Text.Json;
using Jornada.Api;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class Dt23MonitorReadOnlyContractTests
{
    [TestCase("VERDE")]
    [TestCase("AMARELO")]
    [TestCase("VERMELHO")]
    [TestCase("INDETERMINADO")]
    public void Unverified_persisted_states_cannot_certify_evidence(string ignoredDatabaseState)
    {
        // DB status is deliberately absent from the projection constructor:
        // raw SQL declarations are never authentication of independent truth.
        var cutoff = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        var preview = Dt23MonitorReadOnlyService.Unverified("DT23_DEV_V1", cutoff);
        Assert.Multiple(() =>
        {
            Assert.That(preview.State, Is.EqualTo("INDETERMINADO"));
            Assert.That(preview.Provenance.SourceKind, Is.EqualTo("UNVERIFIED"));
            Assert.That(preview.Provenance.Verified, Is.False);
            Assert.That(preview.PolicyVersion, Is.EqualTo("DT23_DEV_V1"));
            Assert.That(preview.CutoffUtc, Is.EqualTo(cutoff));
            Assert.That(preview.Reasons, Is.Not.Empty);
        });
    }

    [Test]
    public void No_snapshot_never_fabricates_a_cutoff_or_a_verified_provenance()
    {
        var preview = Dt23MonitorReadOnlyService.Unverified("DT23_NEW", null);
        var json = JsonSerializer.Serialize(preview,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        Assert.Multiple(() =>
        {
            Assert.That(doc.RootElement.GetProperty("cutoffUtc").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(doc.RootElement.GetProperty("state").GetString(), Is.EqualTo("INDETERMINADO"));
            Assert.That(doc.RootElement.GetProperty("provenance").GetProperty("verified").GetBoolean(), Is.False);
        });
    }

    [Test]
    public void Monitor_contract_is_read_only_and_keeps_original_authorization()
    {
        var root = FindRepositoryRoot();
        var api = File.ReadAllText(Path.Combine(root, "Solution", "src", "Jornada.Api", "OperationalMonitorApi.cs"));
        var reader = File.ReadAllText(Path.Combine(root, "Solution", "src", "Jornada.Api", "Dt23MonitorReadOnlyService.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(api, Does.Contain("RequireAuthorization(Permission)"));
            Assert.That(api, Does.Contain("Dt23MonitorReadOnlyService.GetAsync(operationalSql, ct)"));
            Assert.That(reader, Does.Contain("SELECT TOP (1)"));
            Assert.That(reader, Does.Not.Contain("INSERT INTO"));
            Assert.That(reader, Does.Not.Contain("GENERATE_DRAFT"));
            Assert.That(reader, Does.Not.Contain("ACTIVATE"));
        });
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? dir = new(TestContext.CurrentContext.TestDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RELEASE_INFO.txt"))
                && Directory.Exists(Path.Combine(dir.FullName, "Solution"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Jornada repository root missing.");
    }
}
