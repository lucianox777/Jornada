using System.Text.Json;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class CandidateSpecificationCurrencyTests
{
    [Test]
    public void Candidate_specification_does_not_freeze_evolving_head_or_reopen_resolved_nome_mae_gate()
    {
        var root = FindRepositoryRoot();
        var specificationPath = Path.Combine(root, "Documentos", "Especificacao_Tecnica_Jornada_Candidata.md");
        var manifestPath = Path.Combine(root, "CANDIDATE_INFO.json");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(specificationPath), Is.True);
            Assert.That(File.Exists(manifestPath), Is.True);
        });

        var specification = File.ReadAllText(specificationPath);
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var externalGates = manifest.RootElement
            .GetProperty("external_gates")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(specification, Does.Contain("o SHA exato somente é fixado no corte formal da candidata"));
            Assert.That(specification, Does.Not.Match(@"Base técnica de consolidação:\*\* `master` em `[0-9a-f]{40}`"));

            Assert.That(specification, Does.Contain("O contrato cadastral `Pessoa v3` admite `nomeMae` ausente"));
            Assert.That(specification, Does.Contain("não deve preencher sinteticamente esse atributo nem descartar a observação por sua ausência"));
            Assert.That(specification, Does.Not.Contain("Transição de `nomeMae`"));

            Assert.That(externalGates, Does.Not.Contain("NOMEMAE_TRANSITION"));
            Assert.That(externalGates, Does.Contain("HML_REPRESENTATIVE_VOLUMETRY"));
            Assert.That(externalGates, Does.Contain("CCGD_PURPOSE_LEGAL_BASIS_DECISION"));
            Assert.That(externalGates, Does.Contain("FABRIC_SQL_DATABASE_EXACT_HEAD_HOMOLOGATION"));
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
