using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class IbgeTypedNameFrequencySnapshotTests
{
    [Test]
    public void Snapshot_DistinguishesFirstNameFromSurname_without_assigning_surname_to_token_heuristics()
    {
        var snapshot = IbgeTypedNameFrequencyCatalog.Create(
            "censo-2022-v1",
            IbgeGeographicScope.Brazil,
            null,
            new[]
            {
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "Silva", 10),
                new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.Surname, "Silva", 100)
            });

        Assert.Multiple(() =>
        {
            Assert.That(IbgeCalibrationAttributeCatalog.TryGetOccurrences(
                snapshot, BlockingCandidateFeatureCatalog.FirstName, "silva", out var first), Is.True);
            Assert.That(first, Is.EqualTo(10));

            Assert.That(snapshot.TryGetOccurrences(
                IbgeNameStatisticKind.Surname, "SILVA", out var publishedSurname), Is.True);
            Assert.That(publishedSurname, Is.EqualTo(100));

            Assert.That(IbgeCalibrationAttributeCatalog.TryGetOccurrences(
                snapshot, BlockingCandidateFeatureCatalog.LastName, "SILVA", out var heuristicLastName), Is.False);
            Assert.That(heuristicLastName, Is.Zero);
        });
    }

    [Test]
    public void Snapshot_RequiresTerritorialCodeOutsideBrazil()
    {
        Assert.That(
            () => IbgeTypedNameFrequencyCatalog.Create(
                "v1",
                IbgeGeographicScope.Municipality,
                null,
                new[] { new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "ANA", 1) }),
            Throws.ArgumentException);
    }

    [Test]
    public void Snapshot_FingerprintIncludesStatisticKindAndTerritorialScope()
    {
        var firstName = IbgeTypedNameFrequencyCatalog.Create(
            "v1",
            IbgeGeographicScope.Brazil,
            null,
            new[] { new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "ANA", 5) });
        var surname = IbgeTypedNameFrequencyCatalog.Create(
            "v1",
            IbgeGeographicScope.Brazil,
            null,
            new[] { new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.Surname, "ANA", 5) });
        var municipality = IbgeTypedNameFrequencyCatalog.Create(
            "v1",
            IbgeGeographicScope.Municipality,
            "3550308",
            new[] { new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "ANA", 5) });

        Assert.Multiple(() =>
        {
            Assert.That(firstName.FingerprintSha256, Is.Not.EqualTo(surname.FingerprintSha256));
            Assert.That(firstName.FingerprintSha256, Is.Not.EqualTo(municipality.FingerprintSha256));
        });
    }

    [Test]
    public void UnsupportedFeature_DoesNotBorrowIbgeFrequency()
    {
        var snapshot = IbgeTypedNameFrequencyCatalog.Create(
            "v1",
            IbgeGeographicScope.Brazil,
            null,
            new[] { new IbgeTypedNameFrequencyEntry(IbgeNameStatisticKind.FirstName, "ANA", 5) });

        Assert.That(IbgeCalibrationAttributeCatalog.TryGetOccurrences(
            snapshot, BlockingCandidateFeatureCatalog.FullName, "ANA", out var occurrences), Is.False);
        Assert.That(occurrences, Is.Zero);
    }
}
