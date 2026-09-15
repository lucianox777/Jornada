using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class LegacySemanticBirthProvenanceTests
{
    private static readonly Guid ModelId = Guid.Parse("8a000000-0000-4000-8000-000000000001");

    [Test]
    public void LegacyV5_with_complete_semantic_contract_remains_replayable()
    {
        var model = LinkageModelPolicy.Create(
            ModelId,
            5,
            LinkageParameterCatalog.LegacySemanticBirthAlgorithmVersion,
            LegacyV5Parameters());

        Assert.That(LinkageModelPolicy.SupportsSemanticBirthScoring(model), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LegacyV5_provenance_requires_enabled_semantic_scoring_flag(bool disabledInsteadOfMissing)
    {
        var parameters = LegacyV5Parameters();
        if (disabledInsteadOfMissing)
            parameters[LinkageParameterCatalog.BirthSemanticEvidenceScoring] = 0m;
        else
            parameters.Remove(LinkageParameterCatalog.BirthSemanticEvidenceScoring);

        var error = Assert.Throws<InvalidOperationException>(() => LinkageModelPolicy.Create(
            ModelId,
            5,
            LinkageParameterCatalog.LegacySemanticBirthAlgorithmVersion,
            parameters));

        Assert.That(error!.Message, Does.Contain(LinkageParameterCatalog.BirthSemanticEvidenceScoring));
    }

    private static Dictionary<string, decimal> LegacyV5Parameters()
    {
        var parameters = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .001m,
            [LinkageParameterCatalog.PriorBlockMin] = .000001m,
            [LinkageParameterCatalog.PriorBlockMax] = .25m,
            [LinkageParameterCatalog.Threshold] = .90m,
            [LinkageParameterCatalog.ConflictMargin] = .05m,
            [LinkageParameterCatalog.BirthSemanticEvidenceScoring] = 1m
        };

        foreach (var feature in new[] { "NOME", "NOME_MAE" })
        foreach (var (state, m, u) in new[]
                 {
                     ("EXACT", .90m, .01m),
                     ("HIGH", .05m, .04m),
                     ("MEDIUM", .03m, .10m),
                     ("LOW", .02m, .85m)
                 })
        {
            parameters[$"M_{feature}_{state}"] = m;
            parameters[$"U_{feature}_{state}"] = u;
        }

        foreach (var state in BirthDateSemanticEvidence.States)
        {
            parameters[$"M_NASCIMENTO_SEMANTICO_{state}"] = .5m;
            parameters[$"U_NASCIMENTO_SEMANTICO_{state}"] = .5m;
        }

        return parameters;
    }
}
