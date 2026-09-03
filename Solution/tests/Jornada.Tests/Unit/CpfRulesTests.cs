using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class CpfRulesTests
{
    [TestCase("11144477735", "11144477735")]
    [TestCase("111.444.777-35", "11144477735")]
    public void Normalizes_valid_cpf(string input, string expected) =>
        Assert.That(CpfRules.NormalizeAndValidate(input), Is.EqualTo(expected));

    [TestCase("11111111111")]
    [TestCase("123")]
    [TestCase("")]
    public void Rejects_invalid_cpf(string input) =>
        Assert.That(CpfRules.NormalizeAndValidate(input), Is.Null);
}
