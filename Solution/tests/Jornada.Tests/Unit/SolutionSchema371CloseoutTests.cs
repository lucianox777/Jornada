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
        Assert.Multiple((Action)(() => {
            Assert.That(sql,Does.Contain("RESPONSAVEL_RECEBIMENTO"));
            Assert.That(sql,Does.Contain("BENEFICIARIO"));
            Assert.That(sql,Does.Contain("pessoa_observacao_id"));
            Assert.That(sql,Does.Not.Contain("INSERT identidade.identity_map").IgnoreCase);
            Assert.That(sql,Does.Not.Contain("UPDATE identidade.identity_map").IgnoreCase);
        }));
    }

    [Test]
    public void Causal_fingerprint_excludes_scores_and_model_ids_and_is_idempotent()
    {
        var sql=File.ReadAllText(Path.Combine(Root(),"Solution","database","migrations","20261003_SolutionSchema_371_Participantes_Divergencia.sql"));
        var proc=sql[(sql.IndexOf("CREATE OR ALTER PROCEDURE qualidade.sp_registrar_divergencia_causal_v1",StringComparison.Ordinal))..];
        Assert.Multiple((Action)(() => {
            Assert.That(proc,Does.Contain("conteudo_hash"));
            Assert.That(proc,Does.Contain("candidatos_canonicos"));
            Assert.That(proc,Does.Contain("DIVERGENCIA_CAUSAL_V1"));
            Assert.That(proc,Does.Contain("same causal state never reopens"));
            Assert.That(proc,Does.Not.Contain("score_melhor").IgnoreCase);
            Assert.That(proc,Does.Not.Contain("modelo_id").IgnoreCase);
        }));
    }

    [Test]
    public void Processor_runtime_publishes_identity_divergence_through_causal_v1_only()
    {
        var source=File.ReadAllText(Path.Combine(Root(),"Solution","src","Jornada.Processor.Worker","SqlProcessorRepository.Persistence.cs"));
        var methodStart=source.IndexOf("private static async Task RecordIdentityDivergenceAsync",StringComparison.Ordinal);
        var methodEnd=source.IndexOf("private static async Task RecordFactDivergenceAsync",methodStart,StringComparison.Ordinal);
        Assert.That(methodStart,Is.GreaterThanOrEqualTo(0));
        Assert.That(methodEnd,Is.GreaterThan(methodStart));
        var method=source[methodStart..methodEnd];
        Assert.Multiple((Action)(() => {
            Assert.That(method,Does.Contain("qualidade.sp_registrar_divergencia_causal_v1"));
            Assert.That(method,Does.Contain("CommandType.StoredProcedure"));
            Assert.That(method,Does.Contain("@candidatos_json"));
            Assert.That(method,Does.Not.Contain("INSERT qualidade.divergencia_gestor").IgnoreCase);
            Assert.That(method,Does.Not.Contain("IF NOT EXISTS").IgnoreCase);
            Assert.That(method,Does.Not.Contain("modelo_id").IgnoreCase);
            Assert.That(method,Does.Not.Contain("score_melhor").IgnoreCase);
        }));
    }

    [Test]
    public void Possible_presentation_is_append_only_minimal_and_threshold_free()
    {
        var sql=File.ReadAllText(Path.Combine(Root(),"Solution","database","migrations","20261003_SolutionSchema_371_Possivel_Apresentacao.sql"));
        Assert.Multiple((Action)(() => {
            Assert.That(sql,Does.Contain("apresentacao_id"));
            Assert.That(sql,Does.Contain("NENHUM_DESTES"));
            Assert.That(sql,Does.Contain("ordem BETWEEN 1 AND 5"));
            Assert.That(sql,Does.Contain("sp_selar_linkage_apresentacao_v1"));
            Assert.That(sql,Does.Contain("candidatos_fingerprint_sha256"));
            Assert.That(sql,Does.Contain("append-only"));
            Assert.That(sql,Does.Not.Contain("nome_mae").IgnoreCase);
            Assert.That(sql,Does.Not.Contain("data_nascimento").IgnoreCase);
            Assert.That(sql,Does.Not.Contain("limiar_inferior").IgnoreCase);
            Assert.That(sql,Does.Not.Contain("threshold").IgnoreCase);
        }));
    }

    [Test]
    public void Manifest_runs_371_delta_before_schema_consolidation()
    {
        var lines=File.ReadAllLines(Path.Combine(Root(),"Solution","database","migrations","manifest.txt"));
        var delta=Array.IndexOf(lines,"migrations/20261003_SolutionSchema_371_Participantes_Divergencia.sql");
        var possible=Array.IndexOf(lines,"migrations/20261003_SolutionSchema_371_Possivel_Apresentacao.sql");
        var consolidation=Array.IndexOf(lines,"migrations/20260910_Schema_Consolidation_370.sql");
        Assert.That(delta,Is.GreaterThanOrEqualTo(0));
        Assert.That(possible,Is.GreaterThan(delta));
        Assert.That(consolidation,Is.GreaterThan(possible));
    }
}
