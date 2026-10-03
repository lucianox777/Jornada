using System.Text.RegularExpressions;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class SolutionSchema371CloseoutTests
{
    private static string Root()
    {
        var d=new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while(d is not null){ if(File.Exists(Path.Combine(d.FullName,"RELEASE_INFO.txt"))) return d.FullName; d=d.Parent; }
        throw new DirectoryNotFoundException();
    }

    [Test]
    public void Participants_are_role_explicit_and_never_identity_anchors()
    {
        var sql=File.ReadAllText(Path.Combine(Root(),"Solution","database","migrations","20261003_SolutionSchema_371_Participantes_Divergencia.sql"));
        Assert.Multiple(() => {
            Assert.That(sql,Does.Contain("RESPONSAVEL_RECEBIMENTO"));
            Assert.That(sql,Does.Contain("BENEFICIARIO"));
            Assert.That(sql,Does.Contain("pessoa_observacao_id"));
            Assert.That(sql,Does.Not.Contain("INSERT identidade.identity_map").IgnoreCase);
            Assert.That(sql,Does.Not.Contain("UPDATE identidade.identity_map").IgnoreCase);
        });
    }

    [Test]
    public void Causal_fingerprint_excludes_scores_and_model_ids_and_is_idempotent()
    {
        var sql=File.ReadAllText(Path.Combine(Root(),"Solution","database","migrations","20261003_SolutionSchema_371_Participantes_Divergencia.sql"));
        var proc=sql[(sql.IndexOf("CREATE OR ALTER PROCEDURE qualidade.sp_registrar_divergencia_causal_v1",StringComparison.Ordinal))..];
        Assert.Multiple(() => {
            Assert.That(proc,Does.Contain("conteudo_hash"));
            Assert.That(proc,Does.Contain("candidatos_canonicos"));
            Assert.That(proc,Does.Contain("DIVERGENCIA_CAUSAL_V1"));
            Assert.That(proc,Does.Contain("same causal state never reopens"));
            Assert.That(proc,Does.Not.Contain("score_melhor").IgnoreCase);
            Assert.That(proc,Does.Not.Contain("modelo_id").IgnoreCase);
        });
    }

    [Test]
    public void Manifest_runs_371_delta_before_schema_consolidation()
    {
        var lines=File.ReadAllLines(Path.Combine(Root(),"Solution","database","migrations","manifest.txt"));
        var delta=Array.IndexOf(lines,"migrations/20261003_SolutionSchema_371_Participantes_Divergencia.sql");
        var consolidation=Array.IndexOf(lines,"migrations/20260910_Schema_Consolidation_370.sql");
        Assert.That(delta,Is.GreaterThanOrEqualTo(0));
        Assert.That(consolidation,Is.GreaterThan(delta));
    }
}
