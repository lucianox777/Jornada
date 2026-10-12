using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class ProbabilisticV9OperationalRoutingGuardTests
{
    private const string V9 = LinkageParameterCatalog.CompositeNominalDecisionEvidenceAlgorithmVersion;
    private static readonly Guid ModelId = Guid.Parse("90000000-0000-4000-8000-000000000009");

    [TestCase(V9)]
    [TestCase("V9")]
    [TestCase("EXPERIMENTAL_COMPOSITE_V9")]
    public void Create_refuses_uncertified_V9_before_v8_parameter_validation(string algorithm)
    {
        var error = Assert.Throws<InvalidOperationException>((Action)(() =>
            LinkageModelPolicy.Create(ModelId, 9, algorithm,
                new Dictionary<string, decimal>())));
        Assert.That(error!.Message, Does.Contain("V9 não habilitada"));
        Assert.That(error.Message, Does.Contain("fallback para V8 proibido"));
    }

    [Test]
    public void Public_contract_does_not_mark_historical_versions_as_V9()
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.That(LinkageParameterCatalog.IsCompositeNominalV9(V9), Is.True);
            Assert.That(LinkageParameterCatalog.IsCompositeNominalV9("V9"), Is.True);
            Assert.That(LinkageParameterCatalog.IsCompositeNominalV9(
                LinkageParameterCatalog.NeutralMissingDecisionEvidenceAlgorithmVersion), Is.False);
            Assert.That(LinkageParameterCatalog.IsCompositeNominalV9(
                LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion), Is.False);
        }));
    }

    [Test]
    public void Internal_model_cannot_bypass_V9_guard_even_with_empty_candidate_block_or_ranking()
    {
        // A model can be constructed directly by an internal caller without
        // LinkageModelPolicy.Create. Every operational entry must fail closed
        // even if there are no candidates to score.
        var model = new LinkageModel(ModelId, 9, V9,
            new Dictionary<string, decimal>(), .99m, .1m);
        var observation = new IdentityObservation(
            null, "SEM_CPF", "Maria Clara Silva", new DateOnly(1982, 4, 10), "Ana Costa");

        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() =>
                ProbabilisticLinkageDecisions.Rank(
                    model, observation, Array.Empty<LinkageCandidate>())),
                Throws.InvalidOperationException.With.Message.Contains("V9 não habilitada"));
            Assert.That((Action)(() =>
                ProbabilisticLinkageDecisions.Resolve(
                    model, observation, Array.Empty<LinkageCandidate>())),
                Throws.InvalidOperationException.With.Message.Contains("V9 não habilitada"));
            Assert.That((Action)(() =>
                ProbabilisticLinkageDecisions.ResolveRanked(
                    model, Array.Empty<CandidateScore>(), "SEM_CANDIDATO")),
                Throws.InvalidOperationException.With.Message.Contains("V9 não habilitada"));
        }));
    }

    [Test]
    public void Preview_does_not_use_operational_algorithm_version()
    {
        // This guard must not block the independent mathematical V9 preview.
        Assert.That(LinkageParameterCatalog.IsCompositeNominalV9(
            LinkageParameterCatalog.OperationalDecisionEvidenceAlgorithmVersion), Is.False);
    }
}
