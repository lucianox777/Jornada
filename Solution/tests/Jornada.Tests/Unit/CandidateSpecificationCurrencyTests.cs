using System.Text.Json;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class CandidateSpecificationCurrencyTests
{
    [Test]
    public void Candidate_manifest_keeps_current_external_gates_and_sql_server_production_target()
    {
        var root = FindRepositoryRoot();
        var specificationPath = Path.Combine(root, "Documentos", "Especificacao_Tecnica_Jornada_Candidata.md");
        var manifestPath = Path.Combine(root, "CANDIDATE_INFO.json");
        var readmePath = Path.Combine(root, "Solution", "README.md");
        var fabricCompatibilityPath = Path.Combine(root, "Solution", "docs", "Fabric_SQL_Compatibility.md");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(specificationPath), Is.True);
            Assert.That(File.Exists(manifestPath), Is.True);
            Assert.That(File.Exists(readmePath), Is.True);
            Assert.That(File.Exists(fabricCompatibilityPath), Is.True);
        });

        var specification = File.ReadAllText(specificationPath);
        var readme = File.ReadAllText(readmePath);
        var fabricCompatibility = File.ReadAllText(fabricCompatibilityPath);
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var externalGates = manifest.RootElement.GetProperty("external_gates").EnumerateArray()
            .Select(value => value.GetString()).Where(value => value is not null).Cast<string>().ToArray();
        var productionTarget = manifest.RootElement.GetProperty("candidate").GetProperty("production_relational_target").GetString();

        Assert.Multiple(() =>
        {
            Assert.That(specification, Does.Contain("o SHA exato somente é fixado no corte formal da candidata"));
            Assert.That(specification, Does.Not.Match(@"Base técnica de consolidação:\*\* `master` em `[0-9a-f]{40}`"));
            Assert.That(specification, Does.Contain("O contrato cadastral `Pessoa v3` admite `nomeMae` ausente"));
            Assert.That(specification, Does.Contain("Grupo de Trabalho do Programa Reencontro (GTPR)"));

            Assert.That(productionTarget, Is.EqualTo("MICROSOFT_SQL_SERVER"));
            Assert.That(readme, Does.Contain("Microsoft SQL Server como tecnologia relacional normativa e banco relacional operacional de Produção"));
            Assert.That(readme, Does.Contain("SQL Database in Microsoft Fabric não é alvo operacional de Produção nem gate de release"));
            Assert.That(fabricCompatibility, Does.Contain("Não existe mais gate `FABRIC_SQL_DATABASE_EXACT_HEAD_HOMOLOGATION`"));

            Assert.That(externalGates, Does.Not.Contain("NOMEMAE_TRANSITION"));
            Assert.That(externalGates, Does.Contain("HML_REPRESENTATIVE_VOLUMETRY"));
            Assert.That(externalGates, Does.Contain("GTPR_PURPOSE_LEGAL_BASIS_DECISION"));
            Assert.That(externalGates, Does.Not.Contain("CCGD_PURPOSE_LEGAL_BASIS_DECISION"));
            Assert.That(externalGates, Does.Not.Contain("FABRIC_SQL_DATABASE_EXACT_HEAD_HOMOLOGATION"));
            Assert.That(externalGates, Does.Contain("LINKAGE_REPRESENTATIVE_STATISTICAL_VALIDATION"));
        });
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt")) &&
                Directory.Exists(Path.Combine(current.FullName, "Solution")) &&
                Directory.Exists(Path.Combine(current.FullName, "Documentos")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        Assert.Fail("Raiz do repositório Jornada não encontrada a partir do diretório de testes.");
        return string.Empty;
    }
}
