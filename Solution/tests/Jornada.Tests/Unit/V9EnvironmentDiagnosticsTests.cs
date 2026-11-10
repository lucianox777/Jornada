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
    public void Homologation_requires_explicit_private_sink_opt_in(string environment)
    {
        Assert.That(V9WorkerDevelopmentDiagnostics.Enabled(
            Env(("DOTNET_ENVIRONMENT", environment))), Is.False);
        Assert.That(V9WorkerDevelopmentDiagnostics.Enabled(Env(
            ("DOTNET_ENVIRONMENT", environment),
            ("ASPNETCORE_ENVIRONMENT", environment),
            ("JORNADA_RUNTIME_MODE", environment),
            ("JORNADA_PRIVATE_DIAGNOSTICS", "1"))), Is.True);
    }

    [Test]
    public void Production_ci_and_conflicting_environments_always_fail_closed()
    {
        var cases = new[]
        {
            Env(("DOTNET_ENVIRONMENT", "Production"),
                ("JORNADA_PRIVATE_DIAGNOSTICS", "1")),
            Env(("DOTNET_ENVIRONMENT", "HML"), ("JORNADA_RUNTIME_MODE", "PROD"),
                ("JORNADA_PRIVATE_DIAGNOSTICS", "1")),
            Env(("DOTNET_ENVIRONMENT", "Staging"), ("ASPNETCORE_ENVIRONMENT", "Production"),
                ("JORNADA_PRIVATE_DIAGNOSTICS", "1")),
            Env(("DOTNET_ENVIRONMENT", "Staging"), ("CI", "true"),
                ("JORNADA_PRIVATE_DIAGNOSTICS", "1")),
            Env(("DOTNET_ENVIRONMENT", "Development"), ("GITHUB_ACTIONS", "true")),
            Env(("DOTNET_ENVIRONMENT", "Staging"), ("JORNADA_PRIVATE_DIAGNOSTICS", "true"))
        };
        foreach (var env in cases)
        {
            using var sink = new StringWriter();
            Assert.That(V9WorkerDevelopmentDiagnostics.Enabled(env), Is.False);
            V9WorkerDevelopmentDiagnostics.WriteException(
                new Exception("synthetic-must-remain-private"), sink, env);
            Assert.That(sink.ToString(), Is.Empty);
        }
    }
}
