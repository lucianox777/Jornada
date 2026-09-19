using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class FellegiSunterScoreBreakdownTests
{
    [Test]
    public void Breakdown_ReconstructsRuntimeLogOddsAndExposesV6EvidenceStates()
    {
        var parameters = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .01m,
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.BirthSemanticEvidenceScoring] = 1m,
            ["M_NOME_EXACT"] = .80m,
            ["U_NOME_EXACT"] = .10m,
            ["M_NOME_MAE_MISSING"] = .20m,
            ["U_NOME_MAE_MISSING"] = .40m,
            [$"M_NASCIMENTO_SEMANTICO_{BirthDateSemanticEvidence.DayMonthSwap}"] = .30m,
            [$"U_NASCIMENTO_SEMANTICO_{BirthDateSemanticEvidence.DayMonthSwap}"] = .05m
        };
        var leftBirth = new DateOnly(1980, 5, 6);
        var rightBirth = new DateOnly(1980, 6, 5);

        var breakdown = FellegiSunterScoring.CalculateWithBreakdown(
            parameters,
            NameComparisonState.EXACT,
            motherNameState: null,
            blockCandidateCount: 999,
            leftBirth,
            rightBirth);
        var runtime = FellegiSunterScoring.Calculate(
            parameters,
            NameComparisonState.EXACT,
            motherNameState: null,
            blockCandidateCount: 999,
            leftBirth,
            rightBirth);
        var reconstructed = Math.Round(
            breakdown.PriorLogOdds + breakdown.Contributions.Sum(static contribution => contribution.LogLikelihoodRatio),
            8,
            MidpointRounding.AwayFromZero);

        Assert.Multiple(() =>
        {
            Assert.That(breakdown.Score, Is.EqualTo(runtime));
            Assert.That(breakdown.PriorKind, Is.EqualTo("MODEL_PRIOR"), "V6 não deve reintroduzir prior dependente do tamanho do bloco.");
            Assert.That(reconstructed, Is.EqualTo(runtime.LogOdds));
            Assert.That(breakdown.Contributions.Any(c => c.Evidence == "NOME" && c.State == "EXACT"), Is.True);
            Assert.That(breakdown.Contributions.Any(c => c.Evidence == "NOME_MAE" && c.State == "MISSING" && c.MProbability == .20m && c.UProbability == .40m), Is.True);
            Assert.That(breakdown.Contributions.Any(c => c.Evidence == "NASCIMENTO_SEMANTICO" && c.State == BirthDateSemanticEvidence.DayMonthSwap), Is.True);
        });
    }

    [Test]
    public void Breakdown_MakesLegacyNeutralMissingEvidenceAndBlockPriorExplicit()
    {
        var parameters = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .01m,
            [LinkageParameterCatalog.PriorBlockMin] = .000001m,
            [LinkageParameterCatalog.PriorBlockMax] = .25m,
            ["M_NOME_EXACT"] = .90m,
            ["U_NOME_EXACT"] = .01m
        };

        var breakdown = FellegiSunterScoring.CalculateWithBreakdown(
            parameters,
            NameComparisonState.EXACT,
            motherNameState: null,
            blockCandidateCount: 10);

        Assert.Multiple(() =>
        {
            Assert.That(breakdown.PriorKind, Is.EqualTo("BLOCK_CANDIDATE_COUNT"));
            Assert.That(breakdown.PriorProbability, Is.EqualTo(.10m));
            Assert.That(breakdown.Contributions.Single(c => c.Evidence == "NOME_MAE").State, Is.EqualTo("MISSING_NEUTRAL"));
            Assert.That(breakdown.Contributions.Single(c => c.Evidence == "NOME_MAE").LogLikelihoodRatio, Is.Zero);
            Assert.That(breakdown.Contributions.Single(c => c.Evidence == "NASCIMENTO").State, Is.EqualTo("MISSING_NEUTRAL"));
        });
    }
}
