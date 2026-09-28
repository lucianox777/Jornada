using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jornada.Tests.Unit;

/// <summary>
/// DT-07 checks the authoritative release/RC/SQL/security contracts together.
/// DT-08 asserts that physical C# files, not fragile Compile Link entries, define
/// the Linkage.Core boundary. Neither test represents an HML/Production approval.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class DocumentationDriftContractTests
{
    [Test]
    public void ReleaseRcReadmeAndCandidateSpecificationAgreeOnSealedAndTechnicalVersions()
    {
        var root = Root();
        var release = Read(root, "RELEASE_INFO.txt");
        var readme = Read(root, "LEIA-ME.txt");
        var security = Read(root, "SECURITY.md");
        var spec = Read(root, "Documentos/Especificacao_Tecnica_Jornada_v5.00_Candidata.md");
        var sql = Read(root, "Solution/database/Jornada_Fase1_v3.70.sql");
        var manifest = Read(root, "Solution/database/migrations/manifest.txt");
        var debts = Read(root, "Solution/docs/Dividas_Tecnicas.md");
        var evidence = Read(root, "Solution/docs/DT07_Conferencia_Drift_20260928.md");
        var docsReadme = Read(root, "Solution/docs/README.md");

        using var candidate = JsonDocument.Parse(Read(root, "CANDIDATE_INFO.json"));
        var sealedRelease = candidate.RootElement.GetProperty("sealed_release");
        var target = candidate.RootElement.GetProperty("candidate");
        var rc = target.GetProperty("technical_rc");

        Assert.Multiple(() =>
        {
            Assert.That(release, Does.Contain("solution_engenharia=v4.05"));
            Assert.That(release, Does.Contain("schema_solution=v3.69"));
            Assert.That(release, Does.Contain("base_normativa=v3.64"));
            Assert.That(sealedRelease.GetProperty("solution_engineering").GetString(), Is.EqualTo("v4.05"));
            Assert.That(sealedRelease.GetProperty("solution_schema").GetString(), Is.EqualTo("v3.69"));
            Assert.That(sealedRelease.GetProperty("declared_base_normative").GetString(), Is.EqualTo("v3.64"));
            Assert.That(target.GetProperty("target_solution_engineering").GetString(), Is.EqualTo("v5.00"));
            Assert.That(target.GetProperty("release_status").GetString(), Is.EqualTo("NOT_RELEASED"));
            Assert.That(target.GetProperty("solution_schema").GetString(), Is.EqualTo("v3.70"));
            Assert.That(rc.GetProperty("identifier").GetString(), Is.EqualTo("v5.00-rc.1"));
            Assert.That(rc.GetProperty("release_effect").GetString(), Is.EqualTo("NONE"));
            Assert.That(readme, Does.Contain("v5.00-rc.1"));
            Assert.That(readme, Does.Contain("v5.00 FINAL/NORMATIVA NÃO PUBLICADA"));
            Assert.That(readme, Does.Contain("SolutionSchema técnico corrente: 3.70"));
            Assert.That(security, Does.Contain("release_effect=NONE"));
            Assert.That(security, Does.Contain("deny-by-default"));
            Assert.That(spec, Does.Contain("Solution/database/Jornada_Fase1_v3.70.sql"));
            Assert.That(spec, Does.Contain("## 18. Artefatos de evidência principais"));
            Assert.That(sql, Does.Contain("GERADO de database/migrations/manifest.txt"));
            Assert.That(sql, Does.Contain(":r database/Jornada_Fase1.sql"));
            Assert.That(manifest, Does.Contain("migrations/20260910_Schema_Consolidation_370.sql"));
            Assert.That(debts, Does.Contain("ACEITE DOCUMENTAL CONCLUÍDO"));
            Assert.That(evidence, Does.Contain("DT-07 — conferência de drift"));
            Assert.That(docsReadme, Does.Contain("Separação técnica antes do Ensaio — implementada"));
            Assert.That(docsReadme, Does.Contain("tolerância técnica V1 **FROZEN**"));
        });
    }

    [Test]
    public void GitleaksCurrentTreeGateIsPinnedAndFullHistoryRemainsSeparatelyGoverned()
    {
        var root = Root();
        var security = Read(root, "SECURITY.md");
        var workflow = Read(root, ".github/workflows/ci.yml");
        var historyWorkflow = Read(root, ".github/workflows/secret-history-audit.yml");
        var gitleaks = Read(root, ".gitleaks.toml");
        var scan = Read(root, "Solution/docs/Security_Secret_Scanning.md");

        const string version = "GITLEAKS_VERSION: '8.30.1'";
        const string hash = "GITLEAKS_LINUX_X64_SHA256: " +
            "'551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb'";
        Assert.Multiple(() =>
        {
            Assert.That(workflow, Does.Contain(version));
            Assert.That(workflow, Does.Contain(hash));
            Assert.That(workflow, Does.Contain("sha256sum --check --"));
            Assert.That(workflow, Does.Contain("./scripts/security-secret-scan.sh --current-tree-only"));
            Assert.That(historyWorkflow, Does.Contain(version));
            Assert.That(historyWorkflow, Does.Contain(hash));
            Assert.That(historyWorkflow, Does.Contain("workflow_dispatch:"));
            Assert.That(historyWorkflow, Does.Contain("fetch-depth: 0"));
            Assert.That(historyWorkflow, Does.Contain("history-audit-summary.json"));
            Assert.That(security, Does.Contain("não é gate executado em cada commit"));
            Assert.That(security, Does.Contain("triagem humana na issue #405"));
            Assert.That(gitleaks, Does.Contain("useDefault = true"));
            Assert.That(gitleaks, Does.Not.Contain("[[allowlists]]"));
            Assert.That(gitleaks, Does.Contain("id = \"generic-api-key\""));
            Assert.That(gitleaks, Does.Contain("[[rules.allowlists]]"));
            Assert.That(scan, Does.Contain("histórica completa"));
        });
    }

    [Test]
    public void LinkageCorePhysicalSourcesAndAllCurrentProjectsHaveNoLinkedCSharpCompileFiles()
    {
        var root = Root();
        var sol = Path.Combine(root, "Solution");
        var sln = File.ReadAllLines(Path.Combine(sol, "Jornada.sln"));
        var projectPaths = sln
            .Select(line => Regex.Match(line, "\"([^\"]+\\.csproj)\""))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.That(projectPaths, Has.Length.EqualTo(21),
            "A conferência DT-08 refere-se ao monólito de 21 projetos antes da migração física DT-16.");
        foreach (var project in projectPaths)
        {
            var projectFile = Path.Combine(sol, project);
            Assert.That(File.Exists(projectFile), Is.True, project);
            var csproj = File.ReadAllText(projectFile);
            Assert.That(csproj, Does.Not.Match(@"<Compile\s+Include\s*="), project);
            Assert.That(csproj, Does.Not.Match(@"<Compile\s+Remove\s*="), project);
        }

        var coreDir = Path.Combine(sol, "src", "Jornada.Linkage.Core");
        foreach (var source in new[]
        {
            "CalibrationCandidatePareto.cs",
            "FellegiSunterScoring.cs",
            "FsDecisionThresholdCalibration.cs",
            "ProbabilisticLinkagePolicy.cs"
        })
            Assert.That(File.Exists(Path.Combine(coreDir, source)), Is.True,
                "O PR #508 moveu fisicamente os quatro arquivos para Linkage.Core.");

        var core = File.ReadAllText(Path.Combine(coreDir, "Jornada.Linkage.Core.csproj"));
        var runner = Read(root, "Solution/src/Jornada.Linkage.Runner/Jornada.Linkage.Runner.csproj");
        var evidence = Read(root, "Solution/docs/DT07_Conferencia_Drift_20260928.md");
        Assert.Multiple(() =>
        {
            Assert.That(core, Does.Contain("Jornada.Contracts.csproj"));
            Assert.That(runner, Does.Contain("Jornada.Linkage.Core.csproj"));
            Assert.That(evidence, Does.Contain("DT-08 já entregue"));
        });
    }

    private static string Read(string root, string relative)
        => File.ReadAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string Root()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RELEASE_INFO.txt"))
                && Directory.Exists(Path.Combine(directory.FullName, "Solution", "docs")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Raiz da Jornada não encontrada.");
    }
}
