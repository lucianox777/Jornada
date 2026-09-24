using Jornada.Contracts;
using Jornada.Linkage.SyntheticCorpus;

namespace Jornada.Tests.Unit;

/// <summary>
/// CPF exclusivamente sintético e reproduzível para testes cujo número exato
/// não faz parte do contrato. Vetores conhecidos e fixtures cruzadas permanecem fixos.
/// </summary>
internal static class DeterministicCpfFixture
{
    public static string Valid(ulong seed) =>
        SyntheticCorpusV2Rules.GenerateCpf(new Xoshiro256StarStar(seed));
}

[TestFixture]
[Category("Unit")]
public sealed class DeterministicCpfFixtureTests
{
    [Test]
    public void Valid_generates_reproducible_structurally_valid_cpf_for_each_seed()
    {
        var first = DeterministicCpfFixture.Valid(101);
        Assert.Multiple(() =>
        {
            Assert.That(first, Has.Length.EqualTo(11));
            Assert.That(CpfRules.NormalizeAndValidate(first), Is.EqualTo(first));
            Assert.That(DeterministicCpfFixture.Valid(101), Is.EqualTo(first));
            Assert.That(DeterministicCpfFixture.Valid(202), Is.Not.EqualTo(first));
            Assert.That(CpfRules.NormalizeAndValidate(DeterministicCpfFixture.Valid(202)),
                Is.EqualTo(DeterministicCpfFixture.Valid(202)));
        });
    }
}
