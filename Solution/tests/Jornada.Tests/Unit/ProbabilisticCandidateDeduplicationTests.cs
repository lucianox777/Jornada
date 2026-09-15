using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class ProbabilisticCandidateDeduplicationTests
{
    private static readonly Guid ModelId = Guid.Parse("8d000000-0000-4000-8000-000000000001");
    private static readonly Guid CandidateA = Guid.Parse("8d100000-0000-4000-8000-000000000001");
    private static readonly Guid CandidateB = Guid.Parse("8d100000-0000-4000-8000-000000000002");
    private static readonly DateOnly Birth = new(1982, 4, 10);

    [Test]
    public void Same_uuid_reached_by_multiple_blocking_paths_is_ranked_once()
    {
        var model = LinkageModelPolicy.Create(ModelId, 1, "FELLEGI_SUNTER_V1", Parameters());
        var observation = Observation();
        var duplicate = new LinkageCandidate(CandidateA, observation.NomeCompleto, Birth, observation.NomeMae);

        var decision = ProbabilisticLinkageDecisions.Resolve(model, observation, [duplicate, duplicate]);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
            Assert.That(decision.MelhorCandidatoUuid, Is.EqualTo(CandidateA));
            Assert.That(decision.SegundoCandidatoUuid, Is.Null);
            Assert.That(decision.SegundoScore, Is.Null);
            Assert.That(decision.Margem, Is.Null);
        });
    }

    [Test]
    public void Same_uuid_with_divergent_attributes_fails_closed()
    {
        var model = LinkageModelPolicy.Create(ModelId, 1, "FELLEGI_SUNTER_V1", Parameters());
        var observation = Observation();

        var error = Assert.Throws<InvalidOperationException>(() => ProbabilisticLinkageDecisions.Resolve(
            model,
            observation,
            [
                new LinkageCandidate(CandidateA, observation.NomeCompleto, Birth, observation.NomeMae),
                new LinkageCandidate(CandidateA, "Outro Nome", Birth, observation.NomeMae)
            ]));

        Assert.That(error!.Message, Does.Contain("atributos divergentes"));
    }

    [Test]
    public void Two_distinct_uuid_candidates_remain_two_distinct_rank_positions()
    {
        var model = LinkageModelPolicy.Create(ModelId, 1, "FELLEGI_SUNTER_V1", Parameters());
        var observation = Observation();

        var decision = ProbabilisticLinkageDecisions.Resolve(
            model,
            observation,
            [
                new LinkageCandidate(CandidateB, observation.NomeCompleto, Birth, observation.NomeMae),
                new LinkageCandidate(CandidateA, observation.NomeCompleto, Birth, observation.NomeMae)
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Status, Is.EqualTo(ResolutionStatus.CONFLITO));
            Assert.That(decision.MelhorCandidatoUuid, Is.EqualTo(CandidateA));
            Assert.That(decision.SegundoCandidatoUuid, Is.EqualTo(CandidateB));
            Assert.That(decision.MelhorCandidatoUuid, Is.Not.EqualTo(decision.SegundoCandidatoUuid));
        });
    }

    private static IdentityObservation Observation() =>
        new(null, "NAO_INFORMADO", "Maria da Silva", Birth, "Ana de Souza");

    private static Dictionary<string, decimal> Parameters()
    {
        var parameters = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .001m,
            [LinkageParameterCatalog.PriorBlockMin] = .000001m,
            [LinkageParameterCatalog.PriorBlockMax] = .25m,
            [LinkageParameterCatalog.Threshold] = .80m,
            [LinkageParameterCatalog.ConflictMargin] = .05m
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

        return parameters;
    }
}
