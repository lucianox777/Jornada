using Jornada.Access.Security;
using Jornada.Contracts;
using Microsoft.AspNetCore.Http;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class JornadaAccessHeaderTests
{
    [Test]
    public void Missing_or_duplicated_access_keys_fail_as_401()
    {
        var missing = new HeaderDictionary { ["X-Jornada-Gestor"] = "SMADS" };
        Assert.That(JornadaCredentialHeaderParser.Parse(missing).RejectStatus, Is.EqualTo(401));

        var duplicated = new HeaderDictionary
        {
            ["X-Jornada-Access-Key"] = new Microsoft.Extensions.Primitives.StringValues(["one", "two"]),
            ["X-Jornada-Gestor"] = "SMADS"
        };
        Assert.That(JornadaCredentialHeaderParser.Parse(duplicated).RejectStatus, Is.EqualTo(401));
    }

    [Test]
    public void Ambiguous_or_missing_credential_type_fails_as_400()
    {
        var headers = new HeaderDictionary { ["X-Jornada-Access-Key"] = "synthetic-not-a-real-key" };
        Assert.That(JornadaCredentialHeaderParser.Parse(headers).RejectStatus, Is.EqualTo(400));
        headers["X-Jornada-Gestor"] = "SMADS";
        headers["X-Jornada-Beneficio"] = "AR01";
        Assert.That(JornadaCredentialHeaderParser.Parse(headers).RejectStatus, Is.EqualTo(400));
    }

    [TestCase("X-Jornada-Gestor", AccessCredentialType.GESTOR)]
    [TestCase("X-Jornada-Beneficio", AccessCredentialType.BENEFICIO)]
    [TestCase("X-Jornada-Servico", AccessCredentialType.SERVICO)]
    public void Exactly_one_credential_header_selects_its_original_type(string name, AccessCredentialType expected)
    {
        var headers = new HeaderDictionary
        {
            ["X-Jornada-Access-Key"] = "synthetic-not-a-real-key",
            [name] = "AR01"
        };
        var parsed = JornadaCredentialHeaderParser.Parse(headers);
        Assert.Multiple(() =>
        {
            Assert.That(parsed.RejectStatus, Is.Null);
            Assert.That(parsed.Credential?.Type, Is.EqualTo(expected));
            Assert.That(parsed.Credential?.PublicCode, Is.EqualTo("AR01"));
        });
    }
}
