using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

[TestFixture]
public sealed class LedgerEvidenceFrequencyEstimatorTests
{
    [Test]
    public void Estimate_UsesComparablePairsAndKeepsMissingOutsideMu()
    {
        var phone = PersonResolutionAttributeCatalog.ContactPhone;
        var matches = new[]
        {
            Pair(phone, "11999991111", "11999991111"),
            Pair(phone, "11999992222", "11999993333"),
            Pair(phone, "11999994444", null)
        };
        var nonMatches = new[]
        {
            Pair(phone, "11999995555", "11999996666"),
            Pair(phone, "11999997777", "11999998888"),
            Pair(phone, null, "11999990000")
        };

        var item = LedgerEvidenceFrequencyEstimator.Estimate(matches, nonMatches, 1m).Single();

        Assert.Multiple(() =>
        {
            Assert.That(item.MatchExact, Is.EqualTo(1));
            Assert.That(item.MatchDisagree, Is.EqualTo(1));
            Assert.That(item.MatchMissing, Is.EqualTo(1));
            Assert.That(item.NonMatchExact, Is.EqualTo(0));
            Assert.That(item.NonMatchDisagree, Is.EqualTo(2));
            Assert.That(item.NonMatchMissing, Is.EqualTo(1));
            Assert.That(item.MExact, Is.EqualTo(0.5m));
            Assert.That(item.UExact, Is.EqualTo(0.25m));
            Assert.That(item.ExactLogLikelihoodRatio, Is.Not.Null);
            Assert.That((double)item.ExactLogLikelihoodRatio!.Value, Is.EqualTo(Math.Log(2d)).Within(1e-9));
        });
    }

    [Test]
    public void Estimate_FailsClosedWhenEitherClassHasNoComparablePairs()
    {
        var email = PersonResolutionAttributeCatalog.ContactEmail;
        var matches = new[] { Pair(email, "a@example.test", "a@example.test") };
        var nonMatches = new[] { Pair(email, "b@example.test", null) };

        var item = LedgerEvidenceFrequencyEstimator.Estimate(matches, nonMatches, 1m).Single();

        Assert.Multiple(() =>
        {
            Assert.That(item.MExact, Is.Null);
            Assert.That(item.UExact, Is.Null);
            Assert.That(item.ExactLogLikelihoodRatio, Is.Null);
            Assert.That(item.NonMatchMissing, Is.EqualTo(1));
        });
    }

    [Test]
    public void Compare_UsesSetIntersectionForMultiValuedEvidence()
    {
        var email = PersonResolutionAttributeCatalog.ContactEmail;
        var pair = new IdentityTrainingPair(
            "A", new DateOnly(1990, 1, 1), "M",
            "A", new DateOnly(1990, 1, 1), "M",
            LeftResolutionValues:
            [
                new(email, "primeiro@example.test"),
                new(email, "comum@example.test")
            ],
            RightResolutionValues:
            [
                new(email, "comum@example.test"),
                new(email, "outro@example.test")
            ]);

        Assert.That(LedgerEvidenceFrequencyEstimator.Compare(pair, email), Is.EqualTo(LedgerEvidenceState.Exact));
    }

    private static IdentityTrainingPair Pair(string attribute, string? left, string? right) =>
        new(
            "Pessoa", new DateOnly(1990, 1, 1), "Mae",
            "Pessoa", new DateOnly(1990, 1, 1), "Mae",
            LeftResolutionValues: left is null ? null : [new(attribute, left)],
            RightResolutionValues: right is null ? null : [new(attribute, right)]);
}
