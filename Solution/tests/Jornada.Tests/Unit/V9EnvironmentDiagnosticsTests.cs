using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9EnvironmentDiagnosticsTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] settings)
    {
        var values = settings.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        return key => values.TryGetValue(key, out var value) ? value : null;
    }

    [Test]
    public void Production_error_code_has_stable_category_without_exception_message()
    {
        var error = new InvalidDataException("secret-token-canary");
        V9CalibradorSafeFailureCategory.Mark(error, V9CalibradorSafeFailureCategory.PhysicalSqlProbe);
        V9CalibradorSafeFailureCategory.MarkProbeCheckpoint(error, "VALIDATION_PAIR_SCORING");
        var code = V9CalibradorLastErrorLog.DetailedErrorCode(error);
        Assert.That(code, Does.StartWith("V9_CALIBRATOR_FAILED:INVALID_DATA:PHYSICAL_SQL_PROBE:"));
        Assert.That(code, Does.Contain("VALIDATION_PAIR_SCORING"));
        Assert.That(code, Does.Not.Contain("secret-token-canary"));
    }

    [Test]
    public void Private_development_supports_full_exception_but_public_code_stays_stable()
    {
        var env = Env(("DOTNET_ENVIRONMENT", "Development"));
        var error = new InvalidDataException("synthetic-private-details");
        using var sink = new StringWriter();
        Assert.That(V9WorkerDevelopmentDiagnostics.Enabled(env), Is.True);
        V9WorkerDevelopmentDiagnostics.WriteException(error, sink, env);
        Assert.That(sink.ToString(), Does.Contain("synthetic-private-details"));
        Assert.That(V9CalibradorLastErrorLog.SafeErrorCode(error),
            Is.EqualTo("V9_CALIBRATOR_FAILED:INVALID_DATA"));
    }

    [TestCase("Staging")]
    [TestCase("Homologation")]
    [TestCase("Homologacao")]
    [TestCase("HML")]
    public void Homologation_uses_existing_runtime_environment_only(string environment)
    {
        Assert.That(V9WorkerDevelopmentDiagnostics.Enabled(
            Env(("DOTNET_ENVIRONMENT", environment))), Is.True);
        Assert.That(V9WorkerDevelopmentDiagnostics.Enabled(Env(
            ("DOTNET_ENVIRONMENT", environment),
            ("ASPNETCORE_ENVIRONMENT", environment),
            ("JORNADA_RUNTIME_MODE", environment))), Is.True);
    }

    [TestCase("Development", "GITHUB_ACTIONS")]
    [TestCase("Development", "CI")]
    [TestCase("HML", "GITHUB_ACTIONS")]
    public void Ci_marker_does_not_override_nonproduction_diagnostics(string environment, string marker)
    {
        var env = Env(("DOTNET_ENVIRONMENT", environment), (marker, "true"));
        using var sink = new StringWriter();
        Assert.That(V9WorkerDevelopmentDiagnostics.Enabled(env), Is.True);
        V9WorkerDevelopmentDiagnostics.WriteException(
            new InvalidDataException("synthetic-development-detail"), sink, env);
        Assert.That(sink.ToString(), Does.Contain("synthetic-development-detail"));
    }

    [Test]
    public void Production_and_conflicting_environments_always_fail_closed()
    {
        var cases = new[]
        {
            Env(("DOTNET_ENVIRONMENT", "Production"),
                ("JORNADA_PRIVATE_DIAGNOSTICS", "1")),
            Env(("DOTNET_ENVIRONMENT", "HML"), ("JORNADA_RUNTIME_MODE", "PROD"),
                ("JORNADA_PRIVATE_DIAGNOSTICS", "1")),
            Env(("DOTNET_ENVIRONMENT", "Staging"), ("ASPNETCORE_ENVIRONMENT", "Production"),
                ("JORNADA_PRIVATE_DIAGNOSTICS", "1")),
            Env(("DOTNET_ENVIRONMENT", "Staging"), ("ASPNETCORE_ENVIRONMENT", "Development"))
        };
        foreach (var env in cases)
        {
            using var sink = new StringWriter();
            Assert.That(V9WorkerDevelopmentDiagnostics.Enabled(env), Is.False);
            V9WorkerDevelopmentDiagnostics.WriteException(
                new InvalidDataException("synthetic-must-remain-private"), sink, env);
            Assert.That(sink.ToString(), Is.Empty);
        }
    }
}
