using Jornada.Contracts;

namespace Jornada.Tests;

public sealed class PersonNameBasicNormalizationTests
{
    [Test]
    public void Project_SeparatesUpperAccentRemovalAndParticleRemoval()
    {
        var projection = PersonNameBasicNormalization.Project("  João   da  Sílva  ");

        Assert.That(projection, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(projection!.Upper, Is.EqualTo("JOÃO DA SÍLVA"));
            Assert.That(projection.UpperNoDiacritics, Is.EqualTo("JOAO DA SILVA"));
            Assert.That(projection.WithoutPortugueseParticles, Is.EqualTo("JOAO SILVA"));
        });
    }

    [Test]
    public void Project_RemovesOnlyDeclaredPortugueseParticles()
    {
        var projection = PersonNameBasicNormalization.Project("Maria de Fátima dos Reis D'Ávila");

        Assert.That(projection, Is.Not.Null);
        Assert.That(projection!.WithoutPortugueseParticles, Is.EqualTo("MARIA FATIMA REIS D'AVILA"));
    }

    [Test]
    public void Project_DoesNotTurnNonEmptyNameIntoEmptyKey()
    {
        var projection = PersonNameBasicNormalization.Project("de");

        Assert.That(projection, Is.Not.Null);
        Assert.That(projection!.WithoutPortugueseParticles, Is.EqualTo("DE"));
    }

    [Test]
    public void Project_BlankInputProducesNoProjection()
    {
        Assert.That(PersonNameBasicNormalization.Project("   \t\r\n"), Is.Null);
    }

    [Test]
    public void MethodVersion_IsFrozenForTheOutputContract()
    {
        Assert.That(PersonNameBasicNormalization.MethodVersion, Is.EqualTo("PERSON_NAME_BASIC_PTBR_V1"));
    }
}
