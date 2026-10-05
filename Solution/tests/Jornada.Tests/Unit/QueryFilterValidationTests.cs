using Jornada.Api;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class QueryFilterValidationTests
{
    [TestCase("AR01")]
    [TestCase("a1B2")]
    [TestCase("0000")]
    public void Four_ascii_alphanumeric_characters_are_valid(string codigo) =>
        Assert.That(QueryFilterValidation.IsValidCode(codigo), Is.True);

    [TestCase("ABC")]
    [TestCase("ABCDE")]
    [TestCase("AB_1")]
    [TestCase("ÁR01")]
    [TestCase("AR 1")]
    public void Code_validation_preserves_ascii_contract(string codigo) =>
        Assert.That(QueryFilterValidation.IsValidCode(codigo), Is.False);
}
