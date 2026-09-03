using Jornada.Api;
using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class AccessPolicyTests
{
    [Test]
    public async Task Gestor_does_not_require_prior_person_relationship()
    {
        var policy = new MunicipalAccessPolicyEngine();
        var context = GestorContext(["jornada.pessoas.read"]);
        Assert.That(await policy.IsAllowedAsync(context, "jornada.pessoas.read", null, Guid.NewGuid(), CancellationToken.None), Is.True);
    }

    [Test]
    public async Task Type_credential_also_does_not_require_prior_person_relationship()
    {
        var policy = new MunicipalAccessPolicyEngine();
        var context = new AccessContext(Guid.NewGuid(), AccessCredentialType.BENEFICIO, "AA01", "SEHAB", "AA01",
            ["jornada.pessoas.read"], ["AA01"]);
        Assert.That(await policy.IsAllowedAsync(context, "jornada.pessoas.read", "AA01", Guid.NewGuid(), CancellationToken.None), Is.True);
    }

    [Test]
    public async Task Type_credential_remains_restricted_to_its_resource_code()
    {
        var policy = new MunicipalAccessPolicyEngine();
        var context = new AccessContext(Guid.NewGuid(), AccessCredentialType.BENEFICIO, "AA01", "SEHAB", "AA01",
            ["jornada.pessoas.read"], ["AA01"]);
        Assert.That(await policy.IsAllowedAsync(context, "jornada.pessoas.read", "AR01", Guid.NewGuid(), CancellationToken.None), Is.False);
    }

    [Test]
    public async Task Batch_authorization_is_not_conditioned_by_declared_purpose()
    {
        var policy = new MunicipalAccessPolicyEngine();
        var context = GestorContext(["jornada.pessoas.read"]);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        Assert.That(await policy.ArePersonsAllowedAsync(context, "jornada.pessoas.read", null, ids, CancellationToken.None), Is.True);
    }

    [Test]
    public async Task Missing_scope_still_denies_access()
    {
        var policy = new MunicipalAccessPolicyEngine();
        var context = GestorContext([]);
        Assert.That(await policy.IsAllowedAsync(context, "jornada.registros.read", null, null, CancellationToken.None), Is.False);
    }

    private static AccessContext GestorContext(string[] scopes) => new(
        Guid.NewGuid(), AccessCredentialType.GESTOR, "SMADS", "SMADS", null, scopes, []);
}
