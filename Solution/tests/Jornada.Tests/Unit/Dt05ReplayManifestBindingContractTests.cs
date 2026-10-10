namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class Dt05ReplayManifestBindingContractTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "database", "migrations")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Solution root");
    }

    [Test]
    public void Manifest_binding_is_create_once_and_requires_captured_pinned_bronze()
    {
        var root = SolutionRoot();
        var migration = File.ReadAllText(Path.Combine(
            root, "database", "migrations", "20261002_Linkage_Replay_Manifest_Binding_DT05.sql"));
        var manifest = File.ReadAllText(Path.Combine(root, "database", "migrations", "manifest.txt"));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(manifest, Does.Contain("migrations/20261002_Linkage_Replay_Manifest_Binding_DT05.sql"));
            Assert.That(migration, Does.Contain("identidade.linkage_replay_manifesto"));
            Assert.That(migration, Does.Contain("sp_registrar_manifesto_replay_linkage"));
            Assert.That(migration, Does.Contain("IF XACT_STATE()=0"));
            Assert.That(migration, Does.Contain("identidade.linkage_bronze_captura"));
            Assert.That(migration, Does.Contain("identidade.linkage_bronze_pin"));
            Assert.That(migration, Does.Contain("@capturados<>@pins"));
            Assert.That(migration, Does.Contain("manifesto do run é imutável e já foi registrado"));
            Assert.That(migration, Does.Contain("linkage-snapshots/v1/manifests/%"));
            Assert.That(migration, Does.Contain("schema_version=1"));
            Assert.That(migration, Does.Contain("scorer_version"));
            Assert.That(migration, Does.Contain("ruleset_version"));
            Assert.That(migration, Does.Contain("model_version"));
            Assert.That(migration, Does.Contain("input_snapshot_id"));
        }));
    }
}
