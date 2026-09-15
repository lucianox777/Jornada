using Jornada.Contracts;
using Jornada.Linkage.Runner;

internal static class OperationalCandidateRankingEvaluation
{
    public static async Task<object> EvaluateAsync(
        IReadOnlyList<EvaluationLabel> labels,
        SqlProbabilisticIdentityLinkage linkage,
        ProbabilisticLinkageModelRef model,
        CancellationToken ct)
    {
        var rows = new List<ProbabilisticCandidateRankingAudit>(labels.Count);
        foreach (var label in labels)
        {
            rows.Add(await linkage.DiagnoseCandidateRankingAsync(
                label.ObservationId,
                label.TruthPersonUuid,
                model.ModelId,
                ct));
        }

        var candidateCounts = rows.Select(static row => row.CandidateCount).OrderBy(static count => count).ToArray();
        var truthInside = rows.Count(static row => row.TruthInCandidateSet);
        var truthAbsent = rows.Count - truthInside;
        var deterministicTop1 = rows.Count(static row => row.TruthDeterministicRank == 1);
        var deterministicTop2 = rows.Count(static row => row.TruthInTop2);
        var deterministicRankGt2 = rows.Count(static row => row.TruthDeterministicRank > 2);
        var evidenceTop = rows.Count(static row => row.TruthEvidenceRank == 1);
        var evidenceRankGt2 = rows.Count(static row => row.TruthEvidenceRank > 2);
        var tiedAtBestEvidence = rows.Count(static row => row.TruthEvidenceRank == 1 && row.TruthTieCount > 1);

        var nonTop2 = rows
            .Where(static row => !row.TruthInCandidateSet || row.TruthDeterministicRank > 2)
            .Select(static row => new
            {
                pessoaObservacaoId = row.ObservationId,
                truthUuid = row.TruthPersonUuid,
                truthInCandidateSet = row.TruthInCandidateSet,
                candidateCount = row.CandidateCount,
                truthDeterministicRank = row.TruthDeterministicRank,
                truthEvidenceRank = row.TruthEvidenceRank,
                truthTieCount = row.TruthTieCount,
                truthPosterior = row.TruthPosterior,
                truthLogOdds = row.TruthLogOdds,
                topCandidateUuid = row.TopCandidateUuid,
                topPosterior = row.TopPosterior,
                topLogOdds = row.TopLogOdds,
                rankingGapToTop = row.RankingGapToTop,
                rankingSpace = row.RankingSpace
            })
            .ToArray();

        return new
        {
            model = new
            {
                modelId = model.ModelId,
                modelVersion = model.Version,
                algorithmVersion = model.AlgorithmVersion,
                blockingRuleSetVersion = model.BlockingContract?.RuleSetVersion,
                blockingRuleSetFingerprint = model.BlockingContract?.FingerprintSha256,
                projectionSchemaVersion = model.BlockingContract?.ProjectionSchemaVersion,
                projectionFingerprint = model.BlockingContract?.ProjectionFingerprintSha256
            },
            summary = new
            {
                sampleSize = rows.Count,
                truthInsideCandidateSet = truthInside,
                candidateRecallPct = Percentage(truthInside, rows.Count),
                truthAbsentFromCandidateSet = truthAbsent,
                truthDeterministicTop1 = deterministicTop1,
                truthDeterministicTop2 = deterministicTop2,
                truthPresentDeterministicRankGreaterThan2 = deterministicRankGt2,
                truthEvidenceTop = evidenceTop,
                truthPresentEvidenceRankGreaterThan2 = evidenceRankGt2,
                truthTiedAtBestEvidence = tiedAtBestEvidence,
                meanCandidateCount = rows.Count == 0 ? 0m : rows.Average(static row => (decimal)row.CandidateCount),
                p95CandidateCount = Percentile(candidateCounts, 0.95),
                maxCandidateCount = candidateCounts.Length == 0 ? 0 : candidateCounts[^1]
            },
            nonTop2,
            interpretation = new
            {
                deterministicRank = "Ordena pela evidência do modelo e usa UUID apenas para representação determinística de empates.",
                evidenceRank = "1 + quantidade de candidatos com evidência estritamente superior. Empates não pioram o rank evidencial.",
                candidateRecall = "truthAbsentFromCandidateSet isola falha de blocking/candidate recall; verdade presente com evidenceRank>2 isola perda de ranking/scoring."
            }
        };
    }

    private static decimal Percentage(int numerator, int denominator) =>
        denominator == 0 ? 0m : decimal.Round(100m * numerator / denominator, 4);

    private static int Percentile(int[] sorted, double probability)
    {
        if (sorted.Length == 0) return 0;
        var index = (int)Math.Ceiling(probability * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
