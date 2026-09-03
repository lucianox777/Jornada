using System.Text.Json;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class DevelopmentCredentialFixtureTests
{
    private static readonly IReadOnlyDictionary<string, Guid> ExpectedIds = new Dictionary<string, Guid>(StringComparer.Ordinal)
    {
        ["SEHAB"] = Guid.Parse("11111111-1111-4111-8111-111111111111"),
        ["SMADS"] = Guid.Parse("11111111-1111-4111-8111-111111111112"),
        ["AA01"] = Guid.Parse("11111111-1111-4111-8111-111111111113"),
        ["CRA1"] = Guid.Parse("11111111-1111-4111-8111-111111111114"),
        ["SMS"] = Guid.Parse("11111111-1111-4111-8111-111111111115"),
        ["SMDET"] = Guid.Parse("11111111-1111-4111-8111-111111111116"),
        ["AR01"] = Guid.Parse("11111111-1111-4111-8111-111111111117"),
        ["POT1"] = Guid.Parse("11111111-1111-4111-8111-111111111118"),
        ["CPO1"] = Guid.Parse("11111111-1111-4111-8111-111111111119")
    };

    [Test]
    public void Fixture_credential_ids_are_deterministic_and_match_the_seed_contract()
    {
        var path = FindSolutionFile(Path.Combine("config", "security", "test-access-keys.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var credentials = document.RootElement.GetProperty("credentials").EnumerateArray().ToArray();

        Assert.That(credentials, Has.Length.EqualTo(ExpectedIds.Count));

        foreach (var credential in credentials)
        {
            var code = credential.GetProperty("publicCode").GetString();
            var credentialId = credential.GetProperty("credentialId").GetGuid();
            Assert.That(code, Is.Not.Null);
            Assert.That(ExpectedIds.ContainsKey(code!), Is.True);
            Assert.That(credentialId, Is.EqualTo(ExpectedIds[code!]));
        }
    }

    [Test]
    public void Fixture_does_not_carry_declared_purpose_authorization()
    {
        var path = FindSolutionFile(Path.Combine("config", "security", "test-access-keys.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        foreach (var credential in document.RootElement.GetProperty("credentials").EnumerateArray())
        {
            Assert.That(credential.TryGetProperty("authorizedPurposeCodes", out _), Is.False);
            var scopes = credential.GetProperty("scopes").EnumerateArray().Select(x => x.GetString()).ToArray();
            Assert.That(scopes, Does.Not.Contain("jornada.primeiro_atendimento"));
        }
    }

    private static string FindSolutionFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            current = current.Parent;
        }

        Assert.Fail($"Arquivo da Solution não encontrado: {relativePath}");
        return string.Empty;
    }
}
