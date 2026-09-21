using Jornada.Contracts;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class LinkagePromotionConferenceGateContractTests
{
    [Test]
    public void Governed_production_tolerance_remains_unfrozen_and_fail_closed()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(
            root,
            "Solution",
            "config",
            "linkage",
            "implementation-conference-tolerance.json");

        var config = ImplementationConferenceToleranceConfiguration.Load(path);
        var tolerance = config.ToContract();

        Assert.Multiple(() =>
        {
            Assert.That(config.MethodVersion,
                Is.EqualTo(ImplementationConferenceGovernanceContract.MethodVersion));
            Assert.That(config.Scope,
                Is.EqualTo(ImplementationConferenceGovernanceContract.Scope));
            Assert.That(tolerance.Version, Is.EqualTo("UNFROZEN"));
            Assert.That(tolerance.MaxAbsolutePairLlrDifference, Is.Null);
            Assert.That(tolerance.TryGetFrozen(out _, out var reason), Is.False);
            Assert.That(reason, Is.EqualTo("TOLERANCE_NOT_FROZEN"));
        });
    }

    [Test]
    public void Parameters_worker_requires_same_governed_conference_before_validate_and_activate()
    {
        var root = FindRepositoryRoot();
        var worker = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "src",
            "Jornada.Linkage.Parameters.Worker",
            "LinkageParametersWorker.cs"));
        var project = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "src",
            "Jornada.Linkage.Parameters.Worker",
            "Jornada.Linkage.Parameters.Worker.csproj"));

        Assert.Multiple(() =>
        {
            Assert.That(worker, Does.Contain("LoadPromotionConferenceTolerance"));
            Assert.That(worker, Does.Contain("LinkageParameters:ConferenceToleranceConfigPath"));
            Assert.That(worker, Does.Contain("sp_assert_conferencia_linkage_conforme"));
            Assert.That(worker, Does.Contain("@conference_method_version"));
            Assert.That(worker, Does.Contain("@conference_tolerance_version"));
            Assert.That(worker, Does.Contain("@conference_max_llr"));
            Assert.That(worker, Does.Contain("TOLERANCE_NOT_FROZEN"));
            Assert.That(project, Does.Not.Contain("Jornada.Linkage.Conference"));
            Assert.That(project, Does.Not.Contain("Jornada.Linkage.Evaluation"));
        });
    }

    [Test]
    public void Operational_calibration_paths_do_not_skip_conference()
    {
        var root = FindRepositoryRoot();
        var ci = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        var windows = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "install",
            "windows-production",
            "Invoke-JornadaLinkageCalibration.ps1"));
        var local = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "scripts",
            "local-cluster.sh"));
        var scale = File.ReadAllText(Path.Combine(
            root,
            "Solution",
            "scripts",
            "local-scale.sh"));

        Assert.Multiple(() =>
        {
            Assert.That(ci.IndexOf("src/Jornada.Linkage.Conference", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(ci.IndexOf("LinkageParameters__Operation=VALIDATE", StringComparison.Ordinal),
                Is.GreaterThan(ci.IndexOf("src/Jornada.Linkage.Conference", StringComparison.Ordinal)));

            Assert.That(windows.IndexOf("Linkage Conference:", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(windows.IndexOf("Invoke-Parameters 'VALIDATE'", StringComparison.Ordinal),
                Is.GreaterThan(windows.IndexOf("Linkage Conference:", StringComparison.Ordinal)));

            Assert.That(local.IndexOf("/Jornada.Linkage.Conference/", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(local.IndexOf("LinkageParameters__Operation=VALIDATE", StringComparison.Ordinal),
                Is.GreaterThan(local.IndexOf("/Jornada.Linkage.Conference/", StringComparison.Ordinal)));

            Assert.That(scale.IndexOf("src/Jornada.Linkage.Conference", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(scale.IndexOf("LinkageParameters__Operation=VALIDATE", StringComparison.Ordinal),
                Is.GreaterThan(scale.IndexOf("src/Jornada.Linkage.Conference", StringComparison.Ordinal)));
        });
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt"))
                && Directory.Exists(Path.Combine(current.FullName, "Solution")))
                return current.FullName;
            current = current.Parent;
        }

        Assert.Fail("Raiz do repositório Jornada não encontrada.");
        return string.Empty;
    }
}
