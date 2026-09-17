using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
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
        var technicalRc = candidateState.GetProperty("technical_rc");
        var releaseInfo = File.ReadAllText(releasePath);

        Assert.Multiple(() =>
        {
            Assert.That(candidate.RootElement.GetProperty("manifest_version").GetInt32(), Is.EqualTo(2));
            Assert.That(candidate.RootElement.GetProperty("nature").GetString(), Is.EqualTo("ENGINEERING_CANDIDATE"));
            Assert.That(sealedRelease.GetProperty("metadata_source").GetString(), Is.EqualTo("RELEASE_INFO.txt"));
            Assert.That(sealedRelease.GetProperty("solution_engineering").GetString(), Is.EqualTo("v4.05"));
            Assert.That(sealedRelease.GetProperty("solution_schema").GetString(), Is.EqualTo("v3.69"));

            Assert.That(candidateState.GetProperty("target_solution_engineering").GetString(), Is.EqualTo("v5.00"));
            Assert.That(candidateState.GetProperty("release_status").GetString(), Is.EqualTo("NOT_RELEASED"));
            Assert.That(candidateState.GetProperty("solution_schema").GetString(), Is.EqualTo("v3.70"));
            Assert.That(candidateState.GetProperty("candidate_specification_normative_version").ValueKind, Is.EqualTo(JsonValueKind.Null));

            Assert.That(technicalRc.GetProperty("identifier").GetString(), Is.EqualTo("v5.00-rc.1"));
            Assert.That(technicalRc.GetProperty("semver").GetString(), Is.EqualTo("5.0.0-rc.1"));
            Assert.That(technicalRc.GetProperty("status").GetString(), Is.EqualTo("PREPARED_NOT_CUT"));
            Assert.That(technicalRc.GetProperty("release_effect").GetString(), Is.EqualTo("NONE"));
            Assert.That(technicalRc.GetProperty("assembly_version").GetString(), Is.EqualTo("5.0.0.0"));
            Assert.That(technicalRc.GetProperty("file_version").GetString(), Is.EqualTo("5.0.0.0"));
            Assert.That(technicalRc.GetProperty("source_revision_binding").GetString(), Is.EqualTo("DOTNET_SOURCE_REVISION_ID"));

            Assert.That(releaseInfo, Does.Contain("solution_engenharia=v4.05"));
            Assert.That(releaseInfo, Does.Contain("schema_solution=v3.69"));
            Assert.That(releaseInfo, Does.Not.Contain("solution_engenharia=v5.00"));
        });
    }

    [Test]
    public void Technical_rc_manifest_and_dotnet_build_identity_must_match()
    {
        var root = FindRepositoryRoot();
        var propsPath = Path.Combine(root, "Solution", "Directory.Build.props");
        using var candidate = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "CANDIDATE_INFO.json")));
        var technicalRc = candidate.RootElement.GetProperty("candidate").GetProperty("technical_rc");
        var props = XDocument.Load(propsPath);
        var properties = props.Root!
            .Elements("PropertyGroup")
            .Elements()
            .GroupBy(element => element.Name.LocalName)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);

        var expectedSemVer = technicalRc.GetProperty("semver").GetString();
        var expectedAssemblyVersion = technicalRc.GetProperty("assembly_version").GetString();
        var expectedFileVersion = technicalRc.GetProperty("file_version").GetString();
        var assembly = typeof(CandidateInfoTests).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;

        Assert.Multiple(() =>
        {
            Assert.That(properties["VersionPrefix"], Is.EqualTo("5.0.0"));
            Assert.That(properties["VersionSuffix"], Is.EqualTo("rc.1"));
            Assert.That($"{properties["VersionPrefix"]}-{properties["VersionSuffix"]}", Is.EqualTo(expectedSemVer));
            Assert.That(properties["AssemblyVersion"], Is.EqualTo(expectedAssemblyVersion));
            Assert.That(properties["FileVersion"], Is.EqualTo(expectedFileVersion));
            Assert.That(properties["IncludeSourceRevisionInInformationalVersion"], Is.EqualTo("true"));
            Assert.That(properties["SourceRevisionId"], Is.EqualTo("$(GITHUB_SHA)"));

            Assert.That(assembly.GetName().Version?.ToString(), Is.EqualTo(expectedAssemblyVersion));
            Assert.That(fileVersion, Is.EqualTo(expectedFileVersion));
            Assert.That(informationalVersion, Does.StartWith(expectedSemVer!));
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
