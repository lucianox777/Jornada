using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public enum IndependentMatchLabel { Match, NonMatch, Inconclusive }
public enum CandidateCorpusPartition { Training, Evaluation }
public enum IndependentLabelMethod { GovernedReference, IndependentAdjudication }

/// <summary>Opaque, externally attested labeling provenance; never a score-derived label.</summary>
public sealed record CandidateLabelingManifest(
    string Reference, string PolicyVersion, string FrameFingerprint, string SelectionFingerprint,
    string DesignVersion, string BlockingVersion, string NormalizationVersion, string FeatureVersion,
    string PartitionReference, bool Complete, DateTimeOffset AttestedAt);

public sealed record CandidateSourcePartition(Guid SourceId, Guid IndependenceGroupId,
    CandidateCorpusPartition Partition);

public sealed record CandidateIndependentLabel(Guid SourceId, Guid CandidateId,
    IndependentMatchLabel Decision, IndependentLabelMethod Method, string EvidenceReference,
    string EvidenceFingerprint, DateTimeOffset DecidedAt);

/// <summary>Comparison states only. Missing evidence is not a disagreement.</summary>
public sealed record CandidateComparisonVector(NameComparisonState? Name, NameComparisonState? Mother,
    byte? BirthAgreementMask);

public sealed record CandidateEvidencePair(string? LeftName, DateOnly? LeftBirthDate,
    string? LeftMother, string? RightName, DateOnly? RightBirthDate, string? RightMother);

public sealed record CandidateComparisonEvidence(Guid SourceId, Guid CandidateId,
    CandidateComparisonVector Comparison);

/// <summary>Restricted, complete inventory of selected pairs. No raw PII is required by the estimator.</summary>
public sealed record CandidateLabeledCorpus(CandidateLabelingManifest Manifest,
    IReadOnlyList<CandidateSourcePartition> Partitions,
    IReadOnlyList<CandidateIndependentLabel> Labels,
    IReadOnlyList<CandidateComparisonEvidence> Evidence);

public sealed record ValidatedCandidateObservation(CandidateSamplingPair Sample,
    IndependentMatchLabel Label, CandidateCorpusPartition Partition, Guid IndependenceGroupId,
    CandidateComparisonVector Comparison);

public sealed class ValidatedCandidateCorpus
{
    internal ValidatedCandidateCorpus(CandidateSamplingCapture capture, CandidateLabelingManifest manifest,
        IReadOnlyList<ValidatedCandidateObservation> observations)
    {
        Capture = capture;
        Manifest = manifest;
        Observations = observations;
    }

    public CandidateSamplingCapture Capture { get; }
    public CandidateLabelingManifest Manifest { get; }
    public IReadOnlyList<ValidatedCandidateObservation> Observations { get; }
}

public static class CandidateLabeling
{
    public const string Version = "INDEPENDENT_CANDIDATE_LABELS_V1";
    public const string FeatureVersion = "IDENTITY_COMPARISON_JOINT_BIRTH_V1";
    public const string PolicyVersion = "INDEPENDENT_LABEL_POLICY_V1";

    public static CandidateComparisonVector Compare(CandidateEvidencePair pair)
    {
        ArgumentNullException.ThrowIfNull(pair);
        static NameComparisonState? CompareName(string? left, string? right) =>
            IdentityComparison.NormalizeText(left) is null || IdentityComparison.NormalizeText(right) is null
                ? null : IdentityComparison.CompareName(left, right);
        byte? birth = null;
        if (pair.LeftBirthDate is { } left && pair.RightBirthDate is { } right)
        {
            var mask = (left.Day == right.Day ? 1 : 0) |
                (left.Month == right.Month ? 2 : 0) | (left.Year == right.Year ? 4 : 0);
            birth = (byte)mask;
        }
        return new CandidateComparisonVector(CompareName(pair.LeftName, pair.RightName),
            CompareName(pair.LeftMother, pair.RightMother), birth);
    }

    public static ValidatedCandidateCorpus Validate(CandidateSamplingCapture capture, CandidateLabeledCorpus corpus)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(corpus);
        var manifest = corpus.Manifest ?? throw new ArgumentException("Manifesto ausente.", nameof(corpus));
        if (!manifest.Complete || string.IsNullOrWhiteSpace(manifest.Reference) ||
            manifest.PolicyVersion != PolicyVersion || string.IsNullOrWhiteSpace(manifest.PartitionReference) ||
            manifest.AttestedAt == default || manifest.AttestedAt < capture.CapturedAt || manifest.DesignVersion != CandidateSamplingDesign.Version ||
            manifest.DesignVersion != capture.DesignVersion || manifest.BlockingVersion != BirthBlockingPlan.Version ||
            manifest.BlockingVersion != capture.BlockingVersion ||
            manifest.NormalizationVersion != IdentityComparison.NormalizationVersion ||
            manifest.FeatureVersion != FeatureVersion || manifest.FrameFingerprint != capture.FrameFingerprint ||
            manifest.SelectionFingerprint != capture.SelectionFingerprint ||
            !Hash(capture.FrameFingerprint) || !Hash(capture.SelectionFingerprint) ||
            !Hash(capture.ConfigurationFingerprint) || !Hash(capture.UniverseFingerprint) ||
            !Hash(capture.SeedCommitment) || string.IsNullOrWhiteSpace(capture.SnapshotToken) ||
            string.IsNullOrWhiteSpace(capture.FrameReference) || capture.CapturedAt == default ||
            capture.FrameSize < 1 || capture.SelectedSources < 1 || capture.SelectedSources > capture.FrameSize ||
            capture.Pairs is null || capture.PassCounts is null || corpus.Partitions is null ||
            corpus.Labels is null || corpus.Evidence is null)
            throw new ArgumentException("Manifesto ou captura incompatível com o desenho congelado.", nameof(corpus));

        var samples = new Dictionary<(Guid, Guid), CandidateSamplingPair>();
        var strata = new Dictionary<(Guid, BirthBlockingPass), (long Population, int SampleSize)>();
        foreach (var pair in capture.Pairs)
        {
            if (pair is null || pair.SourceId == Guid.Empty || pair.CandidateId == Guid.Empty ||
                pair.Membership == BirthBlockingPass.None || (((int)pair.Membership) & ~31) != 0 ||
                pair.PrimaryPass != BirthBlockingPlan.PrimaryPass(pair.Membership))
                throw new InvalidOperationException("Par amostral inválido.");
            var (p, w) = CandidateSamplingDesign.Inclusion(capture.FrameSize, capture.SelectedSources,
                pair.StratumPopulation, pair.StratumSampleSize);
            if (pair.InclusionProbability != p || pair.DesignWeight != w ||
                !samples.TryAdd((pair.SourceId, pair.CandidateId), pair))
                throw new InvalidOperationException("Probabilidade, peso ou chave amostral inválidos.");
            var key = (pair.SourceId, pair.PrimaryPass);
            var size = (pair.StratumPopulation, pair.StratumSampleSize);
            if (strata.TryGetValue(key, out var previous) && previous != size)
                throw new InvalidOperationException("Denominadores inconsistentes no estrato.");
            strata[key] = size;
        }
        if (samples.Count == 0 || capture.Pairs.Select(p => p.SourceId).Distinct().Count() > capture.SelectedSources ||
            strata.Any(s => samples.Values.Count(p => p.SourceId == s.Key.Item1 && p.PrimaryPass == s.Key.Item2) != s.Value.SampleSize) ||
            capture.PassCounts.Count != BirthBlockingPlan.OrderedPasses.Count ||
            capture.EstimatedPairPopulation != samples.Values.Sum(p => p.DesignWeight) ||
            capture.PassCounts.Sum(p => p.Primary) != capture.EnumeratedPairs ||
            capture.PassCounts.Any(p => p.Members < p.Primary || p.Primary < p.Selected || p.Selected < 0) ||
            capture.OverlapCount < 0 || capture.OverlapCount > capture.EnumeratedPairs)
            throw new InvalidOperationException("Inventário ou totais amostrais inconsistentes.");
        for (var i = 0; i < capture.PassCounts.Count; i++)
        {
            var pass = BirthBlockingPlan.OrderedPasses[i];
            var count = capture.PassCounts[i];
            if (count.Pass != pass || count.Selected != samples.Values.Count(p => p.PrimaryPass == pass) ||
                count.EstimatedPopulation != samples.Values.Where(p => p.PrimaryPass == pass).Sum(p => p.DesignWeight) ||
                count.EstimatedMembership != samples.Values.Where(p => (p.Membership & pass) != 0).Sum(p => p.DesignWeight))
                throw new InvalidOperationException("Totais por pass inconsistentes.");
        }

        var partitions = new Dictionary<Guid, CandidateSourcePartition>();
        var groups = new Dictionary<Guid, CandidateCorpusPartition>();
        foreach (var partition in corpus.Partitions)
        {
            if (partition is null || partition.SourceId == Guid.Empty || partition.IndependenceGroupId == Guid.Empty ||
                !Enum.IsDefined(partition.Partition) || !partitions.TryAdd(partition.SourceId, partition))
                throw new InvalidOperationException("Partição inválida ou duplicada.");
            if (groups.TryGetValue(partition.IndependenceGroupId, out var existing) && existing != partition.Partition)
                throw new InvalidOperationException("Grupo de independência atravessa treinamento e avaliação.");
            groups[partition.IndependenceGroupId] = partition.Partition;
        }
        if (partitions.Count > capture.SelectedSources ||
            !samples.Keys.Select(k => k.Item1).Distinct().All(partitions.ContainsKey) ||
            !partitions.Values.Any(p => p.Partition == CandidateCorpusPartition.Training) ||
            !partitions.Values.Any(p => p.Partition == CandidateCorpusPartition.Evaluation))
            throw new InvalidOperationException("Partições incompletas ou sem avaliação independente.");

        var labels = new Dictionary<(Guid, Guid), CandidateIndependentLabel>();
        foreach (var label in corpus.Labels)
        {
            if (label is null || !Enum.IsDefined(label.Decision) || !Enum.IsDefined(label.Method) ||
                label.DecidedAt == default || label.DecidedAt > manifest.AttestedAt || string.IsNullOrWhiteSpace(label.EvidenceReference) ||
                !Hash(label.EvidenceFingerprint) || !samples.ContainsKey((label.SourceId, label.CandidateId)) ||
                !labels.TryAdd((label.SourceId, label.CandidateId), label))
                throw new InvalidOperationException("Rótulo ausente, duplicado ou sem proveniência independente.");
        }
        var evidence = new Dictionary<(Guid, Guid), CandidateComparisonVector>();
        foreach (var row in corpus.Evidence)
        {
            if (row is null || row.Comparison is null ||
                (row.Comparison.Name is { } name && !Enum.IsDefined(name)) ||
                (row.Comparison.Mother is { } mother && !Enum.IsDefined(mother)) ||
                (row.Comparison.BirthAgreementMask is { } birth && birth > 7) ||
                !samples.ContainsKey((row.SourceId, row.CandidateId)) ||
                !evidence.TryAdd((row.SourceId, row.CandidateId), row.Comparison))
                throw new InvalidOperationException("Evidência de comparação inválida ou duplicada.");
        }
        if (labels.Count != samples.Count || evidence.Count != samples.Count)
            throw new InvalidOperationException("Todos os pares selecionados exigem rótulo e vetor de evidências.");
        var observations = samples.OrderBy(x => x.Key.Item1.ToString("D"), StringComparer.Ordinal)
            .ThenBy(x => x.Key.Item2.ToString("D"), StringComparer.Ordinal)
            .Select(x => new ValidatedCandidateObservation(x.Value, labels[x.Key].Decision,
                partitions[x.Key.Item1].Partition, partitions[x.Key.Item1].IndependenceGroupId, evidence[x.Key]))
            .ToArray();
        return new ValidatedCandidateCorpus(capture, manifest, Array.AsReadOnly(observations));
    }

    private static bool Hash(string? value) => value is { Length: 64 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
