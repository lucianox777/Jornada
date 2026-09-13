using System.Text.Json;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class CandidateInfoTests
{
    [Test]
    public void Candidate_manifest_must_not_rewrite_the_last_sealed_release()
    {
        var root = FindRepositoryRoot();
        var candidatePath = Path.Combine(root, "CANDIDATE_INFO.json");
        var releasePath = Path.Combine(root, "RELEASE_INFO.txt");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(candidatePath), Is.True);
            Assert.That(File.Exists(releasePath), Is.True);
        });

        using var candidate = JsonDocument.Parse(File.ReadAllText(candidatePath));
        var sealedRelease = candidate.RootElement.GetProperty("sealed_release");
        var candidateState = candidate.RootElement.GetProperty("candidate");
        var releaseInfo = File.ReadAllText(releasePath);

        Assert.Multiple(() =>
        {
            Assert.That(candidate.RootElement.GetProperty("nature").GetString(), Is.EqualTo("ENGINEERING_CANDIDATE"));
            Assert.That(sealedRelease.GetProperty("metadata_source").GetString(), Is.EqualTo("RELEASE_INFO.txt"));
            Assert.That(sealedRelease.GetProperty("solution_engineering").GetString(), Is.EqualTo("v4.05"));
            Assert.That(sealedRelease.GetProperty("solution_schema").GetString(), Is.EqualTo("v3.69"));

            Assert.That(candidateState.GetProperty("target_solution_engineering").GetString(), Is.EqualTo("v5.00"));
            Assert.That(candidateState.GetProperty("release_status").GetString(), Is.EqualTo("NOT_RELEASED"));
            Assert.That(candidateState.GetProperty("solution_schema").GetString(), Is.EqualTo("v3.70"));
            Assert.That(candidateState.GetProperty("candidate_specification_normative_version").ValueKind, Is.EqualTo(JsonValueKind.Null));

            Assert.That(releaseInfo, Does.Contain("solution_engenharia=v4.05"));
            Assert.That(releaseInfo, Does.Contain("schema_solution=v3.69"));
            Assert.That(releaseInfo, Does.Not.Contain("solution_engenharia=v5.00"));
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
