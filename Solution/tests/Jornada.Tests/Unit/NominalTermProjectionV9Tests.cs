using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class NominalTermProjectionV9Tests
{
    [Test]
    public void Projects_first_name_and_last_content_surname_with_versioned_semantics()
    {
        var tokens = NominalTermProjectionV9.Project("Dra. Maria Aparecida de Oliveira Santos");
        Assert.That(tokens, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(tokens!.FirstGivenName, Is.EqualTo("MARIA"));
            Assert.That(tokens.LastContentSurname, Is.EqualTo("SANTOS"));
            Assert.That(tokens.TitlePrefix, Is.EqualTo("DRA"));
            Assert.That(tokens.ProjectionVersion, Is.EqualTo(NominalTermProjectionV9.ProjectionVersion));
            Assert.That(tokens.ComponentsVersion, Is.EqualTo(BrazilianNameComponents.MethodVersion));
        });
    }

    [Test]
    public void Preserves_full_name_and_does_not_infer_a_surname_from_single_given_name()
    {
        var tokens = NominalTermProjectionV9.Project("Maria");
        Assert.That(tokens, Is.Not.Null);
        Assert.That(tokens!.NormalizedFull, Is.EqualTo("MARIA"));
        Assert.That(tokens.FirstGivenName, Is.EqualTo("MARIA"));
        Assert.That(tokens.LastContentSurname, Is.Null);
    }

    [Test]
    public void Skips_title_but_preserves_terminal_agnome_for_full_name()
    {
        var tokens = NominalTermProjectionV9.Project("Dr. Joao da Silva Filho");
        Assert.That(tokens, Is.Not.Null);
        Assert.That(tokens!.FirstGivenName, Is.EqualTo("JOAO"));
        Assert.That(tokens.LastContentSurname, Is.EqualTo("SILVA"));
        Assert.That(tokens.NormalizedFull, Does.Contain("FILHO"));
    }

    [Test]
    public void Missing_names_have_no_terms_and_no_imputed_frequencies()
    {
        Assert.That(NominalTermProjectionV9.Project(null), Is.Null);
        Assert.That(NominalTermProjectionV9.Project("  "), Is.Null);
    }
}
