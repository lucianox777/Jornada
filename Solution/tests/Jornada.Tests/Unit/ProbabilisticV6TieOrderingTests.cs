using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class ProbabilisticV6TieOrderingTests
{
    private static readonly Guid ModelId = Guid.Parse("8e000000-0000-4000-8000-000000000001");
    private static readonly Guid LowerUuid = Guid.Parse("00100000-0000-4000-8000-000000000001");
    private static readonly Guid HigherUuid = Guid.Parse("ff100000-0000-4000-8000-000000000001");
    private static readonly DateOnly Birth = new(1975, 6, 15);

    [Test]
    public void Exact_log_odds_tie_is_conflict_and_uuid_only_stabilizes_display_order()
    {
        var model = LinkageModelPolicy.Create(
            ModelId,
            6,
            LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
            V6Parameters());
        var observation = new IdentityObservation(null, "SEM_CPF", "Maria da Silva", Birth, "Ana de Souza");
        var lower = new LinkageCandidate(LowerUuid, observation.NomeCompleto, Birth, observation.NomeMae);
        var higher = new LinkageCandidate(HigherUuid, observation.NomeCompleto, Birth, observation.NomeMae);

        var forward = ProbabilisticLinkageDecisions.Resolve(model, observation, [higher, lower]);
        var reverse = ProbabilisticLinkageDecisions.Resolve(model, observation, [lower, higher]);

        foreach (var decision in new[] { forward, reverse })
        {
            Assert.Multiple(() =>
            {
                Assert.That(decision.Status, Is.EqualTo(ResolutionStatus.CONFLITO));
                Assert.That(decision.PessoaUuidResolvido, Is.Null);
                Assert.That(decision.MelhorCandidatoUuid, Is.EqualTo(LowerUuid));
                Assert.That(decision.SegundoCandidatoUuid, Is.EqualTo(HigherUuid));
                Assert.That(decision.MelhorScore, Is.EqualTo(decision.SegundoScore));
                Assert.That(decision.Margem, Is.EqualTo(0m));
                Assert.That(decision.Motivo, Is.EqualTo("MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE"));
            });
        }
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
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.LogOddsConflictMargin] = .05m,
            [LinkageParameterCatalog.BirthSemanticEvidenceScoring] = 1m,
            ["M_NOME_MAE_MISSING"] = .10m,
            ["U_NOME_MAE_MISSING"] = .10m
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
