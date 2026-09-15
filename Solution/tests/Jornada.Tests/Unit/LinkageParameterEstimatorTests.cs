using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class LinkageParameterEstimatorTests
{
    [Test]
    public void Generates_m_u_threshold_prior_and_semantic_birth_v5_parameters()
    {
        var (matched, unmatched) = TrainingPairs();

        var p = LinkageParameterEstimator.Estimate(matched, unmatched, 1000, 100, 0.5m, 0.95m, 0.03m);

        Assert.Multiple(() =>
        {
            Assert.That(p.ContainsKey("M_NOME_EXACT"), Is.True);
            Assert.That(p.ContainsKey("U_NOME_LOW"), Is.True);
            Assert.That(p["T_LINKAGE"], Is.EqualTo(0.95m));
            Assert.That(p["CONFLICT_MARGIN"], Is.EqualTo(0.03m));
            Assert.That(p[LinkageParameterCatalog.LogOddsConflictMargin], Is.EqualTo(0.03m));
            Assert.That(p[LinkageParameterCatalog.DecisionEvidenceScoring], Is.EqualTo(1m));
            Assert.That(p.ContainsKey("M_NOME_MAE_MISSING"), Is.True);
            Assert.That(p.ContainsKey("U_NOME_MAE_MISSING"), Is.True);
            Assert.That(p["PRIOR_MATCH_PROBABILITY"], Is.EqualTo(0.1m));
            Assert.That(p["PRIOR_BLOCK_MAX"], Is.EqualTo(0.25m));
            Assert.That(p[LinkageParameterCatalog.BirthSemanticEvidenceScoring], Is.EqualTo(1m));
            Assert.That(p.ContainsKey(LinkageParameterCatalog.BirthJointEvidenceScoring), Is.False);
            Assert.That(p.ContainsKey(LinkageParameterCatalog.BirthSingleEvidenceScoring), Is.False);
            Assert.That(p.ContainsKey(LinkageParameterCatalog.BirthComponentScoring), Is.False);
            Assert.That(p.Keys.Any(static x => x.StartsWith("BLOCKING_", StringComparison.Ordinal)), Is.False);

            foreach (var state in LinkageParameterCatalog.BirthSemanticStates)
            {
                Assert.That(p.ContainsKey($"M_NASCIMENTO_SEMANTICO_{state}"), Is.True);
                Assert.That(p.ContainsKey($"U_NASCIMENTO_SEMANTICO_{state}"), Is.True);
                Assert.That(p.ContainsKey($"SUPPORT_M_NASCIMENTO_SEMANTICO_{state}"), Is.True);
                Assert.That(p.ContainsKey($"SUPPORT_U_NASCIMENTO_SEMANTICO_{state}"), Is.True);
            }
            Assert.That(LinkageParameterCatalog.BirthSemanticStates.Sum(state => p[$"M_NASCIMENTO_SEMANTICO_{state}"]), Is.EqualTo(1m).Within(0.00000001m));
            Assert.That(LinkageParameterCatalog.BirthSemanticStates.Sum(state => p[$"U_NASCIMENTO_SEMANTICO_{state}"]), Is.EqualTo(1m).Within(0.00000001m));
            Assert.That(p[$"SUPPORT_M_NASCIMENTO_SEMANTICO_{BirthDateSemanticEvidence.Exact}"], Is.EqualTo(1m));
            Assert.That(p[$"SUPPORT_M_NASCIMENTO_SEMANTICO_{BirthDateSemanticEvidence.OneDigitError}"], Is.EqualTo(1m));

            foreach (var state in LinkageParameterCatalog.BirthJointStates)
            {
                Assert.That(p.ContainsKey($"M_NASCIMENTO_CONJUNTO_{state}"), Is.True);
                Assert.That(p.ContainsKey($"U_NASCIMENTO_CONJUNTO_{state}"), Is.True);
            }
            Assert.That(LinkageParameterCatalog.BirthJointStates.Sum(state => p[$"M_NASCIMENTO_CONJUNTO_{state}"]), Is.EqualTo(1m).Within(0.00000001m));
            Assert.That(LinkageParameterCatalog.BirthJointStates.Sum(state => p[$"U_NASCIMENTO_CONJUNTO_{state}"]), Is.EqualTo(1m).Within(0.00000001m));

            Assert.That(p.ContainsKey("M_DATA_NASCIMENTO_EXACT"), Is.True);
            Assert.That(p.ContainsKey("M_DATA_NASCIMENTO_DIFF"), Is.True);
            Assert.That(p.ContainsKey("U_DATA_NASCIMENTO_EXACT"), Is.True);
            Assert.That(p.ContainsKey("U_DATA_NASCIMENTO_DIFF"), Is.True);
            Assert.That(p["M_DATA_NASCIMENTO_EXACT"] + p["M_DATA_NASCIMENTO_DIFF"], Is.EqualTo(1m).Within(0.00000001m));
            Assert.That(p["U_DATA_NASCIMENTO_EXACT"] + p["U_DATA_NASCIMENTO_DIFF"], Is.EqualTo(1m).Within(0.00000001m));

            Assert.That(p.ContainsKey("M_NASC_DIA_EXACT"), Is.True);
            Assert.That(p.ContainsKey("M_NASC_DIA_DIFF"), Is.True);
            Assert.That(p.ContainsKey("U_NASC_DIA_EXACT"), Is.True);
            Assert.That(p.ContainsKey("U_NASC_DIA_DIFF"), Is.True);
            Assert.That(p.ContainsKey("M_NASC_MES_EXACT"), Is.True);
            Assert.That(p.ContainsKey("M_NASC_ANO_EXACT"), Is.True);
        });
    }

    [Test]
    public void Explicit_v4_contract_activates_only_v4_but_keeps_v5_distribution_for_replay()
    {
        var (matched, unmatched) = TrainingPairs();

        var p = LinkageParameterEstimator.Estimate(matched, unmatched, 1000, 100, 0.5m, 0.95m, 0.03m,
            BirthScoringContract.JointEvidenceV4);

        Assert.Multiple(() =>
        {
            Assert.That(p[LinkageParameterCatalog.BirthJointEvidenceScoring], Is.EqualTo(1m));
            Assert.That(p.ContainsKey(LinkageParameterCatalog.BirthSemanticEvidenceScoring), Is.False);
            Assert.That(p.ContainsKey(LinkageParameterCatalog.DecisionEvidenceScoring), Is.False);
            Assert.That(p.ContainsKey(LinkageParameterCatalog.LogOddsConflictMargin), Is.False);
            Assert.That(p.ContainsKey("M_NOME_MAE_MISSING"), Is.False);
            Assert.That(p.ContainsKey("U_NOME_MAE_MISSING"), Is.False);
            foreach (var name in LinkageParameterCatalog.BirthSemanticEvidenceRequired)
                Assert.That(p.ContainsKey(name), Is.True, $"Distribuição V5 para replay ausente: {name}.");
        });
    }

    [Test]
    public void Explicit_v3_contract_activates_only_v3_but_keeps_newer_distributions_for_replay()
    {
        var (matched, unmatched) = TrainingPairs();

        var p = LinkageParameterEstimator.Estimate(matched, unmatched, 1000, 100, 0.5m, 0.95m, 0.03m,
            BirthScoringContract.SingleEvidenceV3);

        Assert.Multiple(() =>
        {
            Assert.That(p[LinkageParameterCatalog.BirthSingleEvidenceScoring], Is.EqualTo(1m));
            Assert.That(p.ContainsKey(LinkageParameterCatalog.BirthSemanticEvidenceScoring), Is.False);
            Assert.That(p.ContainsKey(LinkageParameterCatalog.BirthJointEvidenceScoring), Is.False);
            Assert.That(p.ContainsKey(LinkageParameterCatalog.BirthComponentScoring), Is.False);
            Assert.That(p.ContainsKey(LinkageParameterCatalog.DecisionEvidenceScoring), Is.False,
                "Contrato legado não pode receber flag V6 por acidente.");
            Assert.That(p.ContainsKey(LinkageParameterCatalog.LogOddsConflictMargin), Is.False);
            Assert.That(p.ContainsKey("M_NOME_MAE_MISSING"), Is.False);
            Assert.That(p.ContainsKey("U_NOME_MAE_MISSING"), Is.False);
            Assert.That(p["M_NOME_MAE_EXACT"] + p["M_NOME_MAE_HIGH"] + p["M_NOME_MAE_MEDIUM"] + p["M_NOME_MAE_LOW"],
                Is.EqualTo(1m).Within(0.00000001m));
            Assert.That(p["U_NOME_MAE_EXACT"] + p["U_NOME_MAE_HIGH"] + p["U_NOME_MAE_MEDIUM"] + p["U_NOME_MAE_LOW"],
                Is.EqualTo(1m).Within(0.00000001m));
            foreach (var name in LinkageParameterCatalog.BirthSemanticEvidenceRequired)
                Assert.That(p.ContainsKey(name), Is.True, $"Distribuição V5 para replay ausente: {name}.");
            foreach (var name in LinkageParameterCatalog.BirthJointEvidenceRequired)
                Assert.That(p.ContainsKey(name), Is.True, $"Distribuição V4 para replay ausente: {name}.");
            foreach (var name in LinkageParameterCatalog.BirthSingleEvidenceRequired)
                Assert.That(p.ContainsKey(name), Is.True, $"Distribuição V3 ausente: {name}.");
        });
    }

    [Test]
    public void Missing_mother_name_is_an_explicit_v6_state_not_low_similarity()
    {
        var matched = new[]
        {
            new IdentityTrainingPair("Maria Silva", new DateOnly(1980,1,1), "Ana Silva", "Maria Silva", new DateOnly(1980,1,1), "Ana Silva"),
            new IdentityTrainingPair("Joao Souza", new DateOnly(1970,2,2), null, "Joao Souza", new DateOnly(1970,2,2), "Rita Souza")
        };
        var unmatched = new[]
        {
            new IdentityTrainingPair("Maria Silva", new DateOnly(1980,1,1), "Ana Silva", "Carlos Pereira", new DateOnly(1981,1,1), "Lucia Pereira"),
            new IdentityTrainingPair("Joao Souza", new DateOnly(1970,2,2), null, "Mariana Lima", new DateOnly(1970,3,2), null)
        };

        var p = LinkageParameterEstimator.Estimate(matched, unmatched, 1000, 100, 0.5m, 0.95m, 0.03m);

        Assert.Multiple(() =>
        {
            Assert.That(p.ContainsKey("M_NOME_MAE_SAMPLE_SIZE"), Is.False);
            Assert.That(p.ContainsKey("U_NOME_MAE_SAMPLE_SIZE"), Is.False);
            Assert.That(p["M_NOME_MAE_MISSING"], Is.GreaterThan(0m));
            Assert.That(p["U_NOME_MAE_MISSING"], Is.GreaterThan(0m));
            Assert.That(p["M_NOME_MAE_EXACT"], Is.GreaterThan(p["M_NOME_MAE_LOW"]));
            Assert.That(p["U_NOME_MAE_LOW"], Is.GreaterThan(p["U_NOME_MAE_EXACT"]));
        });
    }

    private static (IdentityTrainingPair[] Matched, IdentityTrainingPair[] Unmatched) TrainingPairs()
    {
        var matched = new[]
        {
            new IdentityTrainingPair("Maria da Silva", new DateOnly(1980,1,1), "Ana Silva", "Maria da Silva", new DateOnly(1980,1,1), "Ana Silva"),
            new IdentityTrainingPair("Joao Souza", new DateOnly(1970,2,2), "Rita Souza", "João de Souza", new DateOnly(1970,2,3), "Rita Souza")
        };
        var unmatched = new[]
        {
            new IdentityTrainingPair("Maria da Silva", new DateOnly(1980,1,1), "Ana Silva", "Carlos Pereira", new DateOnly(1981,1,1), "Lucia Pereira"),
            new IdentityTrainingPair("Joao Souza", new DateOnly(1970,2,2), "Rita Souza", "Mariana Lima", new DateOnly(1970,3,2), "Teresa Lima")
        };
        return (matched, unmatched);
    }
}
