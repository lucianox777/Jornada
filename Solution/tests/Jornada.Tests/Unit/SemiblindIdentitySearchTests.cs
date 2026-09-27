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

    private sealed class FakePolicy(Guid? denied = null) : IPolicyEngine
    {
        public Task<bool> IsAllowedAsync(AccessContext context, string permission, string? resourceCode,
            Guid? pessoaUuid, CancellationToken ct) => Task.FromResult(pessoaUuid != denied);
        public Task<bool> ArePersonsAllowedAsync(AccessContext context, string permission, string? resourceCode,
            IReadOnlyCollection<Guid> pessoaUuids, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class FailingPolicy : IPolicyEngine
    {
        public Task<bool> IsAllowedAsync(AccessContext context, string permission, string? resourceCode,
            Guid? pessoaUuid, CancellationToken ct) =>
            throw new InvalidOperationException("Falha sintética na autorização");
        public Task<bool> ArePersonsAllowedAsync(AccessContext context, string permission, string? resourceCode,
            IReadOnlyCollection<Guid> pessoaUuids, CancellationToken ct) =>
            throw new InvalidOperationException("Falha sintética na autorização");
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
    public async Task Denied_candidate_is_not_returned()
    {
        var denied = Candidate(1);
        var allowed = Candidate(2);
        var service = new SemiblindIdentitySearchService(
            new FakeRetriever(denied, allowed), new FakePolicy(denied.PessoaUuid));
        var response = await service.SearchAsync(Context(),
            new SemiblindIdentitySearchRequest("Pessoa", new DateOnly(1980, 1, 1), null),
            Guid.NewGuid(), CancellationToken.None);
        Assert.That(response.Candidatos, Has.Count.EqualTo(1));
        Assert.That(response.Candidatos[0].Nome, Is.EqualTo(allowed.Nome));
    }

    [Test]
    public async Task All_candidates_denied_returns_no_personal_data()
    {
        var denied = Candidate(1);
        var service = new SemiblindIdentitySearchService(
            new FakeRetriever(denied), new FakePolicy(denied.PessoaUuid));
        var response = await service.SearchAsync(Context(),
            new SemiblindIdentitySearchRequest("Pessoa", new DateOnly(1980, 1, 1), null),
            Guid.NewGuid(), CancellationToken.None);
        Assert.That(response.Candidatos, Is.Empty);
        Assert.That(response.NenhumDestesDisponivel, Is.True);
    }

    [Test]
    public async Task Missing_birth_date_is_accepted_by_search_contract()
    {
        var service = new SemiblindIdentitySearchService(
            new FakeRetriever(Candidate(1)), new FakePolicy());
        var response = await service.SearchAsync(Context(),
            new SemiblindIdentitySearchRequest("Pessoa", null, null),
            Guid.NewGuid(), CancellationToken.None);
        Assert.That(response.Candidatos, Has.Count.EqualTo(1));
    }

    [Test]
    public void Policy_failure_never_returns_candidate_data()
    {
        var service = new SemiblindIdentitySearchService(
            new FakeRetriever(Candidate(1)), new FailingPolicy());
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.SearchAsync(Context(),
                new SemiblindIdentitySearchRequest("Pessoa", null, null),
                Guid.NewGuid(), CancellationToken.None));
    }

    [Test]
    public void Overlong_name_is_rejected_before_candidate_retrieval()
    {
        var service = new SemiblindIdentitySearchService(new FakeRetriever(), new FakePolicy());
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.SearchAsync(Context(),
                new SemiblindIdentitySearchRequest(new string('A', 201), null, null),
                Guid.NewGuid(), CancellationToken.None));
    }

    [Test]
    public void Overlong_mother_name_is_rejected_before_candidate_retrieval()
    {
        var service = new SemiblindIdentitySearchService(new FakeRetriever(), new FakePolicy());
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.SearchAsync(Context(),
                new SemiblindIdentitySearchRequest("Pessoa", null, new string('A', 201)),
                Guid.NewGuid(), CancellationToken.None));
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
