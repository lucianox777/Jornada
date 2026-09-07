using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class CandidateWeightedEstimatorTests
{
    private static readonly byte[] Seed = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static readonly DateTimeOffset Captured = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid[] Sources = Enumerable.Range(1, 6).Select(i => Id(100 + i)).ToArray();
    private static readonly Guid[] Candidates = Enumerable.Range(1, 4).Select(Id).ToArray();
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static Guid Id(int value) => Guid.Parse($"98000000-0000-4000-8000-{value:000000000000}");

    [Test]
    public void FeatureExtractionPreservesMissingAndJointBirthStates()
    {
        var exact = CandidateLabeling.Compare(new("José", new DateOnly(1982, 4, 10), "Ana",
            "JOSE", new DateOnly(1982, 4, 10), "ANA"));
        Assert.That(exact, Is.EqualTo(new CandidateComparisonVector(NameComparisonState.EXACT, NameComparisonState.EXACT, 7)));
        var missing = CandidateLabeling.Compare(new(null, null, "Ana", "Maria", new DateOnly(1982, 4, 10), null));
        Assert.That(missing, Is.EqualTo(new CandidateComparisonVector(null, null, null)));
        var transposed = CandidateLabeling.Compare(new("A", new DateOnly(1982, 4, 10), "B",
            "A", new DateOnly(1982, 10, 4), "B"));
        Assert.That(transposed.BirthAgreementMask, Is.EqualTo(4));
        var neighbor = CandidateLabeling.Compare(new("A", new DateOnly(1982, 4, 10), "B",
            "A", new DateOnly(1983, 4, 10), "B"));
        Assert.That(neighbor.BirthAgreementMask, Is.EqualTo(3));
    }

    [Test]
    public async Task CompleteIndependentCorpusProducesWeightedDiagnosticOnly()
    {
        var capture = await CaptureAsync();
        var corpus = CandidateLabeling.Validate(capture, Corpus(capture));
        var result = CandidateWeightedEstimator.Estimate(corpus, 0.5m);
        Assert.Multiple(() =>
        {
            Assert.That(result.Version, Is.EqualTo(CandidateWeightedEstimator.Version));
            Assert.That(result.TrainingPairs, Is.EqualTo(12));
            Assert.That(result.EvaluationPairs, Is.EqualTo(6));
            Assert.That(result.TrainingClasses.Select(c => c.DesignWeight), Is.EqualTo(new[] { 8m, 8m }));
            Assert.That(result.TrainingClasses.Select(c => c.IndependentGroups), Is.EqualTo(new[] { 4, 4 }));
            Assert.That(result.TrainingClasses.Select(c => c.EffectiveSampleSize), Is.EqualTo(new[] { 4m, 8m }));
            Assert.That(result.TrainingClasses.All(c => c.Distributions.Count == 3), Is.True);
        });
        var matched = result.TrainingClasses.Single(c => c.Label == IndependentMatchLabel.Match);
        var name = matched.Distributions.Single(d => d.Feature == "NOME");
        Assert.That(name.Probabilities["EXACT"], Is.EqualTo(4.5m / 10.5m));
        Assert.That(name.Probabilities["HIGH"], Is.EqualTo(4.5m / 10.5m));
        var birth = matched.Distributions.Single(d => d.Feature == "NASCIMENTO_CONJUNTO");
        Assert.That(birth.Probabilities["111"], Is.EqualTo(8.5m / 12.5m));
        Assert.That(birth.Probabilities.Count, Is.EqualTo(9));
        Assert.That(result.TrainingClasses.SelectMany(c => c.Distributions).All(d =>
            Math.Abs(d.Probabilities.Values.Sum() - 1m) < 0.000000000000000000000001m), Is.True);
        Assert.That(System.Text.Json.JsonSerializer.Serialize(result), Does.Not.Contain("Maria"));
        Assert.That(System.Text.Json.JsonSerializer.Serialize(result), Does.Not.Contain(Sources[0].ToString("D")));
    }

    [Test]
    public async Task EvaluationLabelsAndFeaturesDoNotInfluenceTrainingDistributions()
    {
        var capture = await CaptureAsync();
        var original = Corpus(capture);
        var changed = original with
        {
            Evidence = original.Evidence.Select(e => Sources.Skip(4).Contains(e.SourceId)
                ? e with { Comparison = new CandidateComparisonVector(null, null, null) } : e).ToArray(),
            Labels = original.Labels.Select(l => Sources.Skip(4).Contains(l.SourceId)
                ? l with { Decision = l.Decision == IndependentMatchLabel.Match ? IndependentMatchLabel.NonMatch : IndependentMatchLabel.Match } : l).ToArray()
        };
        var first = CandidateWeightedEstimator.Estimate(CandidateLabeling.Validate(capture, original), 0.5m);
        var second = CandidateWeightedEstimator.Estimate(CandidateLabeling.Validate(capture, changed), 0.5m);
        Assert.That(System.Text.Json.JsonSerializer.Serialize(second.TrainingClasses),
            Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(first.TrainingClasses)));
    }

    [Test]
    public async Task MissingValuesAreExplicitStatesNotArtificialDisagreements()
    {
        var capture = await CaptureAsync();
        var corpus = Corpus(capture);
        var key = corpus.Labels.First(l => l.Decision == IndependentMatchLabel.Match && l.SourceId == Sources[0]);
        corpus = corpus with { Evidence = corpus.Evidence.Select(e => e.SourceId == key.SourceId && e.CandidateId == key.CandidateId
            ? e with { Comparison = new CandidateComparisonVector(null, null, null) } : e).ToArray() };
        var result = CandidateWeightedEstimator.Estimate(CandidateLabeling.Validate(capture, corpus), 0.5m);
        var matched = result.TrainingClasses.Single(c => c.Label == IndependentMatchLabel.Match);
        Assert.That(matched.Distributions.Single(d => d.Feature == "NOME").Probabilities["MISSING"], Is.EqualTo(2.5m / 10.5m));
        Assert.That(matched.Distributions.Single(d => d.Feature == "NASCIMENTO_CONJUNTO").Probabilities["MISSING"], Is.EqualTo(2.5m / 12.5m));
    }

    [Test]
    public async Task ManifestAndCompleteInventoryAreMandatory()
    {
        var capture = await CaptureAsync();
        var corpus = Corpus(capture);
        Assert.Throws<ArgumentException>(() => CandidateLabeling.Validate(capture, corpus with { Manifest = corpus.Manifest with { Complete = false } }));
        Assert.Throws<ArgumentException>(() => CandidateLabeling.Validate(capture, corpus with { Manifest = corpus.Manifest with { PolicyVersion = "UNAPPROVED" } }));
        Assert.Throws<ArgumentException>(() => CandidateLabeling.Validate(capture, corpus with { Manifest = corpus.Manifest with { SelectionFingerprint = new string('b', 64) } }));
        Assert.Throws<ArgumentException>(() => CandidateLabeling.Validate(capture, corpus with { Manifest = corpus.Manifest with { NormalizationVersion = "OTHER" } }));
        Assert.Throws<ArgumentException>(() => CandidateLabeling.Validate(capture, corpus with { Manifest = corpus.Manifest with { AttestedAt = Captured.AddMinutes(-1) } }));
        Assert.Throws<InvalidOperationException>(() => CandidateLabeling.Validate(capture, corpus with { Labels = corpus.Labels.Skip(1).ToArray() }));
        Assert.Throws<InvalidOperationException>(() => CandidateLabeling.Validate(capture, corpus with { Evidence = corpus.Evidence.Skip(1).ToArray() }));
        Assert.Throws<InvalidOperationException>(() => CandidateLabeling.Validate(capture, corpus with { Labels = corpus.Labels.Concat(corpus.Labels.Take(1)).ToArray() }));
        Assert.Throws<InvalidOperationException>(() => CandidateLabeling.Validate(capture, corpus with { Evidence = corpus.Evidence.Concat(corpus.Evidence.Take(1)).ToArray() }));
    }

    [Test]
    public async Task LabelsCannotBeInferredOrAcceptedWithoutIndependentProvenance()
    {
        var capture = await CaptureAsync();
        var corpus = Corpus(capture);
        var first = corpus.Labels[0];
        void Reject(CandidateIndependentLabel label) => Assert.Throws<InvalidOperationException>(() =>
            CandidateLabeling.Validate(capture, corpus with { Labels = corpus.Labels.Select(l => l == first ? label : l).ToArray() }));
        Reject(first with { Method = (IndependentLabelMethod)99 });
        Reject(first with { Decision = (IndependentMatchLabel)99 });
        Reject(first with { EvidenceReference = "" });
        Reject(first with { EvidenceFingerprint = "not-a-digest" });
        Reject(first with { DecidedAt = Captured.AddDays(2) });
        Reject(first with { CandidateId = Id(999) });
    }

    [Test]
    public async Task SourceAndIndependenceGroupLeakageAreRejected()
    {
        var capture = await CaptureAsync();
        var corpus = Corpus(capture);
        Assert.Throws<InvalidOperationException>(() => CandidateLabeling.Validate(capture, corpus with { Partitions = corpus.Partitions.Skip(1).ToArray() }));
        Assert.Throws<InvalidOperationException>(() => CandidateLabeling.Validate(capture, corpus with { Partitions = corpus.Partitions.Concat(corpus.Partitions.Take(1)).ToArray() }));
        var groups = corpus.Partitions.ToArray();
        groups[4] = groups[4] with { IndependenceGroupId = groups[0].IndependenceGroupId };
        Assert.Throws<InvalidOperationException>(() => CandidateLabeling.Validate(capture, corpus with { Partitions = groups }));
        groups = corpus.Partitions.ToArray();
        groups[4] = groups[4] with { Partition = CandidateCorpusPartition.Training };
        groups[5] = groups[5] with { Partition = CandidateCorpusPartition.Training };
        Assert.Throws<InvalidOperationException>(() => CandidateLabeling.Validate(capture, corpus with { Partitions = groups }));
    }

    [Test]
    public async Task TamperedWeightsAndStrataCannotEnterEstimator()
    {
        var capture = await CaptureAsync();
        var corpus = Corpus(capture);
        var pairs = capture.Pairs.ToArray();
        pairs[0] = pairs[0] with { DesignWeight = 1m };
        Assert.Throws<InvalidOperationException>(() => CandidateLabeling.Validate(capture with { Pairs = pairs }, corpus));
        pairs = capture.Pairs.ToArray();
        pairs[0] = pairs[0] with { InclusionProbability = 0.9m };
        Assert.Throws<InvalidOperationException>(() => CandidateLabeling.Validate(capture with { Pairs = pairs }, corpus));
        pairs = capture.Pairs.ToArray();
        pairs[0] = pairs[0] with { StratumPopulation = 3 };
        Assert.Throws<InvalidOperationException>(() => CandidateLabeling.Validate(capture with { Pairs = pairs }, corpus));
        pairs = capture.Pairs.ToArray();
        pairs[0] = pairs[0] with { Membership = BirthBlockingPass.None };
        Assert.Throws<InvalidOperationException>(() => CandidateLabeling.Validate(capture with { Pairs = pairs }, corpus));
    }

    [Test]
    public async Task InconclusiveAndMissingClassCoverageFailClosed()
    {
        var capture = await CaptureAsync();
        var corpus = Corpus(capture);
        var first = corpus.Labels[0];
        var inconclusive = corpus with { Labels = corpus.Labels.Select(l => l == first
            ? l with { Decision = IndependentMatchLabel.Inconclusive } : l).ToArray() };
        Assert.Throws<InvalidOperationException>(() => CandidateWeightedEstimator.Estimate(CandidateLabeling.Validate(capture, inconclusive), 0.5m));
        var noMatches = corpus with { Labels = corpus.Labels.Select(l => l with { Decision = IndependentMatchLabel.NonMatch }).ToArray() };
        Assert.Throws<InvalidOperationException>(() => CandidateWeightedEstimator.Estimate(CandidateLabeling.Validate(capture, noMatches), 0.5m));
        var onlyOneGroup = corpus with { Labels = corpus.Labels.Select(l => l.SourceId != Sources[0] && l.Decision == IndependentMatchLabel.Match
            ? l with { Decision = IndependentMatchLabel.NonMatch } : l).ToArray() };
        Assert.Throws<InvalidOperationException>(() => CandidateWeightedEstimator.Estimate(CandidateLabeling.Validate(capture, onlyOneGroup), 0.5m));
        var noEvaluationMatch = corpus with { Labels = corpus.Labels.Select(l => Sources.Skip(4).Contains(l.SourceId)
            ? l with { Decision = IndependentMatchLabel.NonMatch } : l).ToArray() };
        Assert.Throws<InvalidOperationException>(() => CandidateWeightedEstimator.Estimate(CandidateLabeling.Validate(capture, noEvaluationMatch), 0.5m));
    }

    [Test]
    public async Task SmoothingAndEffectiveSampleRequirementsAreExplicit()
    {
        var capture = await CaptureAsync();
        var validated = CandidateLabeling.Validate(capture, Corpus(capture));
        Assert.Throws<ArgumentOutOfRangeException>(() => CandidateWeightedEstimator.Estimate(validated, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => CandidateWeightedEstimator.Estimate(validated, -1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => CandidateWeightedEstimator.Estimate(validated, 1001m));
        Assert.Throws<InvalidOperationException>(() => CandidateWeightedEstimator.Estimate(validated, 0.5m, 5m));
        var first = CandidateWeightedEstimator.Estimate(validated, 0.5m);
        var second = CandidateWeightedEstimator.Estimate(validated, 0.5m);
        Assert.That(System.Text.Json.JsonSerializer.Serialize(second),
            Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(first)));
    }

    private static async Task<CandidateSamplingCapture> CaptureAsync()
    {
        var sources = Sources.Select(id => new CandidateUniverseSource(id, new DateOnly(1982, 4, 10), "Maria", "Ana")).ToArray();
        return await CandidateSamplingEngine.CaptureAsync(new CandidateSamplingFrame("CI_FRAME", true, sources),
            new CandidateSamplingOptions(true, 1, 6, new[] { 1, 1, 1, 1, 1 }, 10, 100, 1000, 1000, 60),
            Seed, "synthetic-snapshot", Captured, (_, _) =>
            {
                var candidates = new[]
                {
                    new CandidateSamplingCandidate(Candidates[0], BirthBlockingPass.ExactDate | BirthBlockingPass.MonthYearWithInitial | BirthBlockingPass.DayYearWithInitial),
                    new CandidateSamplingCandidate(Candidates[1], BirthBlockingPass.ExactDate),
                    new CandidateSamplingCandidate(Candidates[2], BirthBlockingPass.MonthYearWithInitial),
                    new CandidateSamplingCandidate(Candidates[3], BirthBlockingPass.NeighborYear)
                };
                return Task.FromResult<IReadOnlyList<CandidateSamplingCandidate>>(candidates);
            }, CancellationToken.None);
    }

    private static CandidateLabeledCorpus Corpus(CandidateSamplingCapture capture)
    {
        var partitions = Sources.Select((id, i) => new CandidateSourcePartition(id, Id(200 + i),
            i < 4 ? CandidateCorpusPartition.Training : CandidateCorpusPartition.Evaluation)).ToArray();
        var labels = capture.Pairs.Select(p => new CandidateIndependentLabel(p.SourceId, p.CandidateId,
            p.PrimaryPass == BirthBlockingPass.ExactDate ? IndependentMatchLabel.Match : IndependentMatchLabel.NonMatch,
            IndependentLabelMethod.GovernedReference, "SYNTHETIC_TRUTH", Hash, Captured)).ToArray();
        var evidence = capture.Pairs.Select(p => new CandidateComparisonEvidence(p.SourceId, p.CandidateId,
            new CandidateComparisonVector(p.PrimaryPass == BirthBlockingPass.ExactDate
                ? Array.IndexOf(Sources, p.SourceId) % 2 == 0 ? NameComparisonState.EXACT : NameComparisonState.HIGH
                : NameComparisonState.LOW,
                NameComparisonState.EXACT,
                p.PrimaryPass == BirthBlockingPass.ExactDate ? (byte)7 :
                    p.PrimaryPass == BirthBlockingPass.MonthYearWithInitial ? (byte)6 : (byte)3))).ToArray();
        var manifest = new CandidateLabelingManifest("CI_LABELS", CandidateLabeling.PolicyVersion,
            capture.FrameFingerprint, capture.SelectionFingerprint, capture.DesignVersion, capture.BlockingVersion,
            IdentityComparison.NormalizationVersion, CandidateLabeling.FeatureVersion, "CI_PARTITION", true, Captured);
        return new CandidateLabeledCorpus(manifest, partitions, labels, evidence);
    }
}
