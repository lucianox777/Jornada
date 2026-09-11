using Jornada.Contracts;

namespace Jornada.Tests;

[TestFixture]
public sealed class MetaphoneBrTests
{
    [TestCase("João Silva", "JOAO SILVA")]
    [TestCase("Marya", "MARIA")]
    [TestCase("Helena", "ELENA")]
    [TestCase("Elena", "ELENA")]
    [TestCase("Philippe", "FILIPE")]
    [TestCase("Filipe", "FILIPE")]
    [TestCase("Chavier", "XAVIER")]
    [TestCase("Xavier", "XAVIER")]
    [TestCase("Luiz", "LUIS")]
    [TestCase("Luis", "LUIS")]
    [TestCase("Joaquim", "JOAKIM")]
    [TestCase("Joaquin", "JOAKIM")]
    public void Encode_MatchesFrozenBrazilianPhoneticVectors(string input, string expected)
    {
        Assert.That(MetaphoneBr.Encode(input), Is.EqualTo(expected));
    }

    [TestCase("MARYA", "MARIA")]
    [TestCase("CHAVIER", "XAVIER")]
    [TestCase("HELENA", "ELENA")]
    [TestCase("PHILIPE", "FILIPE")]
    [TestCase("CALHEIROS", "KA1EIROS")]
    [TestCase("FILHA MANHA CHICO SCHMIDT SCENA ESCOVA QUILO", "FI1A MA3A XIKO SXMIDT SENA ESKOVA KILO")]
    public void Encode_MatchesExactUpstreamMetaphoneBr005ConformanceVectors(string input, string expected)
    {
        // Vetores publicados no próprio testthat do upstream congelado:
        // ipeadata-lab/metaphonebr@17fdee95581442cdcc98fddc30aea3079caf27ae.
        Assert.That(MetaphoneBr.Encode(input), Is.EqualTo(expected));
    }

    [Test]
    public void Encode_IsDeterministicAndDoesNotDependOnDiacritics()
    {
        var accented = MetaphoneBr.Encode("João Gonçalves");
        var ascii = MetaphoneBr.Encode("Joao Goncalves");

        Assert.Multiple(() =>
        {
            Assert.That(accented, Is.Not.Null.And.Not.Empty);
            Assert.That(ascii, Is.EqualTo(accented));
            Assert.That(MetaphoneBr.Encode("João Gonçalves"), Is.EqualTo(accented));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Encode_EmptyInputProducesNoProjection(string? input)
    {
        Assert.That(MetaphoneBr.Encode(input), Is.Null);
    }
}
