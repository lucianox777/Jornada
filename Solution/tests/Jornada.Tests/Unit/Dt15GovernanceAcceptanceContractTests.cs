using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class Dt15GovernanceAcceptanceContractTests
{
    [Test]
    public void SyntheticEvidence_IsPersistedButNeverPromotable()
    {
        var root=Root();
        var program=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.Linkage.Evaluation","Program.cs"));
        var dossier=File.ReadAllText(Path.Combine(root,"Solution","database","migrations","20261003_DT15_Decision_Dossier_Contract.sql"));
        Assert.Multiple(() => {
            Assert.That(program,Does.Contain("@estado=N'INCOMPLETO'"));
            Assert.That(program,Does.Contain("@origem_evidencia=N'SINTETICA_DEV'"));
            Assert.That(program,Does.Contain("sp_registrar_dossie_decisao_modelo_linkage"));
            Assert.That(dossier,Does.Contain("IF @estado<>N'COMPLETO'"));
            Assert.That(dossier,Does.Contain("Dossiê decisório não está completo"));
        });
    }

    [Test]
    public void Promotion_RechecksDossierModelAndActiveBase()
    {
        var root=Root();
        var binding=File.ReadAllText(Path.Combine(root,"Solution","database","migrations",
            "20261003_DT15_Human_Approval_Dossier_Binding.sql"));
        Assert.Multiple(() => {
            Assert.That(binding,Does.Contain("@dossie_sha256 BINARY(32)"));
            Assert.That(binding,Does.Contain("sp_assert_dossie_decisao_modelo_linkage"));
            Assert.That(binding,Does.Contain("modelo mudou após aprovação humana"));
            Assert.That(binding,Does.Contain("modelo ATIVO mudou após aprovação humana"));
        });
    }

    [Test]
    public void MasterView_RemainsReadOnlyAndShowsDecisionLedger()
    {
        var root=Root();
        var api=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.Api","ModelGovernanceReadOnlyApi.cs"));
        var service=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.Api","ModelGovernanceReadOnlyService.cs"));
        Assert.Multiple(() => {
            Assert.That(api,Does.Not.Contain("MapPost("));
            Assert.That(api,Does.Not.Contain("MapPut("));
            Assert.That(api,Does.Not.Contain("MapDelete("));
            Assert.That(service,Does.Contain("DecisionDossiers"));
            Assert.That(service,Does.Contain("ValidateApprovals"));
            Assert.That(service,Does.Contain("ActivateApprovals"));
        });
    }

    private static string Root()
    {
        var dir=new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while(dir is not null) {
            if(File.Exists(Path.Combine(dir.FullName,"RELEASE_INFO.txt"))) return dir.FullName;
            dir=dir.Parent;
        }
        throw new DirectoryNotFoundException();
    }
}
