using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class ExternalNameFrequencySnapshotReaderTests
{
    [Test]
    public void ParseJson_BuildsCanonicalSnapshot()
    {
        const string json = """
        {
          "source": "IBGE_NOMES_NO_BRASIL",
          "source_version": "2026-09",
          "entries": [
            { "name": " Maria ", "occurrences": 20 },
            { "name": "ana", "occurrences": 10 }
          ]
        }
        """;

        var snapshot = ExternalNameFrequencySnapshotReader.ParseJson(json);

        Assert.That(snapshot.Source, Is.EqualTo(ExternalNameFrequencyCatalog.IbgeSource));
        Assert.That(snapshot.SourceVersion, Is.EqualTo("2026-09"));
        Assert.That(snapshot.Entries.Select(static x => x.Name), Is.EqualTo(new[] { "ANA", "MARIA" }));
        Assert.That(snapshot.FingerprintSha256, Has.Length.EqualTo(64));
    }

    [Test]
    public void ParseJson_RejectsUnsupportedSource()
    {
        const string json = """
        {
          "source": "OTHER",
          "source_version": "v1",
          "entries": [{ "name": "ANA", "occurrences": 1 }]
        }
        """;

        Assert.That(
            () => ExternalNameFrequencySnapshotReader.ParseJson(json),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ParseJson_RejectsInvalidDeclaredFingerprint()
    {
        const string json = """
        {
          "source": "IBGE_NOMES_NO_BRASIL",
          "source_version": "v1",
          "fingerprint_sha256": "deadbeef",
          "entries": [{ "name": "ANA", "occurrences": 1 }]
        }
        """;

        Assert.That(
            () => ExternalNameFrequencySnapshotReader.ParseJson(json),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ParseJson_AcceptsMatchingDeclaredFingerprintIgnoringHexCase()
    {
        var expected = ExternalNameFrequencyCatalog.CreateIbgeSnapshot(
            "v1",
            new[] { new ExternalNameFrequencyEntry("ANA", 1) });

        var json = $$"""
        {
          "source": "IBGE_NOMES_NO_BRASIL",
          "source_version": "v1",
          "fingerprint_sha256": "{{expected.FingerprintSha256.ToUpperInvariant()}}",
          "entries": [{ "name": "ANA", "occurrences": 1 }]
        }
        """;

        var actual = ExternalNameFrequencySnapshotReader.ParseJson(json);
        Assert.That(actual.FingerprintSha256, Is.EqualTo(expected.FingerprintSha256));
    }

    [Test]
    public void ParseJson_RejectsDuplicateAfterTechnicalNormalization()
    {
        const string json = """
        {
          "source": "IBGE_NOMES_NO_BRASIL",
          "source_version": "v1",
          "entries": [
            { "name": "Ana", "occurrences": 1 },
            { "name": " ANA ", "occurrences": 2 }
          ]
        }
        """;

        Assert.That(
            () => ExternalNameFrequencySnapshotReader.ParseJson(json),
            Throws.TypeOf<ArgumentException>());
    }
}
