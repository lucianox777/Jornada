namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class LinkageBootstrapGovernanceMigrationTests
{
    private static string MigrationPath => Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "..", "..", "..", "..", "..", "database", "migrations",
        "20260913_Linkage_Bootstrap_IBGE.sql");

    [Test]
    public void Bootstrap_model_cannot_be_activated()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("PRIOR_BOOTSTRAP"));
            Assert.That(sql, Does.Contain("promocao_automatica_permitida"));
            Assert.That(sql, Does.Contain("tr_modelo_linkage_bloqueia_bootstrap_ativo"));
            Assert.That(sql, Does.Contain("Modelo PRIOR_BOOTSTRAP não pode ser ativado"));
        });
    }

    [Test]
    public void Ibge_collision_uses_active_version_and_published_mass()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("ref.v_frequencia_nome_ativa"));
            Assert.That(sql, Does.Contain("SUM(POWER"));
            Assert.That(sql, Does.Contain("massa_publicada"));
            Assert.That(sql, Does.Not.Contain("frequencia=0"));
        });
    }

    [Test]
    public void Bootstrap_cannot_jump_directly_to_homologated()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.That(sql, Does.Contain("não pode saltar diretamente de PRIOR_BOOTSTRAP para HOMOLOGADO"));
    }
}
