using Jornada.Access.Security;
using Jornada.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Text.Json;

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

    [Test]
    public async Task Scope_policies_enforce_credential_type_and_scopes_independently_of_endpoint_code()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJornadaAccessSecurity();
        using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("N"))],
            JornadaAccessSecurity.Scheme));

        async Task<bool> Allowed(AccessCredentialType type, string policy, string[] scopes)
        {
            var http = new DefaultHttpContext();
            http.Items[JornadaAccessSecurity.AccessContextItem] = new AccessContext(
                Guid.NewGuid(), type, "AR01", "SMADS",
                type == AccessCredentialType.GESTOR ? null : "AR01", scopes, ["AR01"]);
            return (await authorization.AuthorizeAsync(principal, http, policy)).Succeeded;
        }

        Assert.That(await Allowed(AccessCredentialType.GESTOR, "jornada.monitor.read", ["jornada.monitor.read"]), Is.True);
        Assert.That(await Allowed(AccessCredentialType.BENEFICIO, "jornada.monitor.read", ["jornada.monitor.read"]), Is.False,
            "Credencial de tipo nunca recebe escopo de Gestor, mesmo se configurado por engano.");
        Assert.That(await Allowed(AccessCredentialType.BENEFICIO, "jornada.pessoas.read", ["jornada.pessoas.read"]), Is.True);
        Assert.That(await Allowed(AccessCredentialType.SERVICO, "jornada.identidade.resolve", ["jornada.identidade.resolve"]), Is.True);
        Assert.That(await Allowed(AccessCredentialType.GESTOR, "jornada.pessoas.read", []), Is.False);
    }

    [Test]
    public async Task Configured_policies_match_every_matrix_scope_and_type()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "config", "security", "authorization-matrix.json");
        using var matrix = JsonDocument.Parse(File.ReadAllText(path));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJornadaAccessSecurity();
        using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        foreach (var route in matrix.RootElement.GetProperty("routes").EnumerateArray())
        {
            var permission = route.GetProperty("permission").GetString()!;
            var policy = await policyProvider.GetPolicyAsync(permission);
            Assert.That(policy, Is.Not.Null, "Policy ausente para " + permission);
            Assert.That(policy!.AuthenticationSchemes, Does.Contain(JornadaAccessSecurity.Scheme));
            var scope = policy.Requirements.OfType<JornadaScopeRequirement>().Single();
            var allowType = route.GetProperty("allowedCredentialTypes").EnumerateArray()
                .Any(v => v.GetString() is "BENEFICIO" or "SERVICO");
            Assert.Multiple(() =>
            {
                Assert.That(scope.Permission, Is.EqualTo(permission));
                Assert.That(scope.AllowType, Is.EqualTo(allowType), "Tipo divergente da matriz em " + permission);
            });
        }
    }
}
