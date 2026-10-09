using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Evaluation;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class IbgePublicMarginalsExchangeTests
{
    private const string Hash = "e3cc61bcc7fca353bb134ee32de4aa50a8da708827eac3750efc0e6ebca5e885";

    [Test]
    public void Export_MatchesExternalIndependentRunnerContract_AndIsDeterministic()
    {
        IbgeTypedNameFrequencyEntry[] entries =
        [
            new(IbgeNameStatisticKind.FirstName, "MARIA", 5),
            new(IbgeNameStatisticKind.FirstName, "ANA", 3),
            new(IbgeNameStatisticKind.FirstName, "ANA", 2),
            new(IbgeNameStatisticKind.Surname, "SILVA", 7),
            new(IbgeNameStatisticKind.Surname, "SANTOS", 4)
        ];
        var json = IbgePublicMarginalsExchange.Serialize(
            IbgePublicMarginalsExchange.Reference, Hash, "FEMININO", entries);
        var reversed = IbgePublicMarginalsExchange.Serialize(
            IbgePublicMarginalsExchange.Reference, Hash, "FEMININO", entries.Reverse());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Multiple((Action)(() =>
        {
            Assert.That(json, Is.EqualTo(reversed));
            Assert.That(root.EnumerateObject().Select(x => x.Name), Is.EquivalentTo(
                new[] { "schema_version", "reference_code", "reference_content_sha256",
                    "first_name_sex", "surname_sex", "first_names", "surnames" }));
            Assert.That(root.GetProperty("schema_version").GetString(),
                Is.EqualTo("JORNADA_IBGE_PUBLIC_MARGINALS_V1"));
            Assert.That(root.GetProperty("first_name_sex").GetString(), Is.EqualTo("FEMININO"));
            Assert.That(root.GetProperty("surname_sex").GetString(), Is.EqualTo("TODOS"));
            Assert.That(root.GetProperty("first_names")[0].GetProperty("name").GetString(),
                Is.EqualTo("ANA"));
            Assert.That(root.GetProperty("first_names")[0].GetProperty("occurrences").GetInt64(),
                Is.EqualTo(5));
            Assert.That(root.GetProperty("surnames").GetArrayLength(), Is.EqualTo(2));
        }));
    }

    [Test]
    public void Export_RejectsWrongProvenanceAndIncompleteMarginals()
    {
        var rows = new[] {
            new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "ANA", 5),
            new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.Surname, "SILVA", 3)
        };
        Assert.Multiple((Action)(() =>
        {
            Assert.That(() => IbgePublicMarginalsExchange.Serialize("OTHER", Hash, "TODOS", rows),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => IbgePublicMarginalsExchange.Serialize(
                IbgePublicMarginalsExchange.Reference, "bad", "TODOS", rows),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => IbgePublicMarginalsExchange.Serialize(
                IbgePublicMarginalsExchange.Reference, Hash, "MASCULINO", rows),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => IbgePublicMarginalsExchange.Serialize(
                IbgePublicMarginalsExchange.Reference, Hash, "TODOS", rows.Take(1)),
                Throws.TypeOf<InvalidDataException>());
        }));
    }
}
