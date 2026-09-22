using Jornada.Contracts;

namespace Jornada.Tests;

public sealed class BrazilianNameComponentsTests
{
    [Test]
    public void Project_NeverErasesAgnomeOrTitleFromFullName()
    {
        var father = BrazilianNameComponents.Project("João da Silva")!;
        var son = BrazilianNameComponents.Project("João da Silva Filho")!;
        var grandson = BrazilianNameComponents.Project("João da Silva Neto")!;
        var junior = BrazilianNameComponents.Project("João da Silva Jr.")!;

        Assert.Multiple(() =>
        {
            Assert.That(father.NormalizedFull, Is.EqualTo("JOAO DA SILVA"));
            Assert.That(son.NormalizedFull, Is.EqualTo("JOAO DA SILVA FILHO"));
            Assert.That(grandson.NormalizedFull, Is.EqualTo("JOAO DA SILVA NETO"));
            Assert.That(junior.NormalizedFull, Is.EqualTo("JOAO DA SILVA JR."));
            Assert.That(father.Agnome, Is.Null);
            Assert.That(son.Agnome, Is.EqualTo("FILHO"));
            Assert.That(grandson.Agnome, Is.EqualTo("NETO"));
            Assert.That(junior.Agnome, Is.EqualTo("JUNIOR"));
            Assert.That(son.LastContentSurname, Is.EqualTo("SILVA"));
            Assert.That(father.LastContentSurname, Is.EqualTo("SILVA"));
            Assert.That(son.NormalizedFull, Is.Not.EqualTo(father.NormalizedFull));
            Assert.That(son.NormalizedFull, Is.Not.EqualTo(grandson.NormalizedFull));
            Assert.That(IdentityComparison.CompareName(father.NormalizedFull, son.NormalizedFull),
                Is.Not.EqualTo(NameComparisonState.EXACT));
        });
    }

    [TestCase("Dra. Ana Maria de Souza Neto", "DRA", "NETO", "SOUZA")]
    [TestCase("Dr. José de la Cruz Jr.", "DR", "JUNIOR", "CRUZ")]
    [TestCase("Maria dos Anjos", null, null, "ANJOS")]
    [TestCase("José di Napoli Filho", null, "FILHO", "NAPOLI")]
    [TestCase("João Silva FL.", null, "FILHO", "SILVA")]
    [TestCase("Ana de los Santos Bisneta", null, "BISNETA", "SANTOS")]
    public void Project_ClassifiesExactTokensWithoutDestructiveNormalization(
        string input, string? title, string? agnome, string? surname)
    {
        var projection = BrazilianNameComponents.Project(input)!;
        Assert.Multiple(() =>
        {
            Assert.That(projection.TitlePrefix, Is.EqualTo(title));
            Assert.That(projection.Agnome, Is.EqualTo(agnome));
            Assert.That(projection.LastContentSurname, Is.EqualTo(surname));
            Assert.That(projection.NormalizedFull,
                Is.EqualTo(IdentityComparison.NormalizeText(input)));
        });
    }

    [Test]
    public void Project_DoesNotAssumeAmbiguousTerminalTokenIsAnAgnome()
    {
        var ambiguous = BrazilianNameComponents.Project("João Filho")!;
        var oneToken = BrazilianNameComponents.Project("Neto")!;
        Assert.Multiple(() =>
        {
            Assert.That(ambiguous.Agnome, Is.Null);
            Assert.That(ambiguous.LastContentSurname, Is.EqualTo("FILHO"));
            Assert.That(oneToken.Agnome, Is.Null);
            Assert.That(oneToken.LastContentSurname, Is.Null);
            Assert.That(BrazilianNameComponents.Project(null), Is.Null);
            Assert.That(BrazilianNameComponents.Project("   "), Is.Null);
        });
    }

    [Test]
    public void Project_ReportsRepeatedParticleWithoutChangingName()
    {
        var result = BrazilianNameComponents.Project("Maria de de Souza")!;
        Assert.Multiple(() =>
        {
            Assert.That(result.NormalizedFull, Is.EqualTo("MARIA DE DE SOUZA"));
            Assert.That(result.LastContentSurname, Is.EqualTo("SOUZA"));
            Assert.That(result.RepeatedParticle, Is.True);
            Assert.That(BrazilianNameComponents.Project("Maria de Souza")!.RepeatedParticle, Is.False);
        });
    }

    [Test]
    public void Project_ExactTokenBoundaryProtectsUnrelatedSurnames()
    {
        var result = BrazilianNameComponents.Project("Ana Silva Filhote")!;
        Assert.Multiple(() =>
        {
            Assert.That(result.Agnome, Is.Null);
            Assert.That(result.LastContentSurname, Is.EqualTo("FILHOTE"));
        });
    }
}
