using Jornada.Contracts;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class NisRulesTests
{
    [TestCase("12000000004", "12000000004")]
    [TestCase("120.00000.00-4", "12000000004")]
    [TestCase("27182818286", "27182818286")]
    public void Valid_synthetic_numbers_are_normalized(string input, string expected)
        => Assert.That(NisRules.NormalizeAndValidate(input), Is.EqualTo(expected));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("11111111111")]
    [TestCase("12000000005")]
    [TestCase("123")]
    public void Invalid_numbers_are_rejected(string? input)
        => Assert.That(NisRules.NormalizeAndValidate(input), Is.Null);
}
