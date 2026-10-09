using System.Text.Json;
using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class IbgeOfflineUReferenceTests
{
    private const string Hash = "e3cc61bcc7fca353bb134ee32de4aa50a8da708827eac3750efc0e6ebca5e885";
    private static readonly string Fixture = """
        {
          "schema_version": "JORNADA_IBGE_PUBLIC_MARGINALS_V1",
          "reference_code": "CENSO2022_NOMES_BRASIL_V1",
          "reference_content_sha256": "e3cc61bcc7fca353bb134ee32de4aa50a8da708827eac3750efc0e6ebca5e885",
          "first_name_sex": "TODOS",
          "surname_sex": "TODOS",
          "first_names": [{"name":"ANA","occurrences":3},{"name":"MARIA","occurrences":1}],
          "surnames": [{"name":"SANTOS","occurrences":2},{"name":"SILVA","occurrences":2}]
        }
        """;

    [Test]
    public void OfflineReference_IsDeterministicWithThreeDistinctCSharpSeeds()
    {
        var first = IbgeOfflineUReference.Estimate(Fixture, 128);
        Assert.That(IbgeOfflineUReference.Estimate(Fixture, 128), Is.EqualTo(first));
        using var doc = JsonDocument.Parse(first);
        var root = doc.RootElement;
        Assert.Multiple((Action)(() =>
        {
            Assert.That(root.GetProperty("schema_version").GetString(),
                Is.EqualTo("JORNADA_IBGE_CSHARP_OFFLINE_U_V1"));
            Assert.That(root.GetProperty("reference_content_sha256").GetString(), Is.EqualTo(Hash));
            Assert.That(root.GetProperty("runs").GetArrayLength(), Is.EqualTo(3));
            Assert.That(root.GetProperty("runs")[0].GetProperty("seed").GetInt32(),
                Is.EqualTo(20261011));
            Assert.That(root.GetProperty("runs")[0].GetProperty("states").EnumerateArray()
                .Sum(s => s.GetProperty("support").GetInt64()), Is.EqualTo(128));
            Assert.That(root.GetProperty("runs")[0]
                .GetProperty("analytic_exact_collision_probability").GetDecimal(),
                Is.EqualTo(0.3125m));
        }));
    }

    [Test]
    public void OfflineReference_RejectsTamperedPublicContract()
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.That(() => IbgeOfflineUReference.Estimate(
                Fixture.Replace("CENSO2022_NOMES_BRASIL_V1", "OTHER"), 10),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => IbgeOfflineUReference.Estimate(
                Fixture.Replace("\"ANA\"", "\"ana\""), 10),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => IbgeOfflineUReference.Estimate(
                Fixture.Replace("\"occurrences\":3", "\"occurrences\":0"), 10),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => IbgeOfflineUReference.Estimate(Fixture, 0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }));
    }
}
