using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class LinkageDecisionMarginContractTests
{
    [Test]
    public void V6_margin_is_log_odds_and_can_legitimately_exceed_one()
    {
        var parameters = V6Parameters();
        var model = LinkageModelPolicy.Create(
            Guid.Parse("8a000000-0000-4000-8000-000000000001"),
            6,
            LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
            parameters);
        var birth = new DateOnly(1982, 4, 10);
        var observation = new IdentityObservation(null, "NAO_INFORMADO", "Maria da Silva", birth, "Ana de Souza");

        var decision = ProbabilisticLinkageDecisions.Resolve(model, observation,
        [
            new LinkageCandidate(Guid.Parse("8b000000-0000-4000-8000-000000000001"), "Maria da Silva", birth, "Ana de Souza"),
            new LinkageCandidate(Guid.Parse("8b000000-0000-4000-8000-000000000002"), "Nome sem relação", birth, "Ana de Souza")
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
            Assert.That(decision.MelhorScore, Is.InRange(0m, 1m));
            Assert.That(decision.SegundoScore, Is.InRange(0m, 1m));
            Assert.That(decision.Margem, Is.GreaterThan(1m),
                "A margem V6 é diferença de log-odds e não pode ser limitada ao intervalo de probabilidades.");
        });
    }

    [Test]
    public void SqlServer_migration_preserves_probability_scores_allows_log_odds_margin_and_forbids_self_second_candidate()
    {
        var root = FindSolutionRoot();
        var migration = File.ReadAllText(Path.Combine(
            root, "database", "migrations", "20260915_Linkage_LogOdds_Margin.sql"));

        Assert.Multiple(() =>
        {
            Assert.That(migration, Does.Contain("ALTER COLUMN margem DECIMAL(18,8) NULL"));
            Assert.That(migration, Does.Contain("score_melhor >= 0 AND score_melhor <= 1"));
            Assert.That(migration, Does.Contain("score_segundo >= 0 AND score_segundo <= 1"));
            Assert.That(migration, Does.Contain("margem IS NULL OR margem >= 0"));
            Assert.That(migration, Does.Not.Contain("margem <= 1"));
            Assert.That(migration, Does.Contain("ck_linkage_resultado_candidatos_distintos"));
            Assert.That(migration, Does.Contain("segundo_candidato_uuid <> melhor_candidato_uuid"));
        });
    }

    private static Dictionary<string, decimal> V6Parameters()
    {
        var parameters = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .001m,
            [LinkageParameterCatalog.PriorBlockMin] = .000001m,
            [LinkageParameterCatalog.PriorBlockMax] = .25m,
            [LinkageParameterCatalog.Threshold] = .40m,
            [LinkageParameterCatalog.ConflictMargin] = .03m,
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.LogOddsConflictMargin] = .05m,
            [LinkageParameterCatalog.BirthSemanticEvidenceScoring] = 1m,
            ["M_NOME_MAE_MISSING"] = .10m,
            ["U_NOME_MAE_MISSING"] = .10m
        };

        foreach (var state in LinkageParameterCatalog.NameStates)
        {
            parameters[$"M_NOME_{state}"] = state == "EXACT" ? .99m : state == "LOW" ? .001m : .0045m;
            parameters[$"U_NOME_{state}"] = state == "EXACT" ? .001m : state == "LOW" ? .99m : .0045m;
            parameters[$"M_NOME_MAE_{state}"] = .25m;
            parameters[$"U_NOME_MAE_{state}"] = .25m;
        }

        foreach (var state in BirthDateSemanticEvidence.States)
        {
            parameters[$"M_NASCIMENTO_SEMANTICO_{state}"] = .5m;
            parameters[$"U_NASCIMENTO_SEMANTICO_{state}"] = .5m;
        }

        return parameters;
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Jornada.sln")))
                return directory.FullName;
            var nested = Path.Combine(directory.FullName, "Solution", "Jornada.sln");
            if (File.Exists(nested))
                return Path.Combine(directory.FullName, "Solution");
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Raiz da Solution não encontrada a partir do diretório de testes.");
    }
}
