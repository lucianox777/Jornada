using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class LinkageDecisionEvidenceInvariantTests
{
    private static readonly Guid ModelId = Guid.Parse("87000000-0000-4000-8000-000000000001");
    private static readonly Guid ExactCandidate = Guid.Parse("88000000-0000-4000-8000-000000000001");
    private static readonly Guid HighCandidate = Guid.Parse("88000000-0000-4000-8000-000000000002");
    private static readonly DateOnly Birth = new(1985, 5, 12);

    [TestCase(false)]
    [TestCase(true)]
    public void Legacy_V5_provenance_still_requires_semantic_birth_flag(bool disabledInsteadOfMissing)
    {
        var parameters = V6Parameters();
        parameters.Remove(LinkageParameterCatalog.DecisionEvidenceScoring);
        parameters.Remove(LinkageParameterCatalog.LogOddsConflictMargin);
        parameters.Remove("M_NOME_MAE_MISSING");
        parameters.Remove("U_NOME_MAE_MISSING");

        if (disabledInsteadOfMissing)
            parameters[LinkageParameterCatalog.BirthSemanticEvidenceScoring] = 0m;
        else
            parameters.Remove(LinkageParameterCatalog.BirthSemanticEvidenceScoring);

        var error = Assert.Throws<InvalidOperationException>(() =>
            LinkageModelPolicy.Create(
                ModelId,
                5,
                LinkageParameterCatalog.LegacySemanticBirthAlgorithmVersion,
                parameters));

        Assert.That(error!.Message, Does.Contain(LinkageParameterCatalog.BirthSemanticEvidenceScoring));
    }

    [Test]
    public void V6_keeps_candidate_separation_after_posterior_saturation()
    {
        Assert.That(IdentityComparison.CompareName("Maria da Silva", "Maria da Silv"), Is.EqualTo(NameComparisonState.HIGH));
        var parameters = V6Parameters();
        var model = LinkageModelPolicy.Create(
            ModelId,
            6,
            LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
            parameters);
        var observation = new IdentityObservation(null, "NAO_INFORMADO", "Maria da Silva", Birth, "Ana de Souza");

        var decision = ProbabilisticLinkageDecisions.Resolve(model, observation,
        [
            new LinkageCandidate(HighCandidate, "Maria da Silv", Birth, observation.NomeMae),
            new LinkageCandidate(ExactCandidate, observation.NomeCompleto, Birth, observation.NomeMae)
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
            Assert.That(decision.PessoaUuidResolvido, Is.EqualTo(ExactCandidate));
            Assert.That(decision.MelhorScore, Is.EqualTo(1.00000000m));
            Assert.That(decision.SegundoScore, Is.EqualTo(1.00000000m));
            Assert.That(decision.Margem, Is.GreaterThan(10m),
                "A margem V6 deve preservar separação em log-odds mesmo quando ambos os posteriores saturam.");
        });
    }

    private static Dictionary<string, decimal> V6Parameters()
    {
        var parameters = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .001m,
            [LinkageParameterCatalog.PriorBlockMin] = .000001m,
            [LinkageParameterCatalog.PriorBlockMax] = .25m,
            [LinkageParameterCatalog.Threshold] = .90m,
            [LinkageParameterCatalog.ConflictMargin] = .03m,
            [LinkageParameterCatalog.LogOddsConflictMargin] = .50m,
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.BirthSemanticEvidenceScoring] = 1m,

            ["M_NOME_EXACT"] = .999999999m,
            ["U_NOME_EXACT"] = .000000001m,
            ["M_NOME_HIGH"] = .99m,
            ["U_NOME_HIGH"] = .01m,
            ["M_NOME_MEDIUM"] = .50m,
            ["U_NOME_MEDIUM"] = .50m,
            ["M_NOME_LOW"] = .01m,
            ["U_NOME_LOW"] = .99m,

            ["M_NOME_MAE_EXACT"] = .999999999m,
            ["U_NOME_MAE_EXACT"] = .000000001m,
            ["M_NOME_MAE_HIGH"] = .70m,
            ["U_NOME_MAE_HIGH"] = .20m,
            ["M_NOME_MAE_MEDIUM"] = .20m,
            ["U_NOME_MAE_MEDIUM"] = .30m,
            ["M_NOME_MAE_LOW"] = .10m,
            ["U_NOME_MAE_LOW"] = .50m,
            ["M_NOME_MAE_MISSING"] = .50m,
            ["U_NOME_MAE_MISSING"] = .50m
        };

        foreach (var state in BirthDateSemanticEvidence.States)
        {
            parameters[$"M_NASCIMENTO_SEMANTICO_{state}"] = state == BirthDateSemanticEvidence.Exact ? .90m : .50m;
            parameters[$"U_NASCIMENTO_SEMANTICO_{state}"] = state == BirthDateSemanticEvidence.Exact ? .01m : .50m;
        }

        return parameters;
    }
}
