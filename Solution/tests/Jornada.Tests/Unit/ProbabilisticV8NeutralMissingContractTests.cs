using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class ProbabilisticV8NeutralMissingContractTests
{
    private static readonly Guid ModelId = Guid.Parse("88000000-0000-4000-8000-000000000008");

    [Test]
    public void V8_requires_explicit_neutral_missing_provenance()
    {
        var p = Parameters();
        p.Remove(LinkageParameterCatalog.NeutralMissingEvidenceScoring);
        Assert.That(
            () => LinkageModelPolicy.Create(ModelId, 8,
                LinkageParameterCatalog.NeutralMissingDecisionEvidenceAlgorithmVersion, p),
            Throws.InvalidOperationException.With.Message.Contains(
                LinkageParameterCatalog.NeutralMissingEvidenceScoring));
    }

    [Test]
    public void V8_rejects_v6_missing_probabilities()
    {
        var p = Parameters();
        p["M_NOME_MAE_MISSING"] = .2m;
        Assert.That(
            () => LinkageModelPolicy.Create(ModelId, 8,
                LinkageParameterCatalog.NeutralMissingDecisionEvidenceAlgorithmVersion, p),
            Throws.InvalidOperationException.With.Message.Contains("V8 não admite probabilidades"));
    }

    [Test]
    public void V8_valid_model_scores_missing_mother_without_evidence()
    {
        var p = Parameters();
        var model = LinkageModelPolicy.Create(ModelId, 8,
            LinkageParameterCatalog.NeutralMissingDecisionEvidenceAlgorithmVersion, p);
        var birth = new DateOnly(1980, 5, 6);
        var obs = new IdentityObservation(null, "SEM_CPF", "MARIA SILVA", birth, null);
        var cand = new LinkageCandidate(Guid.NewGuid(), "MARIA SILVA", birth, "ANA SILVA");
        var decision = ProbabilisticLinkageDecisions.Resolve(model, obs, [cand]);
        var expected = FellegiSunterScoring.Calculate(p, NameComparisonState.EXACT, null, 1, birth, birth);
        Assert.That(decision.MelhorScore, Is.EqualTo(expected.Posterior));
    }

    [Test]
    public void V8_maternal_absence_is_neutral_for_unilateral_and_bilateral_missingness()
    {
        var p = Parameters();
        var model = LinkageModelPolicy.Create(ModelId, 8,
            LinkageParameterCatalog.NeutralMissingDecisionEvidenceAlgorithmVersion, p);
        var birth = new DateOnly(1980, 5, 6);
        var observation = new IdentityObservation(null, "SEM_CPF", "MARIA SILVA", birth, null);
        var unilateral = new LinkageCandidate(Guid.NewGuid(), "MARIA SILVA", birth, "ANA SILVA");
        var bilateral = new LinkageCandidate(Guid.NewGuid(), "MARIA SILVA", birth, null);
        var expected = FellegiSunterScoring.Calculate(p, NameComparisonState.EXACT, null, 1, birth, birth);

        var oneSided = ProbabilisticLinkageDecisions.Resolve(model, observation, [unilateral]);
        var bothMissing = ProbabilisticLinkageDecisions.Resolve(model, observation, [bilateral]);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(oneSided.MelhorScore, Is.EqualTo(expected.Posterior));
            Assert.That(bothMissing.MelhorScore, Is.EqualTo(expected.Posterior));
            Assert.That(bothMissing.MelhorScore, Is.EqualTo(oneSided.MelhorScore));
        }));
    }

    private static Dictionary<string, decimal> Parameters()
    {
        var p = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .01m,
            [LinkageParameterCatalog.PriorBlockMin] = .000001m,
            [LinkageParameterCatalog.PriorBlockMax] = .25m,
            [LinkageParameterCatalog.Threshold] = .95m,
            [LinkageParameterCatalog.ConflictMargin] = .03m,
            [LinkageParameterCatalog.LogOddsConflictMargin] = .03m,
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.NeutralMissingEvidenceScoring] = 1m,
            [LinkageParameterCatalog.BirthSemanticEvidenceScoring] = 1m
        };
        foreach (var state in LinkageParameterCatalog.NameStates)
        {
            p["M_NOME_" + state] = .25m;
            p["U_NOME_" + state] = .25m;
            p["M_NOME_MAE_" + state] = .25m;
            p["U_NOME_MAE_" + state] = .25m;
        }
        foreach (var state in BirthDateSemanticEvidence.States)
        {
            p["M_NASCIMENTO_SEMANTICO_" + state] = .5m;
            p["U_NASCIMENTO_SEMANTICO_" + state] = .5m;
        }
        return p;
    }
}
