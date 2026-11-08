using Microsoft.Extensions.Logging;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

/// <summary>
/// A2 canary: each public Program.cs command passes through ONE exception
/// boundary. The delegate is fault-injected BEFORE any SQL/Gold/TEST access.
/// Full process/host service-failure smoke remains a separate CI gate.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class V9WorkerEntryBoundaryTests
{
    private const string SecretCanary =
        "SENHA_CANARY_2026_CPF_11122233344_SQLCONN_Server=PRIVATE";
    // Keep in sync with every Program.cs CLI branch, including aliases.
    private static readonly string[] Commands =
    [
        "--v9-tf-export-ibge-pin",
        "--v9-prepare-gold-runner",
        "--v9-calibrate",
        "--v9-validate-finalists",
        "--v9-compare-calibration-plans",
        "--v9-adaptive-study",
        "--v9-adaptive-census",
        "--v9-frozen-transfer",
        "--v9-audit-ref-inventory",
        "--v9-audit-ref-frozen-snapshot",
        V9FrozenJointTrainingDerivationCli.Flag,
        V9FrozenJointIndependentComparisonCli.PreflightFlag,
        V9FrozenJointIndependentComparisonCli.Flag,
        "--v9-gold-frozen-tf-preflight",
        "--v9-inspect-fitted-snapshot",
        "--v9-physical-probe-seed42",
        "HOST_DEFAULT_NO_CLI_FLAG"
    ];

    [Test]
    public async Task Every_CLI_and_host_route_canary_is_sanitized_and_exits_nonzero()
    {
        foreach (var command in Commands)
        {
            using var stderr = new StringWriter();
            var executed = false;
            var status = await V9WorkerEntryBoundary.ExecuteAsync(
                [command, SecretCanary],
                suppliedArgs =>
                {
                    executed = true;
                    Assert.That(suppliedArgs, Is.EqualTo(new[] { command, SecretCanary }));
                    throw new InvalidDataException("PRIVATE " + command + " " + SecretCanary);
                }, stderr);
            Assert.Multiple((Action)(() =>
            {
                Assert.That(executed, Is.True, "Dispatch did not execute: " + command);
                Assert.That(status, Is.EqualTo(1), "Failing command returned zero: " + command);
                Assert.That(stderr.ToString().Trim(), Is.EqualTo("V9_CALIBRATOR_FAILED:INVALID_DATA"));
                Assert.That(stderr.ToString(), Does.Not.Contain(SecretCanary));
                Assert.That(stderr.ToString(), Does.Not.Contain(command));
                Assert.That(stderr.ToString(), Does.Not.Contain(" at "));
                Assert.That(stderr.ToString(), Does.Not.Contain("SqlConnection"));
            }));
        }
    }

    [Test]
    public async Task Entry_boundary_is_lossless_on_success_and_does_not_set_exit_code()
    {
        var invoked = false;
        using var stderr = new StringWriter();
        var status = await V9WorkerEntryBoundary.ExecuteAsync(
            ["--v9-inspect-fitted-snapshot"],
            args =>
            {
                invoked = args.Length == 1;
                return Task.CompletedTask;
            }, stderr);
        Assert.That(status, Is.Zero);
        Assert.That(invoked, Is.True);
        Assert.That(stderr.ToString(), Is.Empty);
    }

    [Test]
    public async Task Io_and_unexpected_failures_emit_codes_only_even_when_stderr_fails()
    {
        using var stderr = new StringWriter();
        var status = await V9WorkerEntryBoundary.ExecuteAsync(["--v9-tf-export-ibge-pin"],
            _ => throw new IOException("PRIVATE " + SecretCanary), stderr);
        Assert.That(status, Is.EqualTo(1));
        Assert.That(stderr.ToString().Trim(), Is.EqualTo("V9_CALIBRATOR_FAILED:IO_ERROR"));
        using var broken = new FailingWriter();
        var unhandled = await V9WorkerEntryBoundary.ExecuteAsync(["--v9-prepare-gold-runner"],
            _ => throw new InvalidOperationException("PRIVATE " + SecretCanary), broken);
        Assert.That(unhandled, Is.EqualTo(1), "Broken stderr must not trigger .NET public exception dump");
    }

    [Test]
    public void Host_internal_error_logger_writes_only_safe_codes_marks_failure_and_never_calls_formatter()
    {
        using var stderr = new StringWriter();
        var failures = 0;
        using var provider = new V9WorkerSafeHostLoggerProvider(stderr, () => failures++);
        var logger = provider.CreateLogger("Microsoft.Extensions.Hosting.Internal.Host");
        logger.Log(LogLevel.Error, new EventId(15), SecretCanary,
            new IOException(SecretCanary),
            (state, exception) => throw new InvalidOperationException(SecretCanary));
        Assert.That(stderr.ToString().Trim(), Is.EqualTo("V9_CALIBRATOR_FAILED:IO_ERROR"));
        Assert.That(stderr.ToString(), Does.Not.Contain(SecretCanary));
        Assert.That(failures, Is.EqualTo(1));
        Assert.That(V9WorkerSafeHostLoggerProvider.AllowProviderLog(
            "Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider", LogLevel.Error), Is.False);
        Assert.That(V9WorkerSafeHostLoggerProvider.AllowProviderLog(
            typeof(V9WorkerSafeHostLoggerProvider).FullName, LogLevel.Error), Is.True);
        Assert.That(V9WorkerSafeHostLoggerProvider.AllowProviderLog("Console", LogLevel.Information),
            Is.True);
        logger.Log(LogLevel.Warning, new EventId(16), SecretCanary, null,
            (state, exception) => throw new InvalidOperationException(SecretCanary));
        Assert.That(stderr.ToString(), Does.Contain("V9_WORKER_NOTICE:HOST_WARNING"));
        Assert.That(failures, Is.EqualTo(1), "Harmless host warning must not force failure");
    }

    [Test]
    public void Program_source_encloses_all_dispatch_and_host_startup_inside_one_boundary()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Solution",
            "src", "Jornada.Linkage.Parameters.Worker", "Program.cs")))
            directory = directory.Parent;
        Assert.That(directory, Is.Not.Null, "Repository checkout required to verify public entry points");
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "Solution", "src",
            "Jornada.Linkage.Parameters.Worker", "Program.cs"));
        var start = source.IndexOf("V9WorkerEntryBoundary.ExecuteAsync(args, async guardedArgs =>",
            StringComparison.Ordinal);
        var end = source.LastIndexOf("if (entryStatus != 0)", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThan(0));
        Assert.That(end, Is.GreaterThan(start));
        var inside = source.Substring(start, end - start);
        foreach (var command in Commands.Where(x => x != "HOST_DEFAULT_NO_CLI_FLAG"))
        {
            // Two frozen-comparison aliases use their published const flags,
            // not literal strings in Program.cs.
            var expected = command switch
            {
                V9FrozenJointTrainingDerivationCli.Flag => "V9FrozenJointTrainingDerivationCli.Flag",
                V9FrozenJointIndependentComparisonCli.PreflightFlag =>
                    "V9FrozenJointIndependentComparisonCli.PreflightFlag",
                V9FrozenJointIndependentComparisonCli.Flag => "V9FrozenJointIndependentComparisonCli.Flag",
                _ => command
            };
            Assert.That(inside, Does.Contain(expected),
                "Entry path escaped the A2 boundary: " + command);
        }
        Assert.That(inside, Does.Contain("Host.CreateApplicationBuilder(guardedArgs)"));
        Assert.That(inside, Does.Contain("builder.Logging.AddProvider(new V9WorkerSafeHostLoggerProvider())"));
        Assert.That(inside, Does.Contain("V9WorkerSafeHostLoggerProvider.AllowProviderLog"));
        Assert.That(inside, Does.Contain("await host.RunAsync()"));
        Assert.That(inside, Does.Not.Contain("Console.Error.WriteLine(exception"));
        Assert.That(inside, Does.Not.Contain("throw exception"));
        // All actual routes use the protected argument vector, not an
        // unprotected dispatch outside the single ExecuteAsync.
        Assert.That(inside, Does.Not.Match(@"(?<![A-Za-z])args\["));
    }

    private sealed class FailingWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException(SecretCanary);
    }
}
