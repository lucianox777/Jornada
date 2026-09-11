using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class CandidateReferenceAgreementDiagnosticTests
{
    private static readonly byte[] Seed = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static readonly DateTimeOffset Captured = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid[] Sources = Enumerable.Range(1, 6).Select(i => Id(800 + i)).ToArray();
    private static readonly Guid[] Candidates = Enumerable.Range(1, 4).Select(i => Id(900 + i)).ToArray();
    private const string LabelHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static Guid Id(int value) => Guid.Parse($"98000000-0000-4000-8000-{value:000000000000}");
    private static string Hash(char value) => new(value, 64);

    [Test]
    public async Task ReplicatedJudgmentsExposeCoverageAgreementDiscordanceAndInconclusive()
    {
        var capture = await CaptureAsync();
        var validated = CandidateLabeling.Validate(capture, Corpus(capture));
        var rows = validated.Observations
            .Where(static row => row.Partition == CandidateCorpusPartition.Evaluation)
            .Take(3)
            .ToArray();
        Assert.That(rows, Has.Length.EqualTo(3));

        var judgments = new[]
        {
            Judgment(rows[0], Hash('1'), IndependentMatchLabel.Match, Hash('a')),
            Judgment(rows[0], Hash('2'), IndependentMatchLabel.Match, Hash('b')),
            Judgment(rows[1], Hash('1'), IndependentMatchLabel.Match, Hash('c')),
            Judgment(rows[1], Hash('2'), IndependentMatchLabel.NonMatch, Hash('d')),
            Judgment(rows[2], Hash('1'), IndependentMatchLabel.Match, Hash('e')),
            Judgment(rows[2], Hash('2'), IndependentMatchLabel.Inconclusive, Hash('f'))
        };
        var evidence = AgreementEvidence(validated, judgments);

        var report = CandidateReferenceAgreementDiagnostic.Analyze(validated, evidence);
        var evaluationWeight = validated.Observations
            .Where(static row => row.Partition == CandidateCorpusPartition.Evaluation)
            .Sum(static row => row.Sample.DesignWeight);
        var assessedWeight = rows.Sum(static row => row.Sample.DesignWeight);

        Assert.Multiple(() =>
        {
            Assert.That(report.Version, Is.EqualTo(CandidateReferenceAgreementDiagnostic.Version));
            Assert.That(report.EvaluationPairs, Is.EqualTo(6));
            Assert.That(report.AssessedPairs, Is.EqualTo(3));
            Assert.That(report.ReplicatedPairs, Is.EqualTo(3));
            Assert.That(report.UnanimousPairs, Is.EqualTo(1));
            Assert.That(report.DiscordantPairs, Is.EqualTo(2));
            Assert.That(report.PairsWithInconclusiveJudgment, Is.EqualTo(1));
            Assert.That(report.DistinctIndependenceUnits, Is.EqualTo(2));
            Assert.That(report.DistinctEvidenceArtifacts, Is.EqualTo(6));
            Assert.That(report.PairwiseComparisons, Is.EqualTo(3));
            Assert.That(report.PairwiseAgreements, Is.EqualTo(1));
            Assert.That(report.PairwiseAgreement, Is.EqualTo(1m / 3m));
            Assert.That(report.ConclusivePairwiseComparisons, Is.EqualTo(2));
            Assert.That(report.ConclusivePairwiseAgreements, Is.EqualTo(1));
            Assert.That(report.ConclusivePairwiseAgreement, Is.EqualTo(0.5m));
            Assert.That(report.EvaluationDesignWeight, Is.EqualTo(evaluationWeight));
            Assert.That(report.AssessedDesignWeight, Is.EqualTo(assessedWeight));
            Assert.That(report.ReplicatedDesignWeight, Is.EqualTo(assessedWeight));
            Assert.That(report.AssessedWeightCoverage, Is.EqualTo(assessedWeight / evaluationWeight));
            Assert.That(report.ReplicatedWeightCoverage, Is.EqualTo(assessedWeight / evaluationWeight));
            Assert.That(report.MethodCounts, Has.Count.EqualTo(1));
            Assert.That(report.MethodCounts[0].Method, Is.EqualTo(IndependentLabelMethod.IndependentAdjudication));
            Assert.That(report.MethodCounts[0].Judgments, Is.EqualTo(6));
            Assert.That(report.MethodCounts[0].Pairs, Is.EqualTo(3));
            Assert.That(report.FingerprintSha256, Has.Length.EqualTo(64));
        });
    }

    [Test]
    public async Task SingleJudgmentIsAssessedButNotFabricatedAsReplicatedAgreement()
    {
        var capture = await CaptureAsync();
        var validated = CandidateLabeling.Validate(capture, Corpus(capture));
        var row = validated.Observations.First(static item => item.Partition == CandidateCorpusPartition.Evaluation);
        var evidence = AgreementEvidence(validated,
            [Judgment(row, Hash('1'), IndependentMatchLabel.Match, Hash('a'))]);

        var report = CandidateReferenceAgreementDiagnostic.Analyze(validated, evidence);

        Assert.Multiple(() =>
        {
            Assert.That(report.AssessedPairs, Is.EqualTo(1));
            Assert.That(report.ReplicatedPairs, Is.Zero);
            Assert.That(report.PairwiseComparisons, Is.Zero);
            Assert.That(report.PairwiseAgreement, Is.Null);
            Assert.That(report.ConclusivePairwiseAgreement, Is.Null);
            Assert.That(report.ReplicatedDesignWeight, Is.Zero);
            Assert.That(report.ReplicatedWeightCoverage, Is.Zero);
        });
    }

    [Test]
    public async Task TrainingUnknownAndDuplicateUnitJudgmentsFailClosed()
    {
        var capture = await CaptureAsync();
        var validated = CandidateLabeling.Validate(capture, Corpus(capture));
        var evaluation = validated.Observations.First(static item => item.Partition == CandidateCorpusPartition.Evaluation);
        var training = validated.Observations.First(static item => item.Partition == CandidateCorpusPartition.Training);

        var trainingEvidence = AgreementEvidence(validated,
            [Judgment(training, Hash('1'), IndependentMatchLabel.Match, Hash('a'))]);
        Assert.That(() => CandidateReferenceAgreementDiagnostic.Analyze(validated, trainingEvidence),
            Throws.TypeOf<InvalidOperationException>());

        var unknown = Judgment(evaluation, Hash('1'), IndependentMatchLabel.Match, Hash('a')) with
        {
            CandidateId = Id(9999)
        };
        Assert.That(() => CandidateReferenceAgreementDiagnostic.Analyze(
                validated, AgreementEvidence(validated, [unknown])),
            Throws.TypeOf<InvalidOperationException>());

        var duplicateUnit = new[]
        {
            Judgment(evaluation, Hash('1'), IndependentMatchLabel.Match, Hash('a')),
            Judgment(evaluation, Hash('1'), IndependentMatchLabel.NonMatch, Hash('b'))
        };
        Assert.That(() => CandidateReferenceAgreementDiagnostic.Analyze(
                validated, AgreementEvidence(validated, duplicateUnit)),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task ProvenanceBindingAndAttestationFailClosed()
    {
        var capture = await CaptureAsync();
        var validated = CandidateLabeling.Validate(capture, Corpus(capture));
        var row = validated.Observations.First(static item => item.Partition == CandidateCorpusPartition.Evaluation);
        var evidence = AgreementEvidence(validated,
            [Judgment(row, Hash('1'), IndependentMatchLabel.Match, Hash('a'))]);

        Assert.That(() => CandidateReferenceAgreementDiagnostic.Analyze(
                validated, evidence with { FrameFingerprint = Hash('b') }),
            Throws.TypeOf<ArgumentException>());
        Assert.That(() => CandidateReferenceAgreementDiagnostic.Analyze(
                validated, evidence with { LabelingReference = "OTHER_LABELS" }),
            Throws.TypeOf<ArgumentException>());
        Assert.That(() => CandidateReferenceAgreementDiagnostic.Analyze(
                validated, evidence with { AttestedAt = validated.Manifest.AttestedAt.AddTicks(-1) }),
            Throws.TypeOf<ArgumentException>());
        Assert.That(() => CandidateReferenceAgreementDiagnostic.Analyze(
                validated, evidence with { Judgments = evidence.Judgments.Select(judgment =>
                    judgment with { EvidenceFingerprint = Hash('z') }).ToArray() }),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task ReportIsOrderIndependentAndDefinesNoApprovalOrAdjudicationOutput()
    {
        var capture = await CaptureAsync();
        var validated = CandidateLabeling.Validate(capture, Corpus(capture));
        var rows = validated.Observations
            .Where(static row => row.Partition == CandidateCorpusPartition.Evaluation)
            .Take(2)
            .ToArray();
        var judgments = new[]
        {
            Judgment(rows[0], Hash('1'), IndependentMatchLabel.Match, Hash('a')),
            Judgment(rows[0], Hash('2'), IndependentMatchLabel.Match, Hash('b')),
            Judgment(rows[1], Hash('1'), IndependentMatchLabel.NonMatch, Hash('c')),
            Judgment(rows[1], Hash('2'), IndependentMatchLabel.Match, Hash('d'))
        };

        var first = CandidateReferenceAgreementDiagnostic.Analyze(
            validated, AgreementEvidence(validated, judgments));
        var second = CandidateReferenceAgreementDiagnostic.Analyze(
            validated, AgreementEvidence(validated, judgments.Reverse().ToArray()));

        Assert.Multiple(() =>
        {
            Assert.That(second.FingerprintSha256, Is.EqualTo(first.FingerprintSha256));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(second),
                Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(first)));
            Assert.That(typeof(CandidateReferenceAgreementReport).GetProperties().Any(property =>
                property.Name.Contains("Threshold", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Approved", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Adjudicated", StringComparison.OrdinalIgnoreCase)), Is.False);
        });
    }

    private static CandidateReferenceJudgment Judgment(
        ValidatedCandidateObservation row,
        string unit,
        IndependentMatchLabel decision,
        string evidenceHash) => new(
            row.Sample.SourceId,
            row.Sample.CandidateId,
            unit,
            decision,
            IndependentLabelMethod.IndependentAdjudication,
            "SYNTHETIC_REPLICATE",
            evidenceHash,
            Captured.AddMinutes(1));

    private static CandidateReferenceAgreementEvidence AgreementEvidence(
        ValidatedCandidateCorpus corpus,
        IReadOnlyList<CandidateReferenceJudgment> judgments) => new(
            "REFERENCE_AGREEMENT_SYNTHETIC",
            corpus.Manifest.Reference,
            corpus.Capture.FrameFingerprint,
            corpus.Capture.SelectionFingerprint,
            Captured.AddMinutes(2),
            judgments);

    private static async Task<CandidateSamplingCapture> CaptureAsync()
    {
        var sources = Sources.Select(id =>
            new CandidateUniverseSource(id, new DateOnly(1982, 4, 10), "Maria", "Ana")).ToArray();
        return await CandidateSamplingEngine.CaptureAsync(
            new CandidateSamplingFrame("REFERENCE_AGREEMENT_FRAME", true, sources),
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
            Id(1000 + index),
            index < 4 ? CandidateCorpusPartition.Training : CandidateCorpusPartition.Evaluation)).ToArray();
        var labels = capture.Pairs.Select(pair => new CandidateIndependentLabel(
            pair.SourceId,
            pair.CandidateId,
            pair.PrimaryPass == BirthBlockingPass.ExactDate
                ? IndependentMatchLabel.Match
                : IndependentMatchLabel.NonMatch,
            IndependentLabelMethod.GovernedReference,
            "SYNTHETIC_TRUTH",
            LabelHash,
            Captured)).ToArray();
        var evidence = capture.Pairs.Select(pair => new CandidateComparisonEvidence(
            pair.SourceId,
            pair.CandidateId,
            new CandidateComparisonVector(
                pair.PrimaryPass == BirthBlockingPass.ExactDate ? NameComparisonState.EXACT : NameComparisonState.LOW,
                pair.PrimaryPass == BirthBlockingPass.ExactDate ? NameComparisonState.EXACT : NameComparisonState.LOW,
                pair.PrimaryPass == BirthBlockingPass.ExactDate ? (byte)7 : (byte)3))).ToArray();
        var manifest = new CandidateLabelingManifest(
            "REFERENCE_AGREEMENT_LABELS",
            CandidateLabeling.PolicyVersion,
            capture.FrameFingerprint,
            capture.SelectionFingerprint,
            capture.DesignVersion,
            capture.BlockingVersion,
            IdentityComparison.NormalizationVersion,
            CandidateLabeling.FeatureVersion,
            "REFERENCE_AGREEMENT_PARTITION",
            true,
            Captured);
        return new CandidateLabeledCorpus(manifest, partitions, labels, evidence);
    }
}
