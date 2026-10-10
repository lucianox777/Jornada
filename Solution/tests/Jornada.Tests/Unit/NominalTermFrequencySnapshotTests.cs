using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class NominalTermFrequencySnapshotTests
{
    [Test]
    public void Snapshot_uses_normalized_first_token_and_keeps_person_mother_domains_separate()
    {
        var snapshot = NominalTermFrequencySnapshot.Create(new[]
        {
            new NominalTermFrequencyEntry(
                NominalTermFrequencySnapshot.PersonFirstNameAttribute,
                "MARIA", 600, 1000, 0.6m),
            new NominalTermFrequencyEntry(
                NominalTermFrequencySnapshot.MotherFirstNameAttribute,
                "MARIA", 300, 1000, 0.3m),
            new NominalTermFrequencyEntry(
                NominalTermFrequencySnapshot.PersonFirstNameAttribute,
                "ZULÉICA", 10, 1000, 0.01m)
        });

        Assert.Multiple((Action)(() =>
        {
            Assert.That(snapshot.TryGetPersonFirstName("Maria da Silva", out var maria), Is.True);
            Assert.That(maria, Is.EqualTo(0.6m));
            Assert.That(snapshot.TryGetMotherFirstName("Maria Souza", out var motherMaria), Is.True);
            Assert.That(motherMaria, Is.EqualTo(0.3m));
            Assert.That(snapshot.TryGetPersonFirstName("Zuleica Krause", out var rare), Is.True);
            Assert.That(rare, Is.EqualTo(0.01m));
            Assert.That(snapshot.TryGetPersonFirstName("Nome Não Publicado", out _), Is.False);
        }));
    }

    [Test]
    public void Snapshot_fails_closed_on_inconsistent_frequency_or_unknown_attribute()
    {
        Assert.Throws<InvalidDataException>((Action)(() => NominalTermFrequencySnapshot.Create(new[]
        {
            new NominalTermFrequencyEntry(
                NominalTermFrequencySnapshot.PersonFirstNameAttribute,
                "MARIA", 600, 1000, 0.5m)
        })));

        Assert.Throws<InvalidDataException>((Action)(() => NominalTermFrequencySnapshot.Create(new[]
        {
            new NominalTermFrequencyEntry("OUTRO", "MARIA", 600, 1000, 0.6m)
        })));
    }

    [Test]
    public void Snapshot_extracts_last_significant_surname_for_person_and_mother_without_truncating_full_name()
    {
        var snapshot = NominalTermFrequencySnapshot.Create(new[]
        {
            new NominalTermFrequencyEntry(NominalTermFrequencySnapshot.PersonLastSurnameAttribute, "SANTOS", 200, 1000, 0.2m),
            new NominalTermFrequencyEntry(NominalTermFrequencySnapshot.MotherLastSurnameAttribute, "SANTOS", 80, 1000, 0.08m)
        });

        Assert.Multiple((Action)(() =>
        {
            Assert.That(snapshot.TryGetPersonLastSurname("Maria Aparecida de Oliveira Santos", out var person), Is.True);
            Assert.That(person, Is.EqualTo(0.2m));
            Assert.That(snapshot.TryGetMotherLastSurname("Ana de Souza Santos Filho", out var mother), Is.True);
            Assert.That(mother, Is.EqualTo(0.08m));
            Assert.That(snapshot.TryGetPersonLastSurname("Maria", out _), Is.False);
            Assert.That(snapshot.TryGetMotherLastSurname("Sobrenome Não Publicado", out _), Is.False);
        }));
    }
}
