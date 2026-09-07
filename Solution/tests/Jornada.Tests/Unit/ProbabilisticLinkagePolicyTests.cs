using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class ProbabilisticLinkagePolicyTests
{
    private static readonly Guid ModelId = Guid.Parse("83000000-0000-4000-8000-000000000001");
    private static readonly Guid CandidateA = Guid.Parse("84000000-0000-4000-8000-000000000001");
    private static readonly Guid CandidateB = Guid.Parse("84000000-0000-4000-8000-000000000002");
    private static readonly DateOnly Birth = new(1982, 4, 10);

    [Test]
    public void V1_RemainsCompatibleWithoutBirthComponentParameters()
    {
        var model = LinkageModelPolicy.Create(ModelId, 1, "FELLEGI_SUNTER_V1", Parameters());
        Assert.That(LinkageModelPolicy.SupportsBirthComponentScoring(model), Is.False);
        var observation = Observation();
        var decision = ProbabilisticLinkageDecisions.Resolve(model, observation,
            [new LinkageCandidate(CandidateA, observation.NomeCompleto, Birth, observation.NomeMae)]);
        Assert.Multiple(() =>
        {
            Assert.That(decision.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
            Assert.That(decision.PessoaUuidResolvido, Is.EqualTo(CandidateA));
            Assert.That(decision.ModeloId, Is.EqualTo(ModelId));
        });
    }

    [Test]
    public void EnabledV2_MustNotSilentlyFallBackToV1()
    {
        var parameters = Parameters();
        parameters["BLOCKING_BIRTH_COMPONENTS_V2"] = 1m;
        foreach (var feature in new[] { "NASC_DIA", "NASC_MES", "NASC_ANO" })
        {
            parameters[$"M_{feature}_EXACT"] = .9m;
            parameters[$"M_{feature}_DIFF"] = .1m;
            parameters[$"U_{feature}_EXACT"] = .1m;
            parameters[$"U_{feature}_DIFF"] = .9m;
        }
        var complete = LinkageModelPolicy.Create(ModelId, 2, "FELLEGI_SUNTER_V1", parameters);
        Assert.That(LinkageModelPolicy.SupportsBirthComponentScoring(complete), Is.True);
        parameters.Remove("U_NASC_DIA_DIFF");
        var error = Assert.Throws<InvalidOperationException>(() =>
            LinkageModelPolicy.Create(ModelId, 2, "FELLEGI_SUNTER_V1", parameters));
        Assert.That(error!.Message, Does.Contain("U_NASC_DIA_DIFF"));
    }

    [Test]
    public void Decisions_KeepThresholdMarginAndDeterministicOrdering()
    {
        var model = LinkageModelPolicy.Create(ModelId, 1, "FELLEGI_SUNTER_V1", Parameters());
        var observation = Observation();
        var low = ProbabilisticLinkageDecisions.Resolve(model, observation,
            [new LinkageCandidate(CandidateA, "Nome sem relação", Birth, "Mãe diferente")]);
        Assert.Multiple(() =>
        {
            Assert.That(low.Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
            Assert.That(low.PessoaUuidResolvido, Is.Null);
            Assert.That(low.Motivo, Is.EqualTo("ABAIXO_T_LINKAGE"));
        });
        var tied = ProbabilisticLinkageDecisions.Resolve(model, observation,
        [
            new LinkageCandidate(CandidateB, observation.NomeCompleto, Birth, observation.NomeMae),
            new LinkageCandidate(CandidateA, observation.NomeCompleto, Birth, observation.NomeMae)
        ]);
        Assert.Multiple(() =>
        {
            Assert.That(tied.Status, Is.EqualTo(ResolutionStatus.CONFLITO));
            Assert.That(tied.PessoaUuidResolvido, Is.Null);
            Assert.That(tied.MelhorCandidatoUuid, Is.EqualTo(CandidateA));
            Assert.That(tied.SegundoCandidatoUuid, Is.EqualTo(CandidateB));
            Assert.That(tied.Margem, Is.EqualTo(0m));
        });
        var absent = ProbabilisticLinkageDecisions.Resolve(model, observation, []);
        Assert.That(absent.Motivo, Is.EqualTo("SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO"));
        Assert.Throws<InvalidOperationException>(() => ProbabilisticLinkageDecisions.Resolve(model,
            Observation("11144477735"), []));
    }

    private static IdentityObservation Observation(string? cpf = null) =>
        new(cpf, cpf is null ? "NAO_INFORMADO" : null, "Maria da Silva", Birth, "Ana de Souza");

    private static Dictionary<string, decimal> Parameters()
    {
        var parameters = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["PRIOR_MATCH_PROBABILITY"] = .001m,
            ["PRIOR_BLOCK_MIN"] = .000001m,
            ["PRIOR_BLOCK_MAX"] = .25m,
            ["T_LINKAGE"] = .90m,
            ["CONFLICT_MARGIN"] = .05m
        };
        foreach (var feature in new[] { "NOME", "NOME_MAE" })
            foreach (var (state, m, u) in new[]
            {
                ("EXACT", .90m, .01m), ("HIGH", .05m, .04m),
                ("MEDIUM", .03m, .10m), ("LOW", .02m, .85m)
            })
            {
                parameters[$"M_{feature}_{state}"] = m;
                parameters[$"U_{feature}_{state}"] = u;
            }
        return parameters;
    }
}
