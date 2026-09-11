using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class CandidateEvidenceDependencyDiagnosticTests
{
    private static readonly byte[] Seed = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static readonly DateTimeOffset Captured = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid[] Sources = Enumerable.Range(1, 6).Select(i => Id(500 + i)).ToArray();
    private static readonly Guid[] Candidates = Enumerable.Range(1, 4).Select(i => Id(600 + i)).ToArray();
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static Guid Id(int value) => Guid.Parse($"99000000-0000-4000-8000-{value:000000000000}");

    [Test]
    public async Task EvaluationDependencyIsMeasuredConditionallyByLabel()
    {
        var capture = await CaptureAsync();
        var corpus = Corpus(capture);
        var evaluationMatches = corpus.Labels
            .Where(label => Sources.Skip(4).Contains(label.SourceId) && label.Decision == IndependentMatchLabel.Match)
            .OrderBy(label => label.SourceId)
            .ToArray();
        Assert.That(evaluationMatches, Has.Length.EqualTo(2));

        var evidence = corpus.Evidence.Select(row =>
        {
            var matchIndex = Array.FindIndex(evaluationMatches,
                label => label.SourceId == row.SourceId && label.CandidateId == row.CandidateId);
            if (matchIndex < 0) return row;
            var state = matchIndex == 0 ? NameComparisonState.EXACT : NameComparisonState.LOW;
            return row with { Comparison = new CandidateComparisonVector(state, state, 7) };
        }).ToArray();

        var validated = CandidateLabeling.Validate(capture, corpus with { Evidence = evidence });
        var report = CandidateEvidenceDependencyDiagnostic.Analyze(validated);
        var dependency = report.Metrics.Single(metric =>
            metric.Label == IndependentMatchLabel.Match &&
            metric.Scope == CandidateEvidenceDependencyScope.AllStates &&
            metric.LeftFeature == "NOME" &&
            metric.RightFeature == "NOME_MAE");

        Assert.Multiple(() =>
        {
            Assert.That(report.Version, Is.EqualTo(CandidateEvidenceDependencyDiagnostic.Version));
            Assert.That(report.EvaluationPairs, Is.EqualTo(6));
            Assert.That(report.Metrics, Has.Count.EqualTo(12));
            Assert.That(report.FingerprintSha256, Has.Length.EqualTo(64));
            Assert.That(dependency.IsEstimable, Is.True);
            Assert.That(dependency.IndependentGroups, Is.EqualTo(2));
            Assert.That(dependency.PairCount, Is.EqualTo(2));
            Assert.That(dependency.TotalVariationDistance, Is.EqualTo(0.5d).Within(1e-12));
            Assert.That(dependency.NormalizedMutualInformation, Is.EqualTo(1d).Within(1e-12));
        });
    }

    [Test]
    public async Task ObservedOnlyDoesNotFabricateMetricWhenMissingnessRemovesAGroup()
    {
        var capture = await CaptureAsync();
        var corpus = Corpus(capture);
        var match = corpus.Labels.First(label =>
            Sources.Skip(4).Contains(label.SourceId) && label.Decision == IndependentMatchLabel.Match);
        var evidence = corpus.Evidence.Select(row =>
            row.SourceId == match.SourceId && row.CandidateId == match.CandidateId
                ? row with { Comparison = row.Comparison with { Mother = null } }
                : row).ToArray();

        var report = CandidateEvidenceDependencyDiagnostic.Analyze(
            CandidateLabeling.Validate(capture, corpus with { Evidence = evidence }));
        var allStates = report.Metrics.Single(metric =>
            metric.Label == IndependentMatchLabel.Match &&
            metric.Scope == CandidateEvidenceDependencyScope.AllStates &&
            metric.LeftFeature == "NOME" && metric.RightFeature == "NOME_MAE");
        var observedOnly = report.Metrics.Single(metric =>
            metric.Label == IndependentMatchLabel.Match &&
            metric.Scope == CandidateEvidenceDependencyScope.ObservedOnly &&
            metric.LeftFeature == "NOME" && metric.RightFeature == "NOME_MAE");

        Assert.Multiple(() =>
        {
            Assert.That(allStates.IsEstimable, Is.True);
            Assert.That(observedOnly.IsEstimable, Is.False);
            Assert.That(observedOnly.NonEstimableReason, Is.EqualTo("INSUFFICIENT_INDEPENDENT_GROUPS"));
            Assert.That(observedOnly.TotalVariationDistance, Is.Null);
            Assert.That(observedOnly.NormalizedMutualInformation, Is.Null);
        });
    }

    [Test]
    public async Task TrainingEvidenceDoesNotInfluenceIndependentEvaluationDiagnostic()
    {
        var capture = await CaptureAsync();
        var original = Corpus(capture);
        var changed = original with
        {
            Evidence = original.Evidence.Select(row => Sources.Take(4).Contains(row.SourceId)
                ? row with { Comparison = new CandidateComparisonVector(null, null, null) }
                : row).ToArray(),
            Labels = original.Labels.Select(label => Sources.Take(4).Contains(label.SourceId)
                ? label with { Decision = IndependentMatchLabel.Inconclusive }
                : label).ToArray()
        };

        var first = CandidateEvidenceDependencyDiagnostic.Analyze(CandidateLabeling.Validate(capture, original));
        var second = CandidateEvidenceDependencyDiagnostic.Analyze(CandidateLabeling.Validate(capture, changed));

        Assert.That(System.Text.Json.JsonSerializer.Serialize(second),
            Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(first)));
    }

    [Test]
    public async Task EvaluationMustContainConclusiveMatchAndNonMatchClasses()
    {
        var capture = await CaptureAsync();
        var corpus = Corpus(capture);

        var inconclusive = corpus with
        {
            Labels = corpus.Labels.Select(label => Sources.Skip(4).Contains(label.SourceId) &&
                label.Decision == IndependentMatchLabel.Match
                    ? label with { Decision = IndependentMatchLabel.Inconclusive }
                    : label).ToArray()
        };
        Assert.That(
            () => CandidateEvidenceDependencyDiagnostic.Analyze(CandidateLabeling.Validate(capture, inconclusive)),
            Throws.TypeOf<InvalidOperationException>());

        var noMatch = corpus with
        {
            Labels = corpus.Labels.Select(label => Sources.Skip(4).Contains(label.SourceId)
                ? label with { Decision = IndependentMatchLabel.NonMatch }
                : label).ToArray()
        };
        Assert.That(
            () => CandidateEvidenceDependencyDiagnostic.Analyze(CandidateLabeling.Validate(capture, noMatch)),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task ReportIsDeterministicAndDoesNotDefineAnApprovalThreshold()
    {
        var capture = await CaptureAsync();
        var validated = CandidateLabeling.Validate(capture, Corpus(capture));
        var first = CandidateEvidenceDependencyDiagnostic.Analyze(validated);
        var second = CandidateEvidenceDependencyDiagnostic.Analyze(validated);

        Assert.Multiple(() =>
        {
            Assert.That(second.FingerprintSha256, Is.EqualTo(first.FingerprintSha256));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(second),
                Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(first)));
            Assert.That(typeof(CandidateEvidenceDependencyReport).GetProperties()
                .Any(property => property.Name.Contains("Threshold", StringComparison.OrdinalIgnoreCase)), Is.False);
        });
    }

    private static async Task<CandidateSamplingCapture> CaptureAsync()
    {
        var sources = Sources.Select(id =>
            new CandidateUniverseSource(id, new DateOnly(1982, 4, 10), "Maria", "Ana")).ToArray();
        return await CandidateSamplingEngine.CaptureAsync(
            new CandidateSamplingFrame("DEPENDENCY_FRAME", true, sources),
            new CandidateSamplingOptions(true, 1, 6, new[] { 1, 1, 1, 1, 1 }, 10, 100, 1000, 1000, 60),
            Seed,
            "synthetic-snapshot",
            Captured,
            (_, _) =>
            {
                var candidates = new[]
                {
                    new CandidateSamplingCandidate(Candidates[0], BirthBlockingPass.ExactDate |
                        BirthBlockingPass.MonthYearWithInitial | BirthBlockingPass.DayYearWithInitial),
                    new CandidateSamplingCandidate(Candidates[1], BirthBlockingPass.ExactDate),
                    new CandidateSamplingCandidate(Candidates[2], BirthBlockingPass.MonthYearWithInitial),
                    new CandidateSamplingCandidate(Candidates[3], BirthBlockingPass.NeighborYear)
                };
                return Task.FromResult<IReadOnlyList<CandidateSamplingCandidate>>(candidates);
            },
            CancellationToken.None);
    }

    private static CandidateLabeledCorpus Corpus(CandidateSamplingCapture capture)
    {
        var partitions = Sources.Select((id, index) => new CandidateSourcePartition(
            id,
            Id(700 + index),
            index < 4 ? CandidateCorpusPartition.Training : CandidateCorpusPartition.Evaluation)).ToArray();
        var labels = capture.Pairs.Select(pair => new CandidateIndependentLabel(
            pair.SourceId,
            pair.CandidateId,
            pair.PrimaryPass == BirthBlockingPass.ExactDate
                ? IndependentMatchLabel.Match
                : IndependentMatchLabel.NonMatch,
            IndependentLabelMethod.GovernedReference,
            "SYNTHETIC_TRUTH",
            Hash,
            Captured)).ToArray();
        var evidence = capture.Pairs.Select(pair => new CandidateComparisonEvidence(
            pair.SourceId,
            pair.CandidateId,
            new CandidateComparisonVector(
                pair.PrimaryPass == BirthBlockingPass.ExactDate
                    ? Array.IndexOf(Sources, pair.SourceId) % 2 == 0
                        ? NameComparisonState.EXACT
                        : NameComparisonState.HIGH
                    : NameComparisonState.LOW,
                pair.PrimaryPass == BirthBlockingPass.ExactDate
                    ? NameComparisonState.EXACT
                    : NameComparisonState.LOW,
                pair.PrimaryPass == BirthBlockingPass.ExactDate
                    ? (byte)7
                    : pair.PrimaryPass == BirthBlockingPass.MonthYearWithInitial
                        ? (byte)6
                        : (byte)3))).ToArray();
        var manifest = new CandidateLabelingManifest(
            "DEPENDENCY_LABELS",
            CandidateLabeling.PolicyVersion,
            capture.FrameFingerprint,
            capture.SelectionFingerprint,
            capture.DesignVersion,
            capture.BlockingVersion,
            IdentityComparison.NormalizationVersion,
            CandidateLabeling.FeatureVersion,
            "DEPENDENCY_PARTITION",
            true,
            Captured);
        return new CandidateLabeledCorpus(manifest, partitions, labels, evidence);
    }
}
