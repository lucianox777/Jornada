namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class Dt15HumanModelApprovalContractTests
{
    [Test]
    public void Validate_and_activate_require_distinct_immutable_human_approvals()
    {
        var root=TestContext.CurrentContext.TestDirectory;
        var solution=Path.GetFullPath(Path.Combine(root,"..","..","..","..",".."));
        var migration=File.ReadAllText(Path.Combine(solution,"database","migrations","20261002_DT15_Human_Model_Approval.sql"));
        var dossierBinding=File.ReadAllText(Path.Combine(solution,"database","migrations","20261003_DT15_Human_Approval_Dossier_Binding.sql"));
        var worker=File.ReadAllText(Path.Combine(solution,"src","Jornada.Linkage.Parameters.Worker","LinkageParametersWorker.cs"));
        Assert.Multiple(() => {
            Assert.That(migration,Does.Contain("modelo_linkage_aprovacao"));
            Assert.That(migration,Does.Contain("INSTEAD OF UPDATE,DELETE"));
            Assert.That(migration,Does.Contain("sp_calcular_fingerprint_modelo_linkage"));
            Assert.That(migration,Does.Contain("ativo_base_modelo_id"));
            Assert.That(migration,Does.Contain("modelo mudou após aprovação humana"));
            Assert.That(migration,Does.Contain("modelo ATIVO mudou após aprovação humana"));
            Assert.That(dossierBinding,Does.Contain("@dossie_sha256 BINARY(32)"));
            Assert.That(dossierBinding,Does.Contain("sp_assert_dossie_decisao_modelo_linkage"));
            Assert.That(dossierBinding,Does.Contain("aprovação humana não está vinculada a dossiê decisório"));
            Assert.That(worker,Does.Contain("sp_assert_aprovacao_modelo_linkage @modelo_id=@modelo_id,@acao=N'VALIDATE'"));
            Assert.That(worker,Does.Contain("sp_assert_aprovacao_modelo_linkage @modelo_id=@modelo_id,@acao=N'ACTIVATE'"));
        });
    }
}
