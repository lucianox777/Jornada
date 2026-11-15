using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9CalibrationExecutionResourceBudgetTests
{
    private const long G = V9CalibrationResourceGovernor.GiB;

    [Test]
    public void Automatic_mode_uses_CPU_minus_one_with_live_memory_admission()
    {
        var standard = V9CalibrationResourceGovernor.Decide(0, 8, 9 * G, 7 * G, 11 * G);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(standard.EffectiveParallelism, Is.EqualTo(7)); // 8 logical CPUs minus one
            Assert.That(standard.SchedulingMode, Is.EqualTo("AUTO_CPU_MINUS_ONE_LIVE_RAM_ADMISSION"));
            Assert.That(standard.EffectiveAvailableMemoryBytes, Is.EqualTo(7 * G));
            Assert.That(standard.ResourceAdapted, Is.True);
            Assert.That(standard.TestUsed, Is.False);
            Assert.That(standard.GoldUsed, Is.False);
            Assert.That(standard.Activated, Is.False);
        }));
        var spacious = V9CalibrationResourceGovernor.Decide(0, 8, 32 * G, 30 * G, 10 * G);
        Assert.That(spacious.EffectiveParallelism, Is.EqualTo(7)); // CPU ceiling - 1
        var constrained = V9CalibrationResourceGovernor.Decide(0, 16, 4 * G, null, 10 * G);
        Assert.That(constrained.EffectiveParallelism, Is.EqualTo(15)); // live gate enforces memory
        Assert.That(V9CalibrationResourceGovernor.Decide(0, 1, 8 * G, null, 10 * G)
            .EffectiveParallelism, Is.EqualTo(1));
        Assert.That(V9CalibrationResourceGovernor.Decide(0, 2, 8 * G, null, 10 * G)
            .EffectiveParallelism, Is.EqualTo(1));
        Assert.That(standard.HostReserveBytes, Is.Zero);
        Assert.That(standard.PerWorkerReserveBytes, Is.Zero);
        Assert.That(V9CalibrationResourceGovernor.PreventiveAvailableMemoryBytes, Is.EqualTo(2 * G));
        Assert.That(V9CalibrationResourceGovernor.CriticalAvailableMemoryBytes, Is.EqualTo(G));
    }

    [Test]
    public void Explicit_positive_parallelism_remains_user_declared_and_audited()
    {
        foreach (var configured in new[] { 1, 2, 8 })
        {
            var budget = V9CalibrationResourceGovernor.Decide(configured, 4, 10 * G, null, 9 * G);
            Assert.That(budget.EffectiveParallelism, Is.EqualTo(configured));
            Assert.That(budget.SchedulingMode, Is.EqualTo("EXPLICIT_PLAN_UNCHANGED"));
            Assert.That(budget.ResourceAdapted, Is.False);
        }
    }

    [Test]
    public void Insufficient_RAM_or_disk_fails_closed_instead_of_silently_skipping_fits()
    {
        Assert.That((Action)(() => V9CalibrationResourceGovernor.Decide(0, 8, G, null, 12 * G)),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("RAM_BELOW"));
        Assert.That((Action)(() => V9CalibrationResourceGovernor.Decide(1, 8, 11 * G, null, 2 * G)),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("DISK_INSUFFICIENT"));
        Assert.That((Action)(() => V9CalibrationResourceGovernor.Decide(0, 8, null, null, null)),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("DISK_INSUFFICIENT"));
        Assert.That((Action)(() => V9CalibrationResourceGovernor.Decide(0, 0, 16 * G, null, 12 * G)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9CalibrationResourceGovernor.Decide(-1, 8, 16 * G, null, 12 * G)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9CalibrationResourceGovernor.Decide(0, 8, -1, null, 12 * G)),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Unknown_host_RAM_is_not_mistaken_for_measured_capacity()
    {
        var result = V9CalibrationResourceGovernor.Decide(0, 8, null, null, 11 * G);
        Assert.That(result.EffectiveParallelism, Is.EqualTo(1));
        Assert.That(result.EffectiveAvailableMemoryBytes, Is.Null);
        Assert.That(result.SchedulingMode, Is.EqualTo("AUTO_MEMORY_UNMEASURED_CONSERVATIVE_SINGLE_WORKER"));
    }

    [Test]
    public void Cgroup_remaining_is_hard_upper_bound_and_explicit_plan_is_unchanged()
    {
        var constrained = V9CalibrationResourceGovernor.Decide(0, 16, 14 * G, 6 * G, 8 * G);
        Assert.That(constrained.EffectiveParallelism, Is.EqualTo(15));
        Assert.That(constrained.HostAvailableMemoryBytes, Is.EqualTo(14 * G));
        Assert.That(constrained.CgroupRemainingMemoryBytes, Is.EqualTo(6 * G));
        var explicitOne = V9CalibrationResourceGovernor.Decide(1, 16, 14 * G, 6 * G, 8 * G);
        Assert.That(explicitOne.EffectiveParallelism, Is.EqualTo(1));
    }
}
