using System.Text;
using Jornada.Contracts;
using Jornada.Ingestion;
using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class DeterministicPropertyTests
{
    [Test]
    public void Cpf_normalization_preserves_generated_valid_values_and_rejects_mutated_check_digit()
    {
        var random = new Random(37301);
        for (var i = 0; i < 1000; i++)
        {
            var cpf = BuildCpf(random);
            var decorated = $" {cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]} ";
            Assert.That(CpfRules.NormalizeAndValidate(decorated), Is.EqualTo(cpf), $"case={i}");
            var last = cpf[10] == '9' ? '0' : (char)(cpf[10] + 1);
            Assert.That(CpfRules.NormalizeAndValidate(cpf[..10] + last), Is.Null, $"mutated={i}");
        }
    }

    [Test]
    public void Phone_v2_is_invariant_to_ascii_punctuation_for_domestic_numbers()
    {
        var random = new Random(37302);
        for (var i = 0; i < 1000; i++)
        {
            var length = i % 2 == 0 ? 10 : 11;
            var digits = new string(Enumerable.Range(0, length).Select(_ => (char)('0' + random.Next(10))).ToArray());
            var decorated = $" ({digits[..2]}) {digits[2..7]}-{digits[7..]} ";
            Assert.That(TransversalAttributeInstanceKey.Compute("MULTI", "TELEFONE_BR_CANONICO_V2", decorated), Is.EqualTo("55" + digits), $"case={i}");
        }
    }

    [Test]
    public void Email_v2_is_ascii_case_idempotent_and_preserves_non_ascii_codepoints()
    {
        var random = new Random(37303);
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        for (var i = 0; i < 1000; i++)
        {
            string Part(int n) => new(Enumerable.Range(0, n).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            var local = Part(6) + "é" + Part(3);
            var domain = Part(5) + ".TEST";
            var mixed = new string((local + "@" + domain).Select((c, idx) => idx % 3 == 0 && c is >= 'a' and <= 'z' ? char.ToUpperInvariant(c) : c).ToArray());
            var first = TransversalAttributeInstanceKey.Compute("MULTI", "EMAIL_CANONICO_V2", "\t" + mixed + "\u00A0");
            var second = TransversalAttributeInstanceKey.Compute("MULTI", "EMAIL_CANONICO_V2", first);
            Assert.That(second, Is.EqualTo(first), $"case={i}");
            Assert.That(first, Does.Contain("é"), $"non-ascii={i}");
        }
    }

    [Test]
    public void Deterministic_zip_is_byte_stable_for_seeded_random_payloads()
    {
        var random = new Random(37304);
        for (var i = 0; i < 100; i++)
        {
            var suffix = Convert.ToHexString(RandomBytes(random, random.Next(1, 128))).ToLowerInvariant();
            var manifest = Encoding.UTF8.GetBytes($"{{\"formatoVersao\":2,\"codigoSistemaOrigem\":\"P{i}\",\"pessoaSchemaVersao\":1,\"dataReferencia\":\"2026-09-01\"}}");
            var pessoas = Encoding.UTF8.GetBytes($"{{\"codigoPessoaOrigem\":\"{suffix}\",\"atributos\":[]}}\n");
            var registros = RandomBytes(random, random.Next(0, 256));
            var a = DeterministicIngestionZipWriter.Create(manifest, pessoas, registros);
            var b = DeterministicIngestionZipWriter.Create(manifest, pessoas, registros);
            Assert.That(b, Is.EqualTo(a), $"case={i}");
            Assert.That(IngestionPackageInspector.ComputeSha256(b), Is.EqualTo(IngestionPackageInspector.ComputeSha256(a)), $"sha={i}");
        }
    }

    private static byte[] RandomBytes(Random random, int length)
    {
        var bytes = new byte[length];
        random.NextBytes(bytes);
        return bytes;
    }

    private static string BuildCpf(Random random)
    {
        int[] d = new int[11];
        do { for (var i = 0; i < 9; i++) d[i] = random.Next(10); }
        while (d.Take(9).Distinct().Count() == 1);
        d[9] = Digit(d, 9, 10);
        d[10] = Digit(d, 10, 11);
        return string.Concat(d.Select(x => (char)('0' + x)));
    }

    private static int Digit(int[] d, int length, int startWeight)
    {
        var sum = 0;
        for (var i = 0; i < length; i++) sum += d[i] * (startWeight - i);
        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
