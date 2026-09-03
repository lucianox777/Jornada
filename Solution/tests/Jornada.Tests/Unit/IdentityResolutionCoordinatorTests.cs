using Jornada.Contracts;
using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class IdentityResolutionCoordinatorTests
{
    [Test]
    public async Task Valid_cpf_uses_identity_map()
    {
        var expected = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var map = new FakeIdentityMap(new InternalIdentityResolution(
            ResolutionStatus.RESOLVIDO, expected, ResolutionMethod.CPF_DETERMINISTICO));
        var sut = new IdentityResolutionCoordinator(map);

        var observation = new IdentityObservation(
            "529.982.247-25", null, "Maria da Silva", new DateOnly(1982, 4, 10), "Ana de Souza");
        var result = await sut.ResolveAsync(observation, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
            Assert.That(result.PessoaUuid, Is.EqualTo(expected));
            Assert.That(result.MetodoResolucao, Is.EqualTo(ResolutionMethod.CPF_DETERMINISTICO));
            Assert.That(result.Score, Is.Null);
            Assert.That(result.ModeloId, Is.Null);
            Assert.That(map.LastCpf, Is.EqualTo("52998224725"));
            Assert.That(map.LastObservation, Is.EqualTo(observation));
        });
    }

    [Test]
    public async Task Shared_cpf_suspected_is_conflict_and_never_falls_back_to_probabilistic()
    {
        var map = new FakeIdentityMap(new InternalIdentityResolution(
            ResolutionStatus.CONFLITO, null, ResolutionMethod.CPF_DETERMINISTICO,
            Motivo: CpfIdentityConsistency.SharedCpfSuspectedReason));
        var sut = new IdentityResolutionCoordinator(map);

        var result = await sut.ResolveAsync(
            new IdentityObservation("52998224725", null, "Pedro Santos", new DateOnly(2017, 8, 21), "Maria da Silva"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ResolutionStatus.CONFLITO));
            Assert.That(result.PessoaUuid, Is.Null);
            Assert.That(result.MetodoResolucao, Is.EqualTo(ResolutionMethod.CPF_DETERMINISTICO));
            Assert.That(result.Motivo, Is.EqualTo("CPF_COMPARTILHADO_SUSPEITO"));
        });
    }

    [Test]
    public void Consistency_v1_blocks_low_name_plus_different_birth_date()
    {
        var existing = new IdentityCore("Maria Aparecida da Silva", new DateOnly(1975, 2, 10), "Joana Pereira");
        var incoming = new IdentityCore("Pedro Henrique Santos", new DateOnly(2017, 8, 21), "Maria Aparecida da Silva");

        var result = CpfIdentityConsistency.Evaluate(existing, incoming);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsConflict, Is.True);
            Assert.That(result.Motivo, Is.EqualTo("CPF_COMPARTILHADO_SUSPEITO"));
            Assert.That(result.Nome, Is.EqualTo(NameComparisonState.LOW));
            Assert.That(result.DataNascimentoIgual, Is.False);
        });
    }

    [Test]
    public void Consistency_v1_accepts_name_variation_when_birth_date_matches()
    {
        var existing = new IdentityCore("Maria Aparecida Souza", new DateOnly(1982, 4, 10), "Ana de Souza");
        var incoming = new IdentityCore("Maria Aparecida Souza Oliveira", new DateOnly(1982, 4, 10), "Ana de Souza");

        var result = CpfIdentityConsistency.Evaluate(existing, incoming);

        Assert.That(result.IsConflict, Is.False);
    }

    [Test]
    public void Consistency_v1_does_not_block_isolated_birth_date_difference_when_name_is_compatible()
    {
        var existing = new IdentityCore("Carlos Alberto Santos", new DateOnly(1990, 1, 15), "Lucia Santos");
        var incoming = new IdentityCore("Carlos Alberto Santos", new DateOnly(1990, 1, 16), "Lucia Santos");

        var result = CpfIdentityConsistency.Evaluate(existing, incoming);

        Assert.That(result.IsConflict, Is.False);
    }

    [Test]
    public async Task Missing_cpf_with_admitted_reason_stays_pending_until_on_demand_run()
    {
        var map = new FakeIdentityMap(new InternalIdentityResolution(
            ResolutionStatus.RESOLVIDO, Guid.NewGuid(), ResolutionMethod.CPF_DETERMINISTICO));
        var sut = new IdentityResolutionCoordinator(map);

        var result = await sut.ResolveAsync(
            new IdentityObservation(null, "SEM_CPF", "Carlos Santos", new DateOnly(1990, 1, 15), "Lucia Santos"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
            Assert.That(result.MetodoResolucao, Is.EqualTo(ResolutionMethod.PENDENTE_PROBABILISTICO));
            Assert.That(result.Score, Is.Null);
            Assert.That(result.ModeloId, Is.Null);
            Assert.That(result.Motivo, Is.EqualTo("AGUARDA_LINKAGE_SOB_DEMANDA"));
            Assert.That(map.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task Invalid_informed_cpf_is_conflict()
    {
        var map = new FakeIdentityMap(new InternalIdentityResolution(
            ResolutionStatus.RESOLVIDO, Guid.NewGuid(), ResolutionMethod.CPF_DETERMINISTICO));
        var sut = new IdentityResolutionCoordinator(map);

        var result = await sut.ResolveAsync(
            new IdentityObservation("11111111111", null, "Pessoa", new DateOnly(1980, 1, 1), "Mae"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ResolutionStatus.CONFLITO));
            Assert.That(result.Motivo, Is.EqualTo("CPF_INVALIDO"));
            Assert.That(map.Calls, Is.Zero);
        });
    }

    private sealed class FakeIdentityMap(InternalIdentityResolution result) : IIdentityMapRepository
    {
        public int Calls { get; private set; }
        public string? LastCpf { get; private set; }
        public IdentityObservation? LastObservation { get; private set; }

        public Task<InternalIdentityResolution> ResolveOrCreateByCpfAsync(
            string cpf,
            IdentityObservation observation,
            CancellationToken ct)
        {
            Calls++;
            LastCpf = cpf;
            LastObservation = observation;
            return Task.FromResult(result);
        }
    }
}
