using Jornada.Linkage.Parameters.Worker;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
public sealed class IbgeNominalUPortableArtifactStoreTests
{
    private static readonly IbgeNominalUReferenceInfo Reference = new(
        1, "CENSO2022_NOMES_BRASIL_V1", "IBGE", new string('a', 64));

    [Test]
    public async Task Missing_portable_artifact_is_an_explicit_cache_miss_without_touching_sql()
    {
        await using var connection = new SqlConnection();
        var missing = Path.Combine(Path.GetTempPath(), $"jornada-ibge-u-missing-{Guid.NewGuid():N}.json");

        var imported = await IbgeNominalUPortableArtifactStore.TryImportAsync(
            connection, Reference, 20260917, 1_000_000, missing, CancellationToken.None);

        Assert.That(imported, Is.False);
    }

    [Test]
    public void Incompatible_portable_artifact_fails_closed_before_sql_import()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jornada-ibge-u-invalid-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"schemaVersion":"INCOMPATIBLE"}""");
        try
        {
            using var connection = new SqlConnection();
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await IbgeNominalUPortableArtifactStore.TryImportAsync(
                    connection, Reference, 20260917, 1_000_000, path, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
