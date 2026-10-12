using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Restricted, PII-bearing evidence for one selected candidate pair.
/// NEVER log, serialize to result DTOs, persist or export from this diagnostic
/// process. Labels MUST come from a separate independent source.
/// </summary>
public sealed record CandidateNominalV9RawEvidence(
    Guid SourceId,
    Guid CandidateId,
    CandidateEvidencePair Values);

/// <summary>
/// Recomputes the nominal person+mother levels with the frozen V9 comparator
/// from actual restricted pair evidence, instead of trusting arbitrary
/// caller-provided V9 enum values. This does not independently attest that
/// the raw values were captured from the correct source snapshots.
/// </summary>
public static class CandidateNominalV9EvidenceExtractor
{
    public const string Version = "V9_NOMINAL_RAW_EVIDENCE_RECOMPUTATION_1";

    public static IReadOnlyList<CandidateNominalV9LevelEvidence> Extract(
        ValidatedCandidateCorpus corpus,
        IReadOnlyList<CandidateNominalV9RawEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(evidence);
        if (corpus.Manifest is null || corpus.Capture is null ||
            corpus.Observations is null ||
            !corpus.Manifest.Complete ||
            corpus.Manifest.NormalizationVersion != IdentityComparison.NormalizationVersion ||
            corpus.Manifest.FeatureVersion != CandidateLabeling.FeatureVersion ||
            corpus.Manifest.DesignVersion != CandidateSamplingDesign.Version ||
            corpus.Capture.DesignVersion != CandidateSamplingDesign.Version ||
            corpus.Manifest.SelectionFingerprint != corpus.Capture.SelectionFingerprint ||
            corpus.Manifest.FrameFingerprint != corpus.Capture.FrameFingerprint)
            throw new InvalidOperationException("Corpus V9 não está vinculado ao contrato de amostragem/normalização.");

        // The exact selected pair inventory must be present once, including
        // the untouched EVALUATION partition, so no hard pairs can be hidden.
        var selected = new HashSet<(Guid Source, Guid Candidate)>();
        foreach (var row in corpus.Observations)
        {
            if (row is null || row.Sample is null ||
                row.Sample.SourceId == Guid.Empty || row.Sample.CandidateId == Guid.Empty ||
                !selected.Add((row.Sample.SourceId, row.Sample.CandidateId)))
                throw new InvalidOperationException("Captura V9 contém par inválido ou duplicado.");
        }
        if (selected.Count == 0 || evidence.Count != selected.Count)
            throw new InvalidOperationException("Extração V9 exige inventário completo da captura.");

        var derived = new Dictionary<(Guid Source, Guid Candidate), CandidateNominalV9LevelEvidence>();
        foreach (var entry in evidence)
        {
            if (entry is null || entry.Values is null ||
                entry.SourceId == Guid.Empty || entry.CandidateId == Guid.Empty ||
                !selected.Contains((entry.SourceId, entry.CandidateId)))
                throw new InvalidOperationException("Evidência V9 ausente ou fora do universo selecionado.");
            var key = (entry.SourceId, entry.CandidateId);
            if (derived.ContainsKey(key))
                throw new InvalidOperationException("Evidência V9 duplicada para par selecionado.");
            var person = CompositeNominalComparisonV9.Classify(
                entry.Values.LeftName, entry.Values.RightName);
            var mother = CompositeNominalComparisonV9.Classify(
                entry.Values.LeftMother, entry.Values.RightMother);
            derived.Add(key, new CandidateNominalV9LevelEvidence(
                entry.SourceId, entry.CandidateId, person, mother));
        }

        // Materialize in the capture's frozen order, not arbitrary upstream
        // enumeration order. No names/tokens or raw values in the output.
        return Array.AsReadOnly(corpus.Observations.Select(row =>
        {
            var key = (row.Sample.SourceId, row.Sample.CandidateId);
            return derived[key];
        }).ToArray());
    }
}
