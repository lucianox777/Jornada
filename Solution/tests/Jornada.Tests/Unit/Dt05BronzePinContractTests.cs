namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class Dt05BronzePinContractTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "database", "migrations")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Solution root");
    }

    [Test]
    public void Both_physical_deletion_paths_respect_bronze_pins()
    {
        var root = SolutionRoot();
        var gc = File.ReadAllText(Path.Combine(root, "src", "Jornada.Bronze.Maintenance.Worker", "BronzeMaintenance.cs"));
        var retention = File.ReadAllText(Path.Combine(root, "src", "Jornada.Operations.Maintenance.Worker", "DeliveryBronzeRetentionWorker.cs"));
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations", "20260927_Linkage_Bronze_Pins_DT05.sql"));
        var capture = File.ReadAllText(Path.Combine(root, "database", "migrations", "20260927_Linkage_Bronze_Captura_DT05.sql"));
        var runner = File.ReadAllText(Path.Combine(root, "src", "Jornada.Linkage.Runner", "ProbabilisticLinkageBatchRunner.cs"));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(gc, Does.Contain("FROM identidade.linkage_bronze_pin"));
            Assert.That(gc, Does.Contain("references != 0 || pins != 0"));
            Assert.That(retention, Does.Contain("NOT EXISTS (SELECT 1 FROM identidade.linkage_bronze_pin"));
            Assert.That(retention, Does.Contain("liveReferences += reader.GetInt64(0)"));
            Assert.That(migration, Does.Contain("Jornada.Bronze.Object."));
            Assert.That(capture, Does.Contain("sp_fixar_bronze_para_linkage"));
            Assert.That(capture, Does.Contain("pessoa_observacao_id<=@high_watermark"));
            Assert.That(runner, Does.Contain("await CaptureBronzeSourcesAsync(runId, workCt)"));
            Assert.That(migration, Does.Contain("sp_fixar_bronze_para_linkage"));
            Assert.That(migration, Does.Contain("PRIMARY KEY(linkage_run_id,payload_sha256)"));
            Assert.That(migration, Does.Contain("IX_linkage_bronze_pin_objeto ON identidade.linkage_bronze_pin(payload_sha256)"));
            Assert.That(migration, Does.Contain("estado_armazenamento=N'DISPONIVEL'"));
        }));
    }
}
