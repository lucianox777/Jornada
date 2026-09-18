using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class ProbabilisticV7NameComparisonContractTests
{
    private static readonly Guid V6ModelId = Guid.Parse("76000000-0000-4000-8000-000000000006");
    private static readonly Guid V7ModelId = Guid.Parse("77000000-0000-4000-8000-000000000007");
    private static readonly Guid CandidateId = Guid.Parse("77000000-0000-4000-8000-000000000099");
    private static readonly DateOnly Birth = new(1980, 1, 1);

    [Test]
    public void V7_uses_ptbr_content_token_guard_while_v6_preserves_legacy_v1()
    {
        var v6 = LinkageModelPolicy.Create(
            V6ModelId,
            6,
            LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
            Parameters(includeV7Marker: false));
        var v7 = LinkageModelPolicy.Create(
            V7ModelId,
            7,
            LinkageParameterCatalog.NominalGuardDecisionEvidenceAlgorithmVersion,
            Parameters(includeV7Marker: true));

        var observation = new IdentityObservation(
            null,
            "SEM_CPF",
            "MARIA APARECIDA DA SILVA VALIDACAO UNICA",
            Birth,
            "ANA SOUZA");
        var candidate = new LinkageCandidate(
            CandidateId,
            "MARIA APARECIDA DA SOUZA VALIDACAO UNICA",
            Birth,
            "ANA SOUZA");

        var v6Decision = ProbabilisticLinkageDecisions.Resolve(v6, observation, [candidate]);
        var v7Decision = ProbabilisticLinkageDecisions.Resolve(v7, observation, [candidate]);

        Assert.Multiple(() =>
        {
            Assert.That(v6Decision.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
            Assert.That(v6Decision.PessoaUuidResolvido, Is.EqualTo(CandidateId));
            Assert.That(v7Decision.Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
            Assert.That(v7Decision.PessoaUuidResolvido, Is.Null);
            Assert.That(v7Decision.Motivo, Is.EqualTo("ABAIXO_T_LINKAGE"));
            Assert.That(v6Decision.MelhorScore, Is.GreaterThan(v7Decision.MelhorScore));
        });
    }

    [Test]
    public void V7_without_name_comparison_provenance_is_rejected_fail_closed()
    {
        Assert.That(
            () => LinkageModelPolicy.Create(
                V7ModelId,
                7,
                LinkageParameterCatalog.NominalGuardDecisionEvidenceAlgorithmVersion,
                Parameters(includeV7Marker: false)),
            Throws.InvalidOperationException.With.Message.Contains(
                LinkageParameterCatalog.NameComparisonPtBrContentTokenGuardV2));
    }

    [Test]
    public void Operational_default_algorithm_remains_v6_and_maps_to_legacy_name_contract()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                LinkageParameterCatalog.SemanticBirthAlgorithmVersion,
                Is.EqualTo(LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion));
            Assert.That(
                LinkageParameterCatalog.NameComparisonContractForAlgorithm(
                    LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion),
                Is.EqualTo(NameComparisonContract.WholeNameJaroWinklerV1));
            Assert.That(
                LinkageParameterCatalog.NameComparisonContractForAlgorithm(
                    LinkageParameterCatalog.NominalGuardDecisionEvidenceAlgorithmVersion),
                Is.EqualTo(NameComparisonContract.PtBrContentTokenGuardV2));
        });
    }

    private static Dictionary<string, decimal> Parameters(bool includeV7Marker)
    {
        var parameters = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .50m,
            [LinkageParameterCatalog.PriorBlockMin] = .000001m,
            [LinkageParameterCatalog.PriorBlockMax] = .25m,
            [LinkageParameterCatalog.Threshold] = .80m,
            [LinkageParameterCatalog.ConflictMargin] = .03m,
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.LogOddsConflictMargin] = .05m,
            [LinkageParameterCatalog.BirthSemanticEvidenceScoring] = 1m,
            ["M_NOME_MAE_MISSING"] = .10m,
            ["U_NOME_MAE_MISSING"] = .10m
        };

        if (includeV7Marker)
            parameters[LinkageParameterCatalog.NameComparisonPtBrContentTokenGuardV2] = 1m;

        foreach (var (state, m, u) in new[]
                 {
                     ("EXACT", .04m, .04m),
                     ("HIGH", .90m, .05m),
                     ("MEDIUM", .04m, .06m),
                     ("LOW", .02m, .85m)
                 })
        {
            parameters[$"M_NOME_{state}"] = m;
            parameters[$"U_NOME_{state}"] = u;
        }

        foreach (var state in LinkageParameterCatalog.NameStates)
        {
            parameters[$"M_NOME_MAE_{state}"] = .225m;
            parameters[$"U_NOME_MAE_{state}"] = .225m;
        }

        foreach (var state in BirthDateSemanticEvidence.States)
        {
            parameters[$"M_NASCIMENTO_SEMANTICO_{state}"] = .50m;
            parameters[$"U_NASCIMENTO_SEMANTICO_{state}"] = .50m;
        }

        return parameters;
    }
}
