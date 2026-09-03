using System.Text.Json;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class AuthorizationMatrixContractTests
{
    [Test]
    public void Development_credentials_do_not_grant_privileged_identity_scopes_to_type_credentials()
    {
        using var matrix = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "config", "security", "authorization-matrix.json")));
        using var keys = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "config", "security", "test-access-keys.json")));
        var privileged = matrix.RootElement.GetProperty("permissions").EnumerateArray()
            .Select(x => x.GetProperty("permission").GetString()!)
            .Where(x => x is "jornada.identidade.conflitos.read" or "jornada.identidade.corrigir" or "jornada.ingestao.write" or "jornada.ingestao.status")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var credential in keys.RootElement.GetProperty("credentials").EnumerateArray())
        {
            var type = credential.GetProperty("type").GetString();
            if (string.Equals(type, "GESTOR", StringComparison.OrdinalIgnoreCase)) continue;
            var scopes = credential.GetProperty("scopes").EnumerateArray().Select(x => x.GetString()!).ToArray();
            Assert.That(scopes.Any(privileged.Contains), Is.False,
                $"Credencial de tipo {type}:{credential.GetProperty("publicCode").GetString()} recebeu scope privilegiado.");
        }
    }

    [Test]
    public void Route_matrix_is_unique_and_type_credentials_are_limited_to_explicit_routes()
    {
        using var matrix = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "config", "security", "authorization-matrix.json")));
        var routes = matrix.RootElement.GetProperty("routes").EnumerateArray().ToArray();
        var keys = routes.Select(x => x.GetProperty("method").GetString() + " " + x.GetProperty("path").GetString()).ToArray();
        Assert.That(keys.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(keys.Length));
        var typeRoutes = routes.Where(x => x.GetProperty("allowedCredentialTypes").EnumerateArray()
            .Any(v => v.GetString() is "BENEFICIO" or "SERVICO"))
            .Select(x => x.GetProperty("path").GetString()).ToHashSet();
        Assert.That(typeRoutes, Is.EquivalentTo(new[]
        {
            "/api/v1/identidade/resolver",
            "/api/v1/pessoas/{pessoaUuid}",
            "/api/v1/pessoas/consulta"
        }));
    }
}
