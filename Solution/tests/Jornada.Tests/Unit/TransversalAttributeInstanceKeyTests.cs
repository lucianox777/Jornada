using System.Text.Json;
using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class TransversalAttributeInstanceKeyTests
{
    private static readonly JsonSerializerOptions VectorJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private sealed record PhoneVector(string Id, string Input, bool Valid, string? Expected);
    private sealed record PhoneVectorFile(string Rule, int[] EnvelopeTrimUnicodeCodePoints, PhoneVector[] Cases);

    [Test]
    public void Single_attribute_uses_fixed_instance_key() =>
        Assert.That(TransversalAttributeInstanceKey.Compute("SINGLE", "UNICA_V1", "qualquer valor"), Is.EqualTo("#"));

    [Test]
    public void Phone_v2_conforms_to_shared_vectors()
    {
        var vectors = LoadPhoneVectors();
        Assert.That(vectors.Rule, Is.EqualTo("TELEFONE_BR_CANONICO_V2"));
        Assert.That(vectors.EnvelopeTrimUnicodeCodePoints, Is.EqualTo(new[] { 9, 10, 13, 32, 160 }));
        Assert.Multiple(() =>
        {
            foreach (var vector in vectors.Cases)
            {
                if (vector.Valid)
                {
                    var actual = TransversalAttributeInstanceKey.Compute("MULTI", vectors.Rule, vector.Input);
                    Assert.That(actual, Is.EqualTo(vector.Expected), vector.Id);
                }
                else
                {
                    Assert.Throws<InvalidDataException>(
                        () => TransversalAttributeInstanceKey.Compute("MULTI", vectors.Rule, vector.Input), vector.Id);
                }
            }
        });
    }

    [Test]
    public void Email_instance_key_is_trimmed_and_casefolded() =>
        Assert.That(TransversalAttributeInstanceKey.Compute("MULTI", "EMAIL_CANONICO_V2", "  Pessoa@Example.TEST "), Is.EqualTo("pessoa@example.test"));

    [Test]
    public void Unknown_multi_rule_fails_closed() =>
        Assert.Throws<InvalidDataException>(() => TransversalAttributeInstanceKey.Compute("MULTI", "DESCONHECIDA", "x"));

    private static PhoneVectorFile LoadPhoneVectors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "phone", "telefone-br-canonico-v2.json");
        return JsonSerializer.Deserialize<PhoneVectorFile>(File.ReadAllText(path), VectorJsonOptions) ?? throw new InvalidDataException("Vetores de conformidade TELEFONE_BR_CANONICO_V2 inválidos.");
    }


    [Test]
    public void Email_v2_lowercases_only_ascii_and_does_not_unicode_normalize()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TransversalAttributeInstanceKey.Compute("MULTI", "EMAIL_CANONICO_V2", " JOSÉ@EXAMPLE.ORG "), Is.EqualTo("josÉ@example.org"));
            Assert.That(TransversalAttributeInstanceKey.Compute("MULTI", "EMAIL_CANONICO_V2", "Jose\u0301@Example.org"), Is.EqualTo("jose\u0301@example.org"));
            Assert.That(TransversalAttributeInstanceKey.Compute("MULTI", "EMAIL_CANONICO_V2", "José@Example.org"), Is.EqualTo("josé@example.org"));
        });
    }
}
