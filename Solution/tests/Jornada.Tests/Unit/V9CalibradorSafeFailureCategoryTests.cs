using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9CalibradorSafeFailureCategoryTests
{
    private const string Sensitive = "SENHA_CANARY_SQL_PASSWORD_CPF_11122233344";

    [Test]
    public void Identifies_internal_resource_codes_only()
    {
        Assert.That(V9CalibradorSafeFailureCategory.Classify(
            new InvalidDataException("V9_RESOURCE_RUNTIME_RAM_BELOW_ONE_WORKER")),
            Is.EqualTo("RESOURCE_RUNTIME_RAM"));
        Assert.That(V9CalibradorSafeFailureCategory.Classify(
            new InvalidDataException("V9_RESOURCE_RUNTIME_DISK_BELOW_RESERVE")),
            Is.EqualTo("RESOURCE_RUNTIME_DISK"));
        Assert.That(V9CalibradorSafeFailureCategory.Classify(
            new InvalidDataException("V9_RESOURCE_SQL_CONNECTION_CAPACITY")),
            Is.EqualTo("RESOURCE_SQL_POOL"));
    }

    [Test]
    public void Does_not_echo_untrusted_exception_messages_or_data()
    {
        var malicious = new InvalidDataException("V9_RESOURCE_RUNTIME_DISK_BELOW_RESERVE " + Sensitive);
        malicious.Data["JornadaV9SafeFailureCategory"] = Sensitive;
        Assert.That(V9CalibradorSafeFailureCategory.Classify(malicious),
            Is.EqualTo(V9CalibradorSafeFailureCategory.Unclassified));
        Assert.That(V9CalibradorSafeFailureCategory.Classify(new IOException(Sensitive)),
            Is.EqualTo(V9CalibradorSafeFailureCategory.Unclassified));
    }

    [Test]
    public void Allows_only_compiled_failure_scopes()
    {
        var error = new InvalidDataException(Sensitive);
        V9CalibradorSafeFailureCategory.Mark(error,
            V9CalibradorSafeFailureCategory.PhysicalSqlFrame);
        var category = V9CalibradorSafeFailureCategory.Classify(error);
        Assert.That(category, Is.EqualTo("PHYSICAL_SQL_FRAME"));
        Assert.That(category, Does.Not.Contain(Sensitive));
        Assert.Throws<ArgumentException>((Action)(() =>
            V9CalibradorSafeFailureCategory.Mark(error, Sensitive)));
        Assert.That(V9CalibradorSafeFailureCategory.Classify(error),
            Is.EqualTo(category));
    }

    [Test]
    public void Innermost_safe_category_survives_outer_physical_frame_boundaries()
    {
        var error = new InvalidDataException(Sensitive);
        V9CalibradorSafeFailureCategory.Mark(error,
            V9CalibradorSafeFailureCategory.PhysicalSqlProbe);
        V9CalibradorSafeFailureCategory.Mark(error,
            V9CalibradorSafeFailureCategory.PhysicalSqlFrame);
        Assert.That(V9CalibradorSafeFailureCategory.Classify(error),
            Is.EqualTo(V9CalibradorSafeFailureCategory.PhysicalSqlProbe));
        Assert.That(V9CalibradorSafeFailureCategory.Classify(error), Does.Not.Contain(Sensitive));
    }

    [Test]
    public void Frozen_holdout_and_replay_mismatch_are_fixed_distinct_categories()
    {
        var failedHoldout = new InvalidDataException(Sensitive);
        V9CalibradorSafeFailureCategory.Mark(failedHoldout,
            V9CalibradorSafeFailureCategory.PhysicalFrozenHoldout);
        Assert.That(V9CalibradorSafeFailureCategory.Classify(failedHoldout),
            Is.EqualTo("PHYSICAL_FROZEN_HOLDOUT"));
        var replay = new InvalidDataException(Sensitive);
        V9CalibradorSafeFailureCategory.Mark(replay,
            V9CalibradorSafeFailureCategory.PhysicalReplayMismatch);
        V9CalibradorSafeFailureCategory.Mark(replay,
            V9CalibradorSafeFailureCategory.PhysicalFrozenHoldout);
        Assert.That(V9CalibradorSafeFailureCategory.Classify(replay),
            Is.EqualTo("PHYSICAL_REPLAY_MISMATCH"));
    }

    [Test]
    public void Sql_probe_reasons_are_exact_allowlisted_codes_never_free_text()
    {
        var tagged = new InvalidDataException("Contagem da união SQL diverge da leitura do frame.");
        V9CalibradorSafeFailureCategory.Mark(tagged,
            V9CalibradorSafeFailureCategory.PhysicalSqlProbe);
        Assert.That(V9CalibradorSafeFailureCategory.PhysicalSqlProbeReason(tagged),
            Is.EqualTo("SQL_PAIR_COUNT_MISMATCH"));

        var malicious = new InvalidDataException("Contagem da união SQL diverge da leitura do frame. " + Sensitive);
        V9CalibradorSafeFailureCategory.Mark(malicious,
            V9CalibradorSafeFailureCategory.PhysicalSqlProbe);
        Assert.That(V9CalibradorSafeFailureCategory.PhysicalSqlProbeReason(malicious),
            Is.EqualTo("UNCLASSIFIED_PHYSICAL_SQL_PROBE"));
        Assert.That(V9CalibradorSafeFailureCategory.PhysicalSqlProbeReason(malicious),
            Does.Not.Contain(Sensitive));

        var outside = new InvalidDataException("Contagem da união SQL diverge da leitura do frame.");
        Assert.That(V9CalibradorSafeFailureCategory.PhysicalSqlProbeReason(outside),
            Is.EqualTo("NOT_PHYSICAL_SQL_PROBE"));
    }

    [Test]
    public void Unknown_sql_probe_exception_reports_only_allowlisted_checkpoint()
    {
        var error = new InvalidDataException(Sensitive);
        V9CalibradorSafeFailureCategory.Mark(error,
            V9CalibradorSafeFailureCategory.PhysicalSqlProbe);
        V9CalibradorSafeFailureCategory.MarkProbeCheckpoint(error, "PROBE_FS_FIT");
        Assert.That(V9CalibradorSafeFailureCategory.PhysicalSqlProbeReason(error),
            Is.EqualTo("UNCLASSIFIED_PHYSICAL_SQL_PROBE_AT_PROBE_FS_FIT"));
        Assert.That(V9CalibradorSafeFailureCategory.PhysicalSqlProbeReason(error),
            Does.Not.Contain(Sensitive));
        Assert.Throws<ArgumentException>((Action)(() =>
            V9CalibradorSafeFailureCategory.MarkProbeCheckpoint(error, Sensitive)));

        // An untrusted Data value is never echoed.
        error.Data["JornadaV9SafeProbeCheckpoint"] = Sensitive;
        Assert.That(V9CalibradorSafeFailureCategory.PhysicalSqlProbeReason(error),
            Is.EqualTo("UNCLASSIFIED_PHYSICAL_SQL_PROBE"));

        // Exact known reasons remain more precise than stage information.
        var known = new InvalidDataException("Contagem da união SQL diverge da leitura do frame.");
        V9CalibradorSafeFailureCategory.Mark(known,
            V9CalibradorSafeFailureCategory.PhysicalSqlProbe);
        V9CalibradorSafeFailureCategory.MarkProbeCheckpoint(known, "PROBE_SQL_READ");
        Assert.That(V9CalibradorSafeFailureCategory.PhysicalSqlProbeReason(known),
            Is.EqualTo("SQL_PAIR_COUNT_MISMATCH"));
    }

    [TestCase("FS_REQUEST_VALIDATE")]
    [TestCase("FS_TRAIN_FIT")]
    [TestCase("FS_FIT_VALIDATE")]
    [TestCase("FS_VALIDATION_EVALUATE")]
    [TestCase("FS_METRICS_VALIDATE")]
    public void Fs_fit_phase_checkpoint_is_allowlisted_without_leaking_exception(string checkpoint)
    {
        var error = new InvalidDataException(Sensitive);
        V9CalibradorSafeFailureCategory.Mark(error,
            V9CalibradorSafeFailureCategory.PhysicalSqlProbe);
        V9CalibradorSafeFailureCategory.MarkProbeCheckpoint(error, checkpoint);
        Assert.That(V9CalibradorSafeFailureCategory.PhysicalSqlProbeReason(error),
            Is.EqualTo("UNCLASSIFIED_PHYSICAL_SQL_PROBE_AT_" + checkpoint));
        Assert.That(V9CalibradorSafeFailureCategory.PhysicalSqlProbeReason(error),
            Does.Not.Contain(Sensitive));
    }

    [Test]
    public void Matches_exact_structural_frame_error_as_safe_category()
    {
        Assert.That(V9CalibradorSafeFailureCategory.Classify(
            new InvalidDataException("V9_SQL_COST_ORDER_DUPLICATE_STABLE_KEY")),
            Is.EqualTo("PHYSICAL_FRAME_ORDER"));
    }
}
