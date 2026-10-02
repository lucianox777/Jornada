using NUnit.Framework;

namespace Jornada.Tests.Unit;

/// <summary>
/// The backlog cannot regress to "not implemented" after checked code and PRs are
/// already merged. This checks documented status against the source surface, not
/// operational accreditation or population-level evidence.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class TechnicalDebtEvidenceDocumentationTests
{
    [Test]
    public void BacklogDistinguishesClosedTechnicalDeliveriesFromPendingInstitutionalGates()
    {
        var root = FindRoot();
        static string Read(string root, params string[] parts)
            => File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

        var debts = Read(root, "Solution", "docs", "Dividas_Tecnicas.md");
        var state = Read(root, "Solution", "docs", "Estado_Atual_Projeto.md");
        var plan = Read(root, "Solution", "docs", "Plano_Desenvolvimento.md");
        var dt05 = Read(root, "Solution", "docs", "DT05_Snapshots_Parquet_NAS.md");
        var auth = Read(root, "Solution", "src", "Jornada.Access.Security", "JornadaAccessSecurity.cs");
        var api = Read(root, "Solution", "src", "Jornada.Api", "Program.cs");
        var results = Read(root, "Solution", "src", "Jornada.Resultado.Api", "Program.cs");
        var runner = Read(root, "Solution", "src", "Jornada.Linkage.Runner",
            "ProbabilisticLinkageBatchRunner.cs");
        var guard = Read(root, "Solution", "tests", "Jornada.Integration.Tests",
            "Integration", "Dt05PublicationGuardsSqlServerTests.cs");
        var dt05Sql = Read(root, "Solution", "database", "migrations",
            "20260927_Linkage_Transicao_Semantica_DT05.sql");

        Assert.Multiple(() =>
        {
            Assert.That(debts, Does.Contain("DT-03 |"));
            Assert.That(debts, Does.Contain("DT-04 |"));
            Assert.That(debts, Does.Contain("ACEITE TÉCNICO v1 CONCLUÍDO"));
            Assert.That(debts, Does.Contain("ACEITE TÉCNICO DEV CONCLUÍDO"));
            Assert.That(debts, Does.Contain("DT-05 concluiu seu aceite estreito"));
            Assert.That(debts, Does.Contain("DT-16 |"));
            Assert.That(debts, Does.Contain("REVERTIDA (30/09/2026)"));
            Assert.That(debts, Does.Contain("| DT-13 | **CONCLUÍDA — técnica** | Remover"));
            Assert.That(debts, Does.Contain("| DT-17 | **CONCLUÍDA — escopo técnico DEV** | Matriz obrigatória"));
            Assert.That(debts, Does.Contain("| DT-12 | **CONCLUÍDA — técnica** | Higiene de branches e notas"));
            Assert.That(debts, Does.Contain("| DT-06 | **CONCLUÍDA — técnica** | Baseline das migrations"));
            Assert.That(debts, Does.Contain("Permanecem na fila ativa: **DT-05 e DT-15**"));
            Assert.That(debts, Does.Contain("PR #580"));
            Assert.That(debts, Does.Not.Contain("\\n| DT-"));
            Assert.That(state, Does.Contain("v5.00-rc.1"));
            Assert.That(state, Does.Contain("POST /api/v1/identidade/candidatos"));
            Assert.That(state, Does.Contain("deny-by-default"));
            Assert.That(plan, Does.Contain("implementação técnica DEV entregue"));
            Assert.That(plan, Does.Contain("matriz de regressão DT-17"));
            Assert.That(dt05, Does.Contain("aceite estreito"));
            Assert.That(dt05, Does.Contain("DT-05 global ainda PARCIAL"));
            Assert.That(dt05, Does.Contain("vínculo SQL create-once do manifesto NAS imutável já foi integrado no PR #700"));
            Assert.That(dt05, Does.Contain("reconstrução histórica determinística e medição de custo/latência"));
            Assert.That(auth, Does.Contain("class JornadaAccessAuthenticationHandler"));
            Assert.That(api, Does.Contain("AddJornadaAccessSecurity()"));
            Assert.That(results, Does.Contain("AddJornadaAccessSecurity()"));
            Assert.That(api, Does.Contain("UseAuthentication()"));
            Assert.That(results, Does.Contain("UseAuthentication()"));
            Assert.That(api, Does.Contain("/api/v1/identidade/candidatos"));
            Assert.That(runner, Does.Contain("sp_publicar_resolucao_progressiva_linkage_lote"));
            Assert.That(runner, Does.Not.Contain("DECLARE progressiva_linkage CURSOR"));
            Assert.That(guard, Does.Contain("N'PREPARANDO'"));
            Assert.That(guard, Does.Contain("Is.EqualTo(51941)"));
            Assert.That(dt05Sql, Does.Contain("status=N'EXECUTANDO'"));
            Assert.That(File.Exists(Path.Combine(root, "Solution", "tests",
                "Jornada.Tests", "Unit", "OpenApiTypedContractTests.cs")), Is.True);
        });
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt"))
                && Directory.Exists(Path.Combine(current.FullName, "Solution", "docs")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Não foi possível localizar a raiz Jornada.");
    }
}
