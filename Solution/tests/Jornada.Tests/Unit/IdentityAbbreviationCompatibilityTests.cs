using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class IdentityAbbreviationCompatibilityTests
{
    [TestCase("Maria S. Silva", "Maria Souza Silva")]
    [TestCase("Maria S Silva", "Maria Souza Silva")]
    [TestCase("M. C. Souza", "Maria Clara Souza")]
    [TestCase("Maria Souza da Silva", "Maria S. Silva")]
    public void Compatible_initial_abbreviations_are_detected(string left, string right)
    {
        Assert.Multiple(() =>
        {
            Assert.That(IdentityComparison.IsAbbreviationCompatible(left, right), Is.True);
            Assert.That(IdentityComparison.IsAbbreviationCompatible(right, left), Is.True);
        });
    }

    [TestCase("Maria Souza Silva", "Maria Souza Silva")]
    [TestCase("Maria S. Silva", "Maria Santos Pereira")]
    [TestCase("Maria S. Silva", "Maria T. Silva")]
    [TestCase("Maria Silva", "Maria Souza Silva")]
    [TestCase("Pessoa Distinta Validacao 000001", "Pessoa Teste 0000000101")]
    [TestCase("Ana M. Souza", "Ana Maria Santos")]
    public void Conflicting_or_non_abbreviation_pairs_are_rejected(string left, string right)
    {
        Assert.That(IdentityComparison.IsAbbreviationCompatible(left, right), Is.False);
    }

    [Test]
    public void Diagnostic_has_explicit_version_and_does_not_change_legacy_states()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                IdentityComparison.AbbreviationCompatibilityVersionV1,
                Is.EqualTo("PTBR_POSITIONAL_INITIAL_COMPATIBLE_V1"));
            Assert.That(
                IdentityComparison.CompareName("Maria S. Silva", "Maria Souza Silva"),
                Is.EqualTo(NameComparisonState.MEDIUM));
            Assert.That(
                IdentityComparison.IsAbbreviationCompatible("Maria S. Silva", "Maria Souza Silva"),
                Is.True);
        });
    }
}
