using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class ExternalNameFrequencyCatalogTests
{
    [Test]
    public void Snapshot_is_deterministic_and_order_independent()
    {
        var a = ExternalNameFrequencyCatalog.CreateIbgeSnapshot(
            "2026-09-09",
            new[]
            {
                new ExternalNameFrequencyEntry("Maria", 10),
                new ExternalNameFrequencyEntry("Joao", 5)
            });

        var b = ExternalNameFrequencyCatalog.CreateIbgeSnapshot(
            "2026-09-09",
            new[]
            {
                new ExternalNameFrequencyEntry(" joao ", 5),
                new ExternalNameFrequencyEntry("maria", 10)
            });

        Assert.That(a.FingerprintSha256, Is.EqualTo(b.FingerprintSha256));
        Assert.That(a.Entries.Select(x => x.Name), Is.EqualTo(new[] { "JOAO", "MARIA" }));
    }

    [Test]
    public void Snapshot_rejects_duplicate_normalized_names()
    {
        Assert.Throws<ArgumentException>(() => ExternalNameFrequencyCatalog.CreateIbgeSnapshot(
            "v1",
            new[]
            {
                new ExternalNameFrequencyEntry("Maria", 1),
                new ExternalNameFrequencyEntry(" maria ", 2)
            }));
    }

    [Test]
    public void Snapshot_rejects_invalid_metadata_and_counts()
    {
        Assert.Throws<ArgumentException>(() => ExternalNameFrequencyCatalog.CreateIbgeSnapshot(
            " ",
            new[] { new ExternalNameFrequencyEntry("Maria", 1) }));

        Assert.Throws<ArgumentOutOfRangeException>(() => ExternalNameFrequencyCatalog.CreateIbgeSnapshot(
            "v1",
            new[] { new ExternalNameFrequencyEntry("Maria", -1) }));
    }
}
