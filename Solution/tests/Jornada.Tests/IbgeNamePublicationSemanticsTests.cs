using Jornada.Contracts;

namespace Jornada.Tests;

public sealed class IbgeNamePublicationSemanticsTests
{
    [Test]
    public void ProjectFirstName_UsesOnlyTheFirstPublishedNameComponent()
    {
        var projection = IbgeNamePublicationSemantics.ProjectFirstName("Maria Clara da Silva");

        Assert.That(projection, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(projection!.FirstName, Is.EqualTo("Maria"));
            Assert.That(projection.FirstNameNormalized, Is.EqualTo("MARIA"));
        });
    }

    [Test]
    public void ProjectFirstName_NormalizesDiacriticsWithTheLinkageCanonicalRule()
    {
        var projection = IbgeNamePublicationSemantics.ProjectFirstName("  João   Pedro dos Santos  ");

        Assert.That(projection, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(projection!.FirstName, Is.EqualTo("João"));
            Assert.That(projection.FirstNameNormalized, Is.EqualTo("JOAO"));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   \t\r\n")]
    public void ProjectFirstName_BlankInputProducesNoProjection(string? value)
    {
        Assert.That(IbgeNamePublicationSemantics.ProjectFirstName(value), Is.Null);
    }

    [Test]
    public void Contract_DoesNotExposeSurnameInferenceFromFullName()
    {
        var properties = typeof(IbgePublishedNameProjection).GetProperties()
            .Select(static property => property.Name)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(properties, Does.Contain(nameof(IbgePublishedNameProjection.FirstName)));
            Assert.That(properties, Does.Contain(nameof(IbgePublishedNameProjection.FirstNameNormalized)));
            Assert.That(properties.Any(static name => name.Contains("Surname", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(properties.Any(static name => name.Contains("Sobrenome", StringComparison.OrdinalIgnoreCase)), Is.False);
        });
    }

    [Test]
    public void MethodVersion_IsFrozenForReplayAndAudit()
    {
        Assert.That(
            IbgeNamePublicationSemantics.MethodVersion,
            Is.EqualTo("IBGE_CENSO_2022_NOMES_PUBLICACAO_V1"));
    }
}
