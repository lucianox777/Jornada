using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Replicated reference judgment with an opaque independence-unit fingerprint.</summary>
public sealed record CandidateReferenceJudgment(
    Guid SourceId,
    Guid CandidateId,
    string IndependenceUnitFingerprint,
    IndependentMatchLabel Decision,
    IndependentLabelMethod Method,
    string EvidenceReference,
    string EvidenceFingerprint,
    DateTimeOffset DecidedAt);

/// <summary>Binds an agreement audit to the exact sampled frame, selection and labeling reference.</summary>
public sealed record CandidateReferenceAgreementEvidence(
    string Reference,
    string LabelingReference,
    string FrameFingerprint,
    string SelectionFingerprint,
    DateTimeOffset AttestedAt,
    IReadOnlyList<CandidateReferenceJudgment> Judgments);

public sealed record CandidateReferenceAgreementMethodCount(
    IndependentLabelMethod Method,
    int Judgments,
    int Pairs);

public sealed record CandidateReferenceAgreementReport(
    string Version,
    string Reference,
    string LabelingReference,
    string FrameFingerprint,
    string SelectionFingerprint,
    DateTimeOffset AttestedAt,
    int EvaluationPairs,
    int AssessedPairs,
    int ReplicatedPairs,
    int UnanimousPairs,
    int DiscordantPairs,
    int PairsWithInconclusiveJudgment,
    int DistinctIndependenceUnits,
    int DistinctEvidenceArtifacts,
    int PairwiseComparisons,
    int PairwiseAgreements,
    decimal? PairwiseAgreement,
    int ConclusivePairwiseComparisons,
    int ConclusivePairwiseAgreements,
    decimal? ConclusivePairwiseAgreement,
    decimal EvaluationDesignWeight,
    decimal AssessedDesignWeight,
    decimal ReplicatedDesignWeight,
    decimal AssessedWeightCoverage,
    decimal ReplicatedWeightCoverage,
    IReadOnlyList<CandidateReferenceAgreementMethodCount> MethodCounts,
    string FingerprintSha256);

/// <summary>
/// Read-only diagnostic for replicated reference judgments. It measures coverage and agreement but
/// never adjudicates labels, changes the labeled corpus or defines an approval threshold.
/// </summary>
public static class CandidateReferenceAgreementDiagnostic
{
    public const string Version = "CANDIDATE_REFERENCE_AGREEMENT_DIAGNOSTIC_V1";

    public static CandidateReferenceAgreementReport Analyze(
        ValidatedCandidateCorpus corpus,
        CandidateReferenceAgreementEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(evidence.Judgments);

        if (string.IsNullOrWhiteSpace(evidence.Reference) ||
            string.IsNullOrWhiteSpace(evidence.LabelingReference) ||
            evidence.LabelingReference != corpus.Manifest.Reference ||
            evidence.FrameFingerprint != corpus.Capture.FrameFingerprint ||
            evidence.SelectionFingerprint != corpus.Capture.SelectionFingerprint ||
            !Hash(evidence.FrameFingerprint) ||
            !Hash(evidence.SelectionFingerprint) ||
            evidence.AttestedAt == default ||
            evidence.AttestedAt < corpus.Manifest.AttestedAt)
        {
            throw new ArgumentException(
                "Evidência de concordância não está vinculada ao corpus/rotulagem atestados.",
                nameof(evidence));
        }

        var evaluation = corpus.Observations
            .Where(static row => row.Partition == CandidateCorpusPartition.Evaluation)
            .ToDictionary(
                static row => (row.Sample.SourceId, row.Sample.CandidateId),
                static row => row);
        if (evaluation.Count == 0)
            throw new InvalidOperationException("A análise de concordância exige partição Evaluation.");
        if (evidence.Judgments.Count == 0)
            throw new InvalidOperationException("A análise de concordância exige ao menos um julgamento.");

        var judgments = new Dictionary<(Guid SourceId, Guid CandidateId, string Unit), CandidateReferenceJudgment>();
        foreach (var judgment in evidence.Judgments)
        {
            if (judgment is null ||
                judgment.SourceId == Guid.Empty ||
                judgment.CandidateId == Guid.Empty ||
                !evaluation.ContainsKey((judgment.SourceId, judgment.CandidateId)) ||
                !Hash(judgment.IndependenceUnitFingerprint) ||
                !Enum.IsDefined(judgment.Decision) ||
                !Enum.IsDefined(judgment.Method) ||
                string.IsNullOrWhiteSpace(judgment.EvidenceReference) ||
                !Hash(judgment.EvidenceFingerprint) ||
                judgment.DecidedAt == default ||
                judgment.DecidedAt > evidence.AttestedAt ||
                !judgments.TryAdd(
                    (judgment.SourceId, judgment.CandidateId, judgment.IndependenceUnitFingerprint),
                    judgment))
            {
                throw new InvalidOperationException(
                    "Julgamento inválido, fora de Evaluation ou duplicado pela mesma unidade independente.");
            }
        }

        var byPair = judgments.Values
            .GroupBy(static judgment => (judgment.SourceId, judgment.CandidateId))
            .ToDictionary(static group => group.Key, static group => group.ToArray());

        var replicatedPairs = 0;
        var unanimousPairs = 0;
        var discordantPairs = 0;
        var pairsWithInconclusive = 0;
        var pairwiseComparisons = 0;
        var pairwiseAgreements = 0;
        var conclusivePairwiseComparisons = 0;
        var conclusivePairwiseAgreements = 0;
        decimal assessedDesignWeight = 0m;
        decimal replicatedDesignWeight = 0m;

        foreach (var (key, pairJudgments) in byPair)
        {
            var weight = evaluation[key].Sample.DesignWeight;
            assessedDesignWeight += weight;
            if (pairJudgments.Any(static judgment => judgment.Decision == IndependentMatchLabel.Inconclusive))
                pairsWithInconclusive++;

            if (pairJudgments.Length < 2)
                continue;

            replicatedPairs++;
            replicatedDesignWeight += weight;
            if (pairJudgments.Select(static judgment => judgment.Decision).Distinct().Count() == 1)
                unanimousPairs++;
            else
                discordantPairs++;

            for (var left = 0; left < pairJudgments.Length; left++)
            for (var right = left + 1; right < pairJudgments.Length; right++)
            {
                pairwiseComparisons++;
                if (pairJudgments[left].Decision == pairJudgments[right].Decision)
                    pairwiseAgreements++;

                if (pairJudgments[left].Decision == IndependentMatchLabel.Inconclusive ||
                    pairJudgments[right].Decision == IndependentMatchLabel.Inconclusive)
                    continue;

                conclusivePairwiseComparisons++;
                if (pairJudgments[left].Decision == pairJudgments[right].Decision)
                    conclusivePairwiseAgreements++;
            }
        }

        var evaluationDesignWeight = evaluation.Values.Sum(static row => row.Sample.DesignWeight);
        if (evaluationDesignWeight <= 0m)
            throw new InvalidOperationException("Peso de desenho de Evaluation inválido.");

        var methodCounts = judgments.Values
            .GroupBy(static judgment => judgment.Method)
            .OrderBy(static group => group.Key)
            .Select(static group => new CandidateReferenceAgreementMethodCount(
                group.Key,
                group.Count(),
                group.Select(judgment => (judgment.SourceId, judgment.CandidateId)).Distinct().Count()))
            .ToArray();

        var report = new CandidateReferenceAgreementReport(
            Version,
            evidence.Reference,
            evidence.LabelingReference,
            evidence.FrameFingerprint,
            evidence.SelectionFingerprint,
            evidence.AttestedAt,
            evaluation.Count,
            byPair.Count,
            replicatedPairs,
            unanimousPairs,
            discordantPairs,
            pairsWithInconclusive,
            judgments.Keys.Select(static key => key.Unit).Distinct(StringComparer.Ordinal).Count(),
            judgments.Values.Select(static judgment => judgment.EvidenceFingerprint).Distinct(StringComparer.Ordinal).Count(),
            pairwiseComparisons,
            pairwiseAgreements,
            Ratio(pairwiseAgreements, pairwiseComparisons),
            conclusivePairwiseComparisons,
            conclusivePairwiseAgreements,
            Ratio(conclusivePairwiseAgreements, conclusivePairwiseComparisons),
            evaluationDesignWeight,
            assessedDesignWeight,
            replicatedDesignWeight,
            assessedDesignWeight / evaluationDesignWeight,
            replicatedDesignWeight / evaluationDesignWeight,
            Array.AsReadOnly(methodCounts),
            string.Empty);

        return report with { FingerprintSha256 = Fingerprint(report, judgments.Values) };
    }

    private static decimal? Ratio(int numerator, int denominator) =>
        denominator == 0 ? null : (decimal)numerator / denominator;

    private static string Fingerprint(
        CandidateReferenceAgreementReport report,
        IEnumerable<CandidateReferenceJudgment> judgments)
    {
        var builder = new StringBuilder();
        Append(builder, report.Version);
        Append(builder, report.Reference);
        Append(builder, report.LabelingReference);
        Append(builder, report.FrameFingerprint);
        Append(builder, report.SelectionFingerprint);
        Append(builder, report.AttestedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(builder, report.EvaluationPairs.ToString(CultureInfo.InvariantCulture));
        Append(builder, report.AssessedPairs.ToString(CultureInfo.InvariantCulture));
        Append(builder, report.ReplicatedPairs.ToString(CultureInfo.InvariantCulture));
        Append(builder, report.UnanimousPairs.ToString(CultureInfo.InvariantCulture));
        Append(builder, report.DiscordantPairs.ToString(CultureInfo.InvariantCulture));
        Append(builder, report.PairsWithInconclusiveJudgment.ToString(CultureInfo.InvariantCulture));
        Append(builder, report.DistinctIndependenceUnits.ToString(CultureInfo.InvariantCulture));
        Append(builder, report.DistinctEvidenceArtifacts.ToString(CultureInfo.InvariantCulture));
        Append(builder, report.PairwiseComparisons.ToString(CultureInfo.InvariantCulture));
        Append(builder, report.PairwiseAgreements.ToString(CultureInfo.InvariantCulture));
        Append(builder, Decimal(report.PairwiseAgreement));
        Append(builder, report.ConclusivePairwiseComparisons.ToString(CultureInfo.InvariantCulture));
        Append(builder, report.ConclusivePairwiseAgreements.ToString(CultureInfo.InvariantCulture));
        Append(builder, Decimal(report.ConclusivePairwiseAgreement));
        Append(builder, report.EvaluationDesignWeight.ToString("G29", CultureInfo.InvariantCulture));
        Append(builder, report.AssessedDesignWeight.ToString("G29", CultureInfo.InvariantCulture));
        Append(builder, report.ReplicatedDesignWeight.ToString("G29", CultureInfo.InvariantCulture));
        Append(builder, report.AssessedWeightCoverage.ToString("G29", CultureInfo.InvariantCulture));
        Append(builder, report.ReplicatedWeightCoverage.ToString("G29", CultureInfo.InvariantCulture));

        foreach (var method in report.MethodCounts)
        {
            Append(builder, method.Method.ToString());
            Append(builder, method.Judgments.ToString(CultureInfo.InvariantCulture));
            Append(builder, method.Pairs.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var judgment in judgments
            .OrderBy(static judgment => judgment.SourceId.ToString("D"), StringComparer.Ordinal)
            .ThenBy(static judgment => judgment.CandidateId.ToString("D"), StringComparer.Ordinal)
            .ThenBy(static judgment => judgment.IndependenceUnitFingerprint, StringComparer.Ordinal)
            .ThenBy(static judgment => judgment.Decision)
            .ThenBy(static judgment => judgment.Method)
            .ThenBy(static judgment => judgment.EvidenceReference, StringComparer.Ordinal)
            .ThenBy(static judgment => judgment.EvidenceFingerprint, StringComparer.Ordinal)
            .ThenBy(static judgment => judgment.DecidedAt))
        {
            Append(builder, judgment.SourceId.ToString("D"));
            Append(builder, judgment.CandidateId.ToString("D"));
            Append(builder, judgment.IndependenceUnitFingerprint);
            Append(builder, judgment.Decision.ToString());
            Append(builder, judgment.Method.ToString());
            Append(builder, judgment.EvidenceReference);
            Append(builder, judgment.EvidenceFingerprint);
            Append(builder, judgment.DecidedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append('\n');

    private static string Decimal(decimal? value) =>
        value is null ? "-" : value.Value.ToString("G29", CultureInfo.InvariantCulture);

    private static bool Hash(string? value) => value is { Length: 64 } &&
        value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
