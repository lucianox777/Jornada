using Jornada.Contracts;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class LinkagePromotionConferenceGateContractTests
{
    [Test]
    public void Governed_engineering_tolerance_is_predeclared_and_frozen_with_exact_decision()
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
            Assert.That(tolerance.Version, Is.EqualTo("V1_2026-09-26"));
            Assert.That(tolerance.MaxAbsolutePairLlrDifference, Is.EqualTo(.01m));
            Assert.That(tolerance.TryGetFrozen(out var value, out var reason), Is.True);
            Assert.That(value, Is.EqualTo(.01m));
            Assert.That(reason, Is.Empty);
            Assert.That(config.DecisionEquivalence, Is.EqualTo(
                ImplementationConferenceGovernanceContract.DecisionEquivalence));
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
            Assert.That(worker, Does.Contain("TryGetFrozen"));
            Assert.That(worker, Does.Contain("Promoção do modelo bloqueada pela conferência de implementação"));
            Assert.That(project, Does.Not.Contain("Jornada.Linkage.Conference"));
            Assert.That(project, Does.Not.Contain("Jornada.Linkage.Evaluation"));
        });
    }

    [Test]
    public void Validate_and_activate_share_the_same_governed_parameters_and_sql_assertion()
    {
        var root = FindRepositoryRoot();
        var worker = File.ReadAllText(Path.Combine(
            root, "Solution", "src", "Jornada.Linkage.Parameters.Worker",
            "LinkageParametersWorker.cs"));

        const string validateMarker = "private async Task ValidateDraftAsync(";
        const string activateMarker = "private async Task ActivateValidatedAsync(";
        const string loaderMarker = "private ImplementationConferenceToleranceContract LoadPromotionConferenceTolerance(";
        var validateStart = worker.IndexOf(validateMarker, StringComparison.Ordinal);
        var activateStart = worker.IndexOf(activateMarker, StringComparison.Ordinal);
        var loaderStart = worker.IndexOf(loaderMarker, StringComparison.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(validateStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(activateStart, Is.GreaterThan(validateStart));
            Assert.That(loaderStart, Is.GreaterThan(activateStart));
        });
        if (validateStart < 0 || activateStart <= validateStart || loaderStart <= activateStart)
            return;

        var validate = worker[validateStart..activateStart];
        var activate = worker[activateStart..loaderStart];
        foreach (var section in new[] { validate, activate })
        {
            Assert.Multiple(() =>
            {
                Assert.That(section, Does.Contain("LoadPromotionConferenceTolerance();"));
                Assert.That(section, Does.Contain("BeginTransactionAsync(IsolationLevel.Serializable"));
                Assert.That(section, Does.Contain("EXEC auditoria.sp_assert_conferencia_linkage_conforme"));
                Assert.That(section, Does.Contain("@modelo_id=@modelo_id"));
                Assert.That(section, Does.Contain("@metodo_versao=@conference_method_version"));
                Assert.That(section, Does.Contain("@tolerancia_versao=@conference_tolerance_version"));
                Assert.That(section, Does.Contain("@max_llr_par_permitido=@conference_max_llr"));
                Assert.That(section, Does.Contain("AddConferenceGateParameters(command, conferenceTolerance);"));
                Assert.That(section, Does.Contain("{DecisionCalibrationRateGateSql}"),
                    "Os dois caminhos devem executar o MESMO gate SQL de taxa de FP.");
                Assert.That(section.IndexOf("EXEC auditoria.sp_assert_conferencia_linkage_conforme",
                    StringComparison.Ordinal),
                    Is.LessThan(section.IndexOf("UPDATE identidade.modelo_linkage SET status=",
                        StringComparison.Ordinal)));
            });
        }
        Assert.That(worker.Split("LoadPromotionConferenceTolerance();", StringSplitOptions.None).Length - 1,
            Is.EqualTo(2), "Somente os dois gates de promoção devem carregar o contrato governado.");
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
            Assert.That(windows, Does.Not.Contain("--connection-string $connectionString"));

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
