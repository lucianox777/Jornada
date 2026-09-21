using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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
            Assert.That(candidate.RootElement.GetProperty("manifest_version").GetInt32(), Is.EqualTo(3));
            Assert.That(candidate.RootElement.GetProperty("nature").GetString(), Is.EqualTo("ENGINEERING_CANDIDATE"));
            Assert.That(sealedRelease.GetProperty("metadata_source").GetString(), Is.EqualTo("RELEASE_INFO.txt"));
            Assert.That(sealedRelease.GetProperty("solution_engineering").GetString(), Is.EqualTo("v4.05"));
            Assert.That(sealedRelease.GetProperty("solution_schema").GetString(), Is.EqualTo("v3.69"));

            Assert.That(candidateState.GetProperty("target_solution_engineering").GetString(), Is.EqualTo("v5.00"));
            Assert.That(candidateState.GetProperty("release_status").GetString(), Is.EqualTo("NOT_RELEASED"));
            Assert.That(candidateState.GetProperty("solution_schema").GetString(), Is.EqualTo("v3.70"));
            Assert.That(candidateState.GetProperty("candidate_specification").GetString(), Is.EqualTo("Documentos/Especificacao_Tecnica_Jornada_v5.00_Candidata.md"));
            Assert.That(candidateState.GetProperty("candidate_specification_identifier").GetString(), Is.EqualTo("v5.00-candidata"));
            Assert.That(candidateState.GetProperty("candidate_specification_status").GetString(), Is.EqualTo("TECHNICAL_CANDIDATE_NOT_PUBLISHED"));
            Assert.That(candidateState.GetProperty("candidate_specification_normative_version").ValueKind, Is.EqualTo(JsonValueKind.Null));

            Assert.That(technicalRc.GetProperty("identifier").GetString(), Is.EqualTo("v5.00-rc.1"));
            Assert.That(technicalRc.GetProperty("semver").GetString(), Is.EqualTo("5.0.0-rc.1"));
            Assert.That(technicalRc.GetProperty("status").GetString(), Is.EqualTo("CHECKPOINT_CONTENT"));
            Assert.That(technicalRc.GetProperty("release_effect").GetString(), Is.EqualTo("NONE"));
            Assert.That(technicalRc.GetProperty("assembly_version").GetString(), Is.EqualTo("5.0.0.0"));
            Assert.That(technicalRc.GetProperty("file_version").GetString(), Is.EqualTo("5.0.0.0"));
            Assert.That(technicalRc.GetProperty("source_revision_binding").GetString(), Is.EqualTo("DOTNET_SOURCE_REVISION_ID"));
            Assert.That(technicalRc.GetProperty("cut_rule").GetString(), Does.Contain("workflow_dispatch rc_evidence_preflight=true"));
            Assert.That(technicalRc.GetProperty("actions_freeze_until_cut").GetBoolean(), Is.True);
            Assert.That(technicalRc.GetProperty("release_assets_mutability").GetString(), Is.EqualTo("REPLACEABLE_BY_RERUN_WITH_CLOBBER"));
            Assert.That(technicalRc.GetProperty("digest_authority").GetString(), Is.EqualTo("SIGSTORE_ATTESTATION_SUBJECT_DIGESTS"));

            var preflight = technicalRc.GetProperty("preflight");
            Assert.That(preflight.GetProperty("required").GetBoolean(), Is.True);
            Assert.That(preflight.GetProperty("trigger").GetString(), Is.EqualTo("workflow_dispatch"));
            Assert.That(preflight.GetProperty("input").GetString(), Is.EqualTo("rc_evidence_preflight"));
            Assert.That(preflight.GetProperty("required_value").GetBoolean(), Is.True);
            Assert.That(preflight.GetProperty("same_commit_required").GetBoolean(), Is.True);
            Assert.That(preflight.GetProperty("required_job").GetString(), Is.EqualTo("rc-evidence"));
            Assert.That(preflight.GetProperty("required_conclusion").GetString(), Is.EqualTo("success"));
            Assert.That(preflight.GetProperty("remote_publish_allowed").GetBoolean(), Is.False);

            Assert.That(releaseInfo, Does.Contain("solution_engenharia=v4.05"));
            Assert.That(releaseInfo, Does.Contain("schema_solution=v3.69"));
            Assert.That(releaseInfo, Does.Not.Contain("solution_engenharia=v5.00"));
        });
    }

    [Test]
    public void Candidate_schema_provenance_must_bind_manifest_and_structural_fingerprint()
    {
        var root = FindRepositoryRoot();
        using var candidate = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "CANDIDATE_INFO.json")));
        var candidateState = candidate.RootElement.GetProperty("candidate");
        var provenance = candidateState.GetProperty("schema_provenance");
        var manifestRelative = provenance.GetProperty("migration_manifest").GetString()!;
        var manifestPath = Path.Combine(root, manifestRelative.Replace('/', Path.DirectorySeparatorChar));
        var manifestText = File.ReadAllText(manifestPath)
            .TrimStart('\uFEFF')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var manifestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifestText))).ToLowerInvariant();
        var recordedManifestHash = provenance.GetProperty("migration_manifest_sha256").GetString();
        var structuralHash = provenance.GetProperty("structural_fingerprint_sha256").GetString();
        var sourceCommit = provenance.GetProperty("source_commit").GetString();

        Assert.Multiple(() =>
        {
            Assert.That(provenance.GetProperty("status").GetString(), Is.EqualTo("BOUND_FOR_TECHNICAL_RC"));
            Assert.That(provenance.GetProperty("canonical_ddl").GetString(), Is.EqualTo(candidateState.GetProperty("canonical_ddl").GetString()));
            Assert.That(provenance.GetProperty("migration_manifest_hash_method").GetString(), Is.EqualTo("SHA256_UTF8_LF"));
            Assert.That(recordedManifestHash, Is.EqualTo(manifestHash));
            Assert.That(structuralHash, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(sourceCommit, Does.Match("^[0-9a-f]{40}$"));
            Assert.That(provenance.GetProperty("evidence_model").GetString(), Is.EqualTo("CURRENT_CI_PLUS_EXACT_RC_RECOMPUTATION"));
        });

        var workflow = File.ReadAllText(Path.Combine(
            root, ".github", "workflows", "schema-consolidation-370.yml"));
        Assert.Multiple(() =>
        {
            Assert.That(workflow, Does.Contain("CANDIDATE_INFO.json"));
            Assert.That(workflow, Does.Contain("fetch-depth: 0"));
            Assert.That(workflow, Does.Contain("git merge-base --is-ancestor"));
            Assert.That(workflow, Does.Contain("structural_fingerprint_sha256"));
            Assert.That(workflow, Does.Contain("test \"$actual_fingerprint\" = \"$declared_fingerprint\""));
            Assert.That(workflow, Does.Not.Contain("actions/runs/$ddl_run_id"));
            Assert.That(workflow, Does.Contain("CANDIDATE_INFO.json"));
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
            Assert.That(properties.ContainsKey("VersionPrefix"), Is.False, "RC binário não deve alterar a versão NuGet dos projetos.");
            Assert.That(properties.ContainsKey("VersionSuffix"), Is.False, "RC binário não deve alterar a versão NuGet dos projetos.");
            Assert.That(properties["AssemblyVersion"], Is.EqualTo(expectedAssemblyVersion));
            Assert.That(properties["FileVersion"], Is.EqualTo(expectedFileVersion));
            Assert.That(properties["InformationalVersion"], Is.EqualTo(expectedSemVer));
            Assert.That(properties["IncludeSourceRevisionInInformationalVersion"], Is.EqualTo("true"));
            Assert.That(properties["SourceRevisionId"], Is.EqualTo("$(GITHUB_SHA)"));

            Assert.That(assembly.GetName().Version?.ToString(), Is.EqualTo(expectedAssemblyVersion));
            Assert.That(fileVersion, Is.EqualTo(expectedFileVersion));
            Assert.That(informationalVersion, Does.StartWith(expectedSemVer!));
        });
    }

    [Test]
    public void Technical_rc_must_have_dispatch_preflight_and_tag_only_publish_path()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        var rcGate = File.ReadAllText(Path.Combine(root, "Solution", "scripts", "technical-rc-gate.py"));
        var rcEvidence = File.ReadAllText(Path.Combine(root, "Solution", "scripts", "rc-evidence-gate.py"));
        var rcBundle = File.ReadAllText(Path.Combine(root, "Solution", "scripts", "build-rc-source-bundle.sh"));

        Assert.Multiple(() =>
        {
            Assert.That(workflow, Does.Contain("release-promotion:\n    if: github.ref_type == 'tag' && startsWith(github.ref_name, 'jornada-solution-v')"));
            Assert.That(workflow, Does.Contain("rc_evidence_preflight:"));
            Assert.That(workflow, Does.Contain("github.event_name == 'workflow_dispatch' && inputs.rc_evidence_preflight"));
            Assert.That(workflow, Does.Contain("rc-evidence:"));
            Assert.That(workflow, Does.Contain("Resolve technical RC identity for tag or dispatch preflight"));
            Assert.That(workflow, Does.Contain("JORNADA_RC_MODE=preflight").Or.Contain("rc_mode=\"preflight\""));
            Assert.That(workflow, Does.Contain("Require prior successful same-commit RC preflight before tag cut"));
            Assert.That(workflow, Does.Contain("event=workflow_dispatch&status=success&head_sha=${GITHUB_SHA}"));
            Assert.That(workflow, Does.Contain("j.get(\"name\")==\"rc-evidence\"").Or.Contain("j.get(\"name\") == \"rc-evidence\""));
            Assert.That(workflow, Does.Contain("rc-publish:"));
            Assert.That(workflow, Does.Contain("needs: rc-evidence"));
            Assert.That(workflow, Does.Contain("Verify declared release-asset mutability"));
            Assert.That(workflow, Does.Contain("REPLACEABLE_BY_RERUN_WITH_CLOBBER"));
            Assert.That(workflow, Does.Contain("gh release create"));
            Assert.That(workflow, Does.Contain("--prerelease"));
            Assert.That(workflow, Does.Contain("Jornada_Dev_DdlFingerprint.sql"));
            Assert.That(workflow, Does.Contain("RC_SCHEMA_PROVENANCE.json"));
            Assert.That(rcGate, Does.Contain("CHECKPOINT_CONTENT"));
            Assert.That(rcGate, Does.Contain("release_effect"));
            Assert.That(rcGate, Does.Contain("actions_freeze_until_cut"));
            Assert.That(rcGate, Does.Contain("REPLACEABLE_BY_RERUN_WITH_CLOBBER"));
            Assert.That(rcGate, Does.Contain("SIGSTORE_ATTESTATION_SUBJECT_DIGESTS"));
            Assert.That(rcGate, Does.Contain("same_commit_required"));
            Assert.That(rcEvidence, Does.Contain("TECHNICAL_RC_EVIDENCE"));
            Assert.That(rcEvidence, Does.Contain("LINKAGE_REPRESENTATIVE_STATISTICAL_VALIDATION"));
            Assert.That(rcBundle, Does.Contain("RC_SOURCE_PROVENANCE.json"));
            Assert.That(rcBundle, Does.Contain("git bundle verify"));
            var rcEvidenceJob = workflow[workflow.IndexOf("  rc-evidence:", StringComparison.Ordinal)..workflow.IndexOf("  rc-publish:", StringComparison.Ordinal)];
            var rcPublishJob = workflow[workflow.IndexOf("  rc-publish:", StringComparison.Ordinal)..workflow.IndexOf("  release-promotion:", StringComparison.Ordinal)];
            Assert.That(rcEvidenceJob, Does.Contain("actions: read"));
            Assert.That(rcEvidenceJob, Does.Contain("contents: read"));
            Assert.That(rcEvidenceJob, Does.Not.Contain("contents: write"));
            Assert.That(rcPublishJob, Does.Contain("contents: write"));
            Assert.That(rcPublishJob, Does.Not.Contain("workflow_dispatch"));
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
