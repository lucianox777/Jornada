using Jornada.Api;
using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class ProgressiveOriginBaseQueryTests
{
    [Test]
    public void Base_code_is_optional_but_when_present_uses_governed_code_domain()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProgressiveOriginApi.TryValidateRequest(
                new ProgressiveOriginQueryRequest("ASSISTENCIA", "P1")), Is.True);
            Assert.That(ProgressiveOriginApi.TryValidateRequest(
                new ProgressiveOriginQueryRequest("ASSISTENCIA", "P1", "CADASTRO_SMADS")), Is.True);
            Assert.That(ProgressiveOriginApi.TryValidateRequest(
                new ProgressiveOriginQueryRequest("ASSISTENCIA", "P1", "cadastro smads")), Is.False);
            Assert.That(ProgressiveOriginApi.TryValidateRequest(
                new ProgressiveOriginQueryRequest("ASSISTENCIA", "P1", new string('A', 121))), Is.False);
        });
    }

    [Test]
    public void Base_aware_sql_authorizes_system_and_keys_person_by_base_plus_code()
    {
        var source = File.ReadAllText(SourcePath());

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("JOIN ref.sistema_origem_base_pessoa sb"));
            Assert.That(source, Does.Contain("sb.sistema_origem_id=s.sistema_origem_id AND sb.ativo=1"));
            Assert.That(source, Does.Contain("po.base_pessoa_origem_id=b.base_pessoa_origem_id"));
            Assert.That(source, Does.Contain("b.codigo COLLATE Latin1_General_100_BIN2=@base"));
            Assert.That(source, Does.Contain("po.codigo_pessoa_origem COLLATE Latin1_General_100_BIN2=@codigo"));
            Assert.That(source, Does.Not.Contain("po.sistema_origem_id=s.sistema_origem_id"));
        });
    }

    [Test]
    public void Response_can_expose_effective_base_without_breaking_legacy_shape()
    {
        var response = new ProgressiveOriginQueryResponse(
            "ASSISTENCIA",
            "P1",
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1"),
            null,
            ProgressiveIdentityStatus.PROVISORIA,
            0,
            DateTimeOffset.Parse("2026-09-13T12:00:00Z"),
            DateTimeOffset.Parse("2026-09-13T12:00:00Z"),
            null,
            "CADASTRO_SMADS");

        Assert.That(response.CodigoBasePessoaOrigem, Is.EqualTo("CADASTRO_SMADS"));
    }

    private static string SourcePath()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return Path.Combine(root, "src", "Jornada.Api", "ProgressiveOriginApi.cs");
    }
}
