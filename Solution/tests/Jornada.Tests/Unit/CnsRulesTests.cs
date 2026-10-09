using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class CnsRulesTests
{
    // Apenas vetores sintéticos de aritmética; não representam cadastro SUS/CADSUS.
    [TestCase("211111111110007")] // Prefixo 2, base PIS, sufixo 000
    [TestCase("111111111110000")] // Prefixo 1
    [TestCase("111111111130001")] // Caso especial DV 10: sufixo 001
    [TestCase("700000000000005")] // CNS 7: soma ponderada módulo 11
    [TestCase("800000000000001")] // CNS 8
    [TestCase("900000000000008")] // CNS 9
    public void Synthetic_structurally_valid_cns_passes_checksum(string value)
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.That(CnsRules.IsValid(value), Is.True);
            Assert.DoesNotThrow(() => CnsRules.ValidateOptionalSmsCns(value));
        }));
    }

    [TestCase("211111111110008")] // Dígito adulterado
    [TestCase("111111111130008")] // Sufixo especial não corresponde à base
    [TestCase("700000000000004")]
    [TestCase("800000000000002")]
    [TestCase("900000000000009")]
    [TestCase("000000000000000")]
    [TestCase("300000000000007")] // Prefixo não permitido
    [TestCase("70000000000000")]
    [TestCase("7000000000000050")]
    [TestCase("700 0000 0000 0005")]
    [TestCase("70000000000000A")]
    [TestCase("７０００００００００００００５")] // Unicode de largura inteira não é ASCII
    [TestCase("")]
    public void Invalid_cns_is_rejected_without_disclosing_the_supplied_value(string value)
    {
        var error = Assert.Throws<InvalidDataException>(
            () => CnsRules.ValidateOptionalSmsCns(value));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(CnsRules.IsValid(value), Is.False);
            Assert.That(error!.Message, Does.Contain("dígito verificador"));
            Assert.That(error.Message, Does.Not.Contain(value.Length == 0 ? "XXXXX" : value));
        }));
    }

    [Test]
    public void Absence_is_optional_but_blank_input_is_not_absence()
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.That(CnsRules.IsValid(null), Is.False);
            Assert.DoesNotThrow(() => CnsRules.ValidateOptionalSmsCns(null));
            Assert.Throws<InvalidDataException>(() => CnsRules.ValidateOptionalSmsCns(" "));
        }));
    }
}
