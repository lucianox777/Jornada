using System.Globalization;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class CandidateSamplingTests
{
    private static readonly byte[] Seed = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static readonly Guid A = Guid.Parse("94000000-0000-4000-8000-000000000001");
    private static readonly Guid B = Guid.Parse("94000000-0000-4000-8000-000000000002");
    private static readonly Guid C = Guid.Parse("94000000-0000-4000-8000-000000000003");
    private static readonly Guid D = Guid.Parse("94000000-0000-4000-8000-000000000004");
    private static readonly Guid E = Guid.Parse("94000000-0000-4000-8000-000000000005");
    private static readonly Guid F = Guid.Parse("94000000-0000-4000-8000-000000000006");
    private static readonly Guid G = Guid.Parse("94000000-0000-4000-8000-000000000007");
    private static readonly Guid SourceA = Guid.Parse("95000000-0000-4000-8000-000000000001");
    private static readonly Guid SourceB = Guid.Parse("95000000-0000-4000-8000-000000000002");
    private static readonly Guid SourceC = Guid.Parse("95000000-0000-4000-8000-000000000003");
    private static readonly DateTimeOffset Captured = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void DrawIsReproducibleAndIndependentOfInputOrder()
    {
        var population = Enumerable.Range(0, 100).ToArray();
        var first = CandidateSamplingDesign.Draw(population, 20, Seed, "sources", x => x.ToString(CultureInfo.InvariantCulture));
        var reversed = CandidateSamplingDesign.Draw(population.Reverse().ToArray(), 20, Seed, "sources", x => x.ToString(CultureInfo.InvariantCulture));
        Assert.That(reversed, Is.EqualTo(first));
        Assert.That(first.Distinct().Count(), Is.EqualTo(20));
        Assert.That(CandidateSamplingDesign.Draw(population, 20, Enumerable.Repeat((byte)9, 32).ToArray(),
            "sources", x => x.ToString(CultureInfo.InvariantCulture)), Is.Not.EqualTo(first));
        Assert.That(CandidateSamplingDesign.Fingerprint(Seed, "frame", "x"), Does.Not.EqualTo(
            CandidateSamplingDesign.Fingerprint(Seed, "selection", "x")));
    }

    [Test]
    public void ProbabilitiesUseActualDenominators()
    {
        var (p, w) = CandidateSamplingDesign.Inclusion(10, 4, 8, 2);
        Assert.Multiple(() =>
        {
            Assert.That(p, Is.EqualTo(0.1m));
            Assert.That(w, Is.EqualTo(10m));
            Assert.That(CandidateSamplingDesign.Inclusion(10, 10, 2, 2).Weight, Is.EqualTo(1m));
        });
        Assert.Throws<ArgumentOutOfRangeException>(() => CandidateSamplingDesign.Inclusion(2, 3, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CandidateSamplingDesign.Inclusion(2, 1, 0, 1));
        Assert.Throws<ArgumentException>(() => CandidateSamplingDesign.Draw(new[] { 1, 1 }, 1, Seed, "x", x => x.ToString(CultureInfo.InvariantCulture)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CandidateSamplingDesign.Draw(new[] { 1 }, 1, new byte[1], "x", x => x.ToString(CultureInfo.InvariantCulture)));
    }

    [Test]
    public async Task CensusEnumeratesAllPassesAndDeduplicatesBeforeSampling()
    {
        var capture = await RunAsync(Frame(3), Options(3), Seed);
        Assert.Multiple(() =>
        {
            Assert.That(capture.FrameSize, Is.EqualTo(3));
            Assert.That(capture.SelectedSources, Is.EqualTo(3));
            Assert.That(capture.EmptySources, Is.EqualTo(1));
            Assert.That(capture.EnumeratedPairs, Is.EqualTo(12));
            Assert.That(capture.OverlapCount, Is.EqualTo(2));
            Assert.That(capture.Pairs.Count, Is.EqualTo(10));
            Assert.That(capture.PassCounts.Select(x => x.Members), Is.EqualTo(new long[] { 4, 4, 4, 2, 2 }));
            Assert.That(capture.PassCounts.Select(x => x.Primary), Is.EqualTo(new long[] { 4, 2, 2, 2, 2 }));
            Assert.That(capture.PassCounts.Select(x => x.Selected), Is.EqualTo(new long[] { 2, 2, 2, 2, 2 }));
            Assert.That(capture.EstimatedPairPopulation, Is.EqualTo(12m));
            Assert.That(capture.PassCounts.Select(x => x.EstimatedMembership), Is.EqualTo(
                BirthBlockingPlan.OrderedPasses.Select(pass => capture.Pairs.Where(p => (p.Membership & pass) != 0).Sum(p => p.DesignWeight))));
            Assert.That(capture.Pairs.Where(p => p.PrimaryPass == BirthBlockingPass.ExactDate).All(p =>
                p.StratumPopulation == 2 && p.StratumSampleSize == 1 && p.InclusionProbability == 0.5m && p.DesignWeight == 2m), Is.True);
        });
        var reordered = await RunAsync(Frame(3) with { Sources = Frame(3).Sources.Reverse().ToArray() }, Options(3), Seed);
        Assert.That(reordered.SelectionFingerprint, Is.EqualTo(capture.SelectionFingerprint));
        Assert.That(reordered.UniverseFingerprint, Is.EqualTo(capture.UniverseFingerprint));
        Assert.That(reordered.FrameFingerprint, Is.EqualTo(capture.FrameFingerprint));
        Assert.That(reordered.Pairs, Is.EqualTo(capture.Pairs));
    }

    [Test]
    public async Task SourceSamplingAndV1PreserveTheirOwnDenominators()
    {
        var frame = Frame(3);
        var selected = CandidateSamplingDesign.Draw(frame.Sources, 1, Seed, "sources", s => s.SourceId.ToString("D"));
        var capture = await RunAsync(frame, Options(1), Seed);
        Assert.That(capture.SelectedSources, Is.EqualTo(1));
        Assert.That(capture.FrameSize, Is.EqualTo(3));
        if (selected[0].SourceId == SourceC)
        {
            Assert.That(capture.EmptySources, Is.EqualTo(1));
            Assert.That(capture.Pairs, Is.Empty);
            Assert.That(capture.EstimatedPairPopulation, Is.Zero);
        }
        else
        {
            Assert.That(capture.Pairs.All(p => p.InclusionProbability > 0 && p.InclusionProbability <= 1), Is.True);
            Assert.That(capture.EstimatedPairPopulation, Is.EqualTo(18m));
            Assert.That(capture.Pairs.Where(p => p.PrimaryPass == BirthBlockingPass.ExactDate).All(p => p.DesignWeight == 6m), Is.True);
        }
        var v1 = await RunAsync(Frame(1), Options(1) with
        {
            UseComponents = false, PrimaryPassQuotas = new[] { 1, 0, 0, 0, 0 }
        }, Seed);
        Assert.That(v1.EnumeratedPairs, Is.EqualTo(2));
        Assert.That(v1.Pairs.Count, Is.EqualTo(1));
        Assert.That(v1.Pairs[0].DesignWeight, Is.EqualTo(2m));
        Assert.That(v1.PassCounts.Skip(1).All(x => x.Members == 0 && x.Selected == 0), Is.True);
    }

    [Test]
    public void InvalidFramesAndLimitsFailClosed()
    {
        Assert.ThrowsAsync<ArgumentException>(async () => await RunAsync(Frame(1) with { Complete = false }, Options(1), Seed));
        Assert.ThrowsAsync<ArgumentException>(async () => await RunAsync(Frame(1) with { Reference = " " }, Options(1), Seed));
        Assert.ThrowsAsync<ArgumentException>(async () => await RunAsync(Frame(1) with { Sources = new[] { Source(SourceA), Source(SourceA) } }, Options(1), Seed));
        Assert.ThrowsAsync<ArgumentException>(async () => await RunAsync(Frame(1), Options(1), new byte[31]));
        Assert.ThrowsAsync<ArgumentException>(async () => await RunAsync(Frame(1), Options(2), Seed));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await RunAsync(Frame(1), Options(1) with { MaxCandidatesPerSource = 5 }, Seed));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await RunAsync(Frame(3), Options(3) with { MaxEnumeratedPairs = 11 }, Seed));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await RunAsync(Frame(1), Options(1) with { MaxSelectedPairs = 4 }, Seed));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await CandidateSamplingEngine.CaptureAsync(Frame(1), Options(1), Seed, "snapshot", Captured,
            (_, _) => Task.FromResult<IReadOnlyList<CandidateSamplingCandidate>>(new[] { new CandidateSamplingCandidate(A, BirthBlockingPass.None) }), CancellationToken.None));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await CandidateSamplingEngine.CaptureAsync(Frame(1), Options(1), Seed, "snapshot", Captured,
            (_, _) => Task.FromResult<IReadOnlyList<CandidateSamplingCandidate>>(new[] { new CandidateSamplingCandidate(A, BirthBlockingPass.ExactDate), new CandidateSamplingCandidate(A, BirthBlockingPass.ExactDate) }), CancellationToken.None));
    }

    private static CandidateUniverseSource Source(Guid id) =>
        new(id, new DateOnly(1982, 4, 10), "Maria", "Ana");

    private static CandidateSamplingFrame Frame(int count) => new("CI_FRAME_V1", true,
        new[] { Source(SourceA), Source(SourceB), Source(SourceC) with { BirthDate = new DateOnly(1970, 1, 1) } }.Take(count).ToArray());

    private static CandidateSamplingOptions Options(int sampleSize) =>
        new(true, 1, sampleSize, new[] { 1, 1, 1, 1, 1 }, 10, 100, 1000, 1000, 60);

    private static async Task<CandidateSamplingCapture> RunAsync(CandidateSamplingFrame frame, CandidateSamplingOptions options, byte[] seed) =>
        await CandidateSamplingEngine.CaptureAsync(frame, options, seed, "snapshot", Captured, (source, _) =>
        {
            var plan = BirthBlockingPlan.Create(source.BirthDate, source.Name, source.Mother, options.UseComponents, options.YearTolerance);
            var candidates = new[]
            {
                (A, new DateOnly(1982, 4, 10), "Maria", "Ana"),
                (B, new DateOnly(1982, 4, 11), "Maria", "Ana"),
                (C, new DateOnly(1982, 5, 10), "Maria", "Ana"),
                (D, new DateOnly(1982, 10, 4), "Maria", "Ana"),
                (E, new DateOnly(1983, 4, 10), "Maria", "Ana"),
                (F, new DateOnly(1982, 4, 10), "Outra", "Outra"),
                (G, new DateOnly(1982, 5, 11), "Outra", "Outra")
            }.Select(x => new CandidateSamplingCandidate(x.Item1, plan.Match(x.Item2, x.Item3, x.Item4)))
                .Where(x => x.Membership != BirthBlockingPass.None).ToArray();
            return Task.FromResult<IReadOnlyList<CandidateSamplingCandidate>>(candidates);
        }, CancellationToken.None);
}
