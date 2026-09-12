using System.Text.Json;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class NameFrequencySnapshotLoaderContractTests
{
    [Test]
    public void CoverageInsert_UsesColumnsDeclaredByCoverageMigration()
    {
        var root = FindRepositoryRoot();
        var loaderPath = Path.Combine(
            root,
            "Solution",
            "src",
            "Jornada.Linkage.Parameters.Worker",
            "NameFrequencySnapshotLoader.cs");
        var migrationPath = Path.Combine(
            root,
            "Solution",
            "database",
            "migrations",
            "20260912_Frequencia_Nomes_Cobertura.sql");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(loaderPath), Is.True);
            Assert.That(File.Exists(migrationPath), Is.True);
        });

        var loader = File.ReadAllText(loaderPath);
        var migration = File.ReadAllText(migrationPath);

        Assert.Multiple(() =>
        {
            Assert.That(migration, Does.Contain("ausencia_semantica NVARCHAR(40) NOT NULL"));
            Assert.That(migration, Does.Contain("origem_endpoint NVARCHAR(400) NOT NULL"));
            Assert.That(loader, Does.Contain("cobertura,ausencia_semantica,origem_endpoint"));
            Assert.That(loader, Does.Not.Contain("ausencia_significa"));
            Assert.That(loader, Does.Not.Contain("cobertura,ausencia_semantica,origem)"));
            Assert.That(loader, Does.Contain("SnapshotJsonOptions,").Or.Contain("SnapshotJsonOptions)"));
            Assert.That(loader, Does.Not.Contain("new JsonSerializerOptions { PropertyNameCaseInsensitive = true },"));
            Assert.That(loader, Does.Contain("ReadProjectionManifestAsync"));
            Assert.That(loader, Does.Contain("ValidateProjectionManifest"));
            Assert.That(loader, Does.Contain("ValidateProjectedFileIntegrity"));
            Assert.That(loader, Does.Contain("canonicalContentSha256"));
            Assert.That(loader, Does.Contain("rowCount"));
        });
    }

    [Test]
    public void ProjectionManifest_IsBoundToOperationalManifest()
    {
        var root = FindRepositoryRoot();
        var referenceRoot = Path.Combine(root, "Solution", "data", "reference", "ibge-nomes-2022");
        var manifestPath = Path.Combine(referenceRoot, "manifest.json");
        var projectionPath = Path.Combine(referenceRoot, "projection-manifest.json");

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        using var projection = JsonDocument.Parse(File.ReadAllText(projectionPath));

        var manifestRoot = manifest.RootElement;
        var projectionRoot = projection.RootElement;
        var snapshot = manifestRoot.GetProperty("snapshot");

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.GetProperty("projectionManifest").GetString(), Is.EqualTo("projection-manifest.json"));
            Assert.That(projectionRoot.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(projectionRoot.GetProperty("referenceCode").GetString(), Is.EqualTo(manifestRoot.GetProperty("referenceCode").GetString()));
            Assert.That(projectionRoot.GetProperty("format").GetString(), Is.EqualTo(snapshot.GetProperty("format").GetString()));
            Assert.That(projectionRoot.GetProperty("generatedFrom").GetString(), Is.EqualTo(snapshot.GetProperty("generatedFrom").GetString()));
        });

        var mainFiles = snapshot.GetProperty("files")
            .EnumerateArray()
            .ToDictionary(x => x.GetProperty("path").GetString()!, StringComparer.Ordinal);
        var projectedFiles = projectionRoot.GetProperty("files")
            .EnumerateArray()
            .ToDictionary(x => x.GetProperty("path").GetString()!, StringComparer.Ordinal);

        Assert.That(projectedFiles.Keys, Is.EquivalentTo(mainFiles.Keys));
        foreach (var (path, mainFile) in mainFiles)
        {
            var projected = projectedFiles[path];
            var canonicalHash = projected.GetProperty("canonicalContentSha256").GetString();

            Assert.Multiple(() =>
            {
                Assert.That(projected.GetProperty("kind").GetString(), Is.EqualTo(mainFile.GetProperty("kind").GetString()), path);
                Assert.That(projected.GetProperty("required").GetBoolean(), Is.EqualTo(mainFile.GetProperty("required").GetBoolean()), path);
                Assert.That(projected.GetProperty("sha256").GetString(), Is.EqualTo(mainFile.GetProperty("sha256").GetString()).IgnoreCase, path);
                Assert.That(projected.GetProperty("rowCount").GetInt64(), Is.GreaterThan(0), path);
                Assert.That(canonicalHash, Has.Length.EqualTo(64), path);
                Assert.That(canonicalHash, Does.Match("^[0-9A-Fa-f]{64}$"), path);
            });
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt")) &&
                Directory.Exists(Path.Combine(current.FullName, "Solution")) &&
                Directory.Exists(Path.Combine(current.FullName, "Documentos")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        Assert.Fail("Raiz do repositório Jornada não encontrada a partir do diretório de testes.");
        return string.Empty;
    }
}