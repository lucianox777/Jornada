using Jornada.Api;
using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class SemiblindIdentitySearchTests
{
    private sealed class FakeRetriever(params SemiblindInternalCandidate[] candidates) : ISemiblindCandidateRetriever
    {
        public Task<IReadOnlyList<SemiblindInternalCandidate>> RetrieveAsync(
            SemiblindIdentitySearchRequest request, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SemiblindInternalCandidate>>(candidates);
    }

    private sealed class FakePolicy : IPolicyEngine
    {
        public Task<bool> IsAllowedAsync(AccessContext context, string permission, string? resourceCode,
            Guid? pessoaUuid, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> ArePersonsAllowedAsync(AccessContext context, string permission, string? resourceCode,
            IReadOnlyCollection<Guid> pessoaUuids, CancellationToken ct) => Task.FromResult(false);
    }

    private static AccessContext Context() => new(
        Guid.NewGuid(), AccessCredentialType.GESTOR, "SMADS", "SMADS", null,
        ["jornada.identidade.busca.read"], []);

    private static SemiblindInternalCandidate Candidate(int i) =>
        new(Guid.NewGuid(), $"Pessoa {i}", new DateOnly(1980, 1, 1), "Mãe");

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(5)]
    [TestCase(8)]
    public async Task Search_exposes_at_most_five_minimized_candidates(int count)
    {
        var service = new SemiblindIdentitySearchService(
            new FakeRetriever(Enumerable.Range(0, count).Select(Candidate).ToArray()), new FakePolicy());
        var response = await service.SearchAsync(Context(),
            new SemiblindIdentitySearchRequest("Pessoa", new DateOnly(1980, 1, 1), null),
            Guid.NewGuid(), CancellationToken.None);

        Assert.That(response.Candidatos, Has.Count.EqualTo(Math.Min(5, count)));
        Assert.That(response.NenhumDestesDisponivel, Is.True);
        Assert.That(response.Candidatos.Select(x => x.OpcaoId).Distinct().Count(),
            Is.EqualTo(response.Candidatos.Count));
        var json = System.Text.Json.JsonSerializer.Serialize(response).ToLowerInvariant();
        Assert.That(json, Does.Not.Contain("posterior"));
        Assert.That(json, Does.Not.Contain("score"));
        Assert.That(json, Does.Not.Contain("pessoauuid"));
        Assert.That(json, Does.Not.Contain("cpf"));
    }

    [Test]
    public void Empty_name_is_rejected()
    {
        var service = new SemiblindIdentitySearchService(new FakeRetriever(), new FakePolicy());
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.SearchAsync(Context(),
                new SemiblindIdentitySearchRequest(" ", null, null),
                Guid.NewGuid(), CancellationToken.None));
    }
}
