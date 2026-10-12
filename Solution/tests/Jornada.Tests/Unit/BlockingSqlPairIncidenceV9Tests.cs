using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class BlockingSqlPairIncidenceV9Tests
{
    [TestCase(0L, 0L, 0L)]
    [TestCase(1L, 0L, 0L)]
    [TestCase(2L, 1L, 1L)]
    [TestCase(3L, 3L, 1L)]
    [TestCase(4L, 6L, 2L)]
    [TestCase(5L, 10L, 2L)]
    [TestCase(6L, 15L, 3L)]
    public void PhysicalSignatureBucketEnumeratesAllPairIncidencesVsDisjointAdjacentPairs(
        long people, long full, long adjacent)
    {
        var result = BlockingPassPairIncidenceCalculatorV9.ForGroup(people);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.FullPairs, Is.EqualTo((decimal)full));
            Assert.That(result.AdjacentPairs, Is.EqualTo((decimal)adjacent));
            Assert.That(result.FullPairs, Is.GreaterThanOrEqualTo(result.AdjacentPairs));
        }));
    }

    [Test]
    public void AggregatesDifferentBucketSizesWithoutDeclaringUniqueCandidatePairs()
    {
        var result = BlockingPassPairIncidenceCalculatorV9.Aggregate(
            "FIRST_MOTHER", [1L, 2L, 3L, 4L]);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.PassId, Is.EqualTo("FIRST_MOTHER"));
            Assert.That(result.SignatureGroups, Is.EqualTo(4));
            Assert.That(result.RecoverableSignatureGroups, Is.EqualTo(3));
            Assert.That(result.FullPairIncidences, Is.EqualTo(10m));
            Assert.That(result.AdjacentPairIncidences, Is.EqualTo(4m));
            Assert.That(result.FullPairIncidences, Is.GreaterThan(result.AdjacentPairIncidences));
        }));
    }

    [Test]
    public void SameUnderlyingPairsInDifferentPassesCannotBeSummedAsUniqueIndividuals()
    {
        var a = BlockingPassPairIncidenceCalculatorV9.Aggregate("PASS_A", [3L]);
        var b = BlockingPassPairIncidenceCalculatorV9.Aggregate("PASS_B", [3L]);

        // Six pair incidences across the two passes can still be exactly
        // THREE unique physical pairs, if the signatures cover same people.
        Assert.Multiple((Action)(() =>
        {
            Assert.That(a.FullPairIncidences + b.FullPairIncidences, Is.EqualTo(6m));
            Assert.That(a.AdjacentPairIncidences + b.AdjacentPairIncidences, Is.EqualTo(2m));
            Assert.That(a.SignatureGroups + b.SignatureGroups, Is.EqualTo(2));
        }));
    }

    [Test]
    public void LargeBucketDoesNotOverflowAnIntermediateInt32OrBigintProduct()
    {
        var value = BlockingPassPairIncidenceCalculatorV9.ForGroup(100_000L);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(value.FullPairs, Is.EqualTo(4_999_950_000m));
            Assert.That(value.AdjacentPairs, Is.EqualTo(50_000m));
        }));
    }

    [Test]
    public void InvalidOrEmptyMaterializedBucketsAreRejected()
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() =>
                BlockingPassPairIncidenceCalculatorV9.ForGroup(-1)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That((Action)(() =>
                BlockingPassPairIncidenceCalculatorV9.Aggregate("", [1L])),
                Throws.TypeOf<ArgumentException>());
            Assert.That((Action)(() =>
                BlockingPassPairIncidenceCalculatorV9.Aggregate("FIRST", [0L])),
                Throws.InvalidOperationException);
        }));
        var empty = BlockingPassPairIncidenceCalculatorV9.Aggregate(
            "NO_KEYS", Array.Empty<long>());
        Assert.Multiple((Action)(() =>
        {
            Assert.That(empty.SignatureGroups, Is.Zero);
            Assert.That(empty.RecoverableSignatureGroups, Is.Zero);
            Assert.That(empty.FullPairIncidences, Is.Zero);
            Assert.That(empty.AdjacentPairIncidences, Is.Zero);
        }));
    }
}
