using System.Text.Json;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9SourceGapAttributionTests
{
    [Test]
    public void Same_source_histogram_uses_declared_bins_and_reconciles_missing_candidates()
    {
        var audit = new V9SourceGapAttributionAccumulator();
        audit.Add(null, null, null, null, null, null);
        audit.Add(10m, null, "1/1", null, true, null);
        foreach (var gap in new[] {0m, .25m, .5m, 1m, 2m, 4m})
            audit.Add(10m, 10m-gap, "1/1", "1/2", true, false);
        var result = audit.Export();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.TotalSources, Is.EqualTo(8));
            Assert.That(result.WithoutFirstCandidate, Is.EqualTo(1));
            Assert.That(result.WithoutSecondCandidate, Is.EqualTo(2));
            Assert.That(result.BestCandidateCorrect, Is.EqualTo(7));
            Assert.That(result.SecondCandidateIncorrect, Is.EqualTo(6));
            Assert.That(result.BestMinusSecondLogOddsBins.Values.Sum(), Is.EqualTo(6));
            Assert.That(result.BestMinusSecondLogOddsBins["0_TO_0.25"], Is.EqualTo(1));
            Assert.That(result.BestMinusSecondLogOddsBins["4_AND_ABOVE"], Is.EqualTo(1));
            Assert.That(result.BestCandidateCells["1/1"], Is.EqualTo(7));
            Assert.That(result.SecondCandidateCells["1/2"], Is.EqualTo(6));
            Assert.That(result.Scope, Is.EqualTo(V9SourceGapAttributionAccumulator.EvidenceScope));
            Assert.That(result.PairedV2V3, Is.False);
            Assert.That(result.Certified, Is.False);
        }));
    }

    [Test]
    public void Low_frequency_cells_are_suppressed_with_totals_preserved()
    {
        var audit = new V9SourceGapAttributionAccumulator();
        for (var i = 0; i < 4; i++)
            audit.Add(3m, 2m, "1/1", "4/8", true, false);
        var result = audit.Export();
        Assert.That(result.BestCandidateCells, Is.Empty);
        Assert.That(result.SecondCandidateCells, Is.Empty);
        Assert.That(result.SuppressedBestCellObservations, Is.EqualTo(4));
        Assert.That(result.SuppressedSecondCellObservations, Is.EqualTo(4));
        var json = JsonSerializer.Serialize(result);
        Assert.That(json, Does.Not.Contain("4/8"));
        Assert.That(json, Does.Not.Contain("1/1"));
    }

    [Test]
    public void Invalid_or_inconsistent_rank_evidence_fails_closed()
    {
        var empty = new V9SourceGapAttributionAccumulator();
        Assert.That((Action)(() => empty.Add(null, 1m, null, "1/1", null, true)), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => empty.Add(1m, null, "1/1", "1/2", true, false)), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => empty.Add(1m, 0m, "9/0", "1/1", true, false)), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => empty.Add(1m, 0m, "1/1", "1/1", null, false)), Throws.TypeOf<InvalidDataException>());
    }
}
