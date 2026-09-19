using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class BlockingCandidatePriorEstimatorTests
{
    [Test]
    public void AppendDiagnostics_preserves_active_prior_and_persists_candidate_pair_evidence()
    {
        var parameters = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = 0.25m,
            ["KEEP"] = 7m
        };
        var estimate = new BlockingCandidatePriorEstimate(
            ObservationSampleSize: 100,
            ObservationsWithCandidates: 95,
            TruthCandidatePairs: 90,
            FalseCandidatePairs: 810,
            TotalCandidatePairs: 900,
            CandidateRecall: 0.90m,
            MatchProbability: 0.10m);

        var result = BlockingCandidatePriorEstimator.AppendDiagnostics(parameters, estimate);

        Assert.Multiple(() =>
        {
            Assert.That(result[LinkageParameterCatalog.PriorMatchProbability], Is.EqualTo(0.25m));
            Assert.That(result["KEEP"], Is.EqualTo(7m));
            Assert.That(result[LinkageParameterCatalog.CandidatePairPriorDiagnosticV1], Is.EqualTo(1m));
            Assert.That(result["DIAG_CANDIDATE_PRIOR_MATCH_PROBABILITY"], Is.EqualTo(0.10m));
            Assert.That(result["DIAG_CANDIDATE_PRIOR_CANDIDATE_RECALL"], Is.EqualTo(0.90m));
            Assert.That(result["DIAG_CANDIDATE_PRIOR_MEAN_CANDIDATES_PER_OBSERVATION"], Is.EqualTo(9m));
            Assert.That(result["DIAG_CANDIDATE_PRIOR_BOTH_CLASSES_OBSERVED"], Is.EqualTo(1m));
            Assert.That(result["DIAG_CANDIDATE_PRIOR_ACTIVE_SCORE_CHANGED"], Is.EqualTo(0m));
            Assert.That(result["DIAG_CANDIDATE_PRIOR_VALIDATION_CANDIDATES_EXCLUDED"], Is.EqualTo(1m));
            Assert.That(result["DIAG_CANDIDATE_PRIOR_DELTA_LOG_ODDS_VS_ACTIVE"], Is.LessThan(0m));
        });
    }

    [Test]
    public void AppendDiagnostics_marks_unavailable_prior_without_inventing_probability()
    {
        var parameters = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = 0.25m
        };
        var estimate = new BlockingCandidatePriorEstimate(
            ObservationSampleSize: 40,
            ObservationsWithCandidates: 0,
            TruthCandidatePairs: 0,
            FalseCandidatePairs: 0,
            TotalCandidatePairs: 0,
            CandidateRecall: 0m,
            MatchProbability: null);

        var result = BlockingCandidatePriorEstimator.AppendDiagnostics(parameters, estimate);

        Assert.Multiple(() =>
        {
            Assert.That(result["DIAG_CANDIDATE_PRIOR_AVAILABLE"], Is.EqualTo(0m));
            Assert.That(result.ContainsKey("DIAG_CANDIDATE_PRIOR_MATCH_PROBABILITY"), Is.False);
            Assert.That(result["DIAG_CANDIDATE_PRIOR_BOTH_CLASSES_OBSERVED"], Is.EqualTo(0m));
            Assert.That(result[LinkageParameterCatalog.PriorMatchProbability], Is.EqualTo(0.25m));
        });
    }
}
