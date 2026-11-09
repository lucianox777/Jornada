using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9CalibrationExecutionResourceBudgetTests
{
    private const long G = V9CalibrationResourceGovernor.GiB;

    [Test]
    public void Automatic_mode_clamps_CPU_by_measured_memory_without_removing_hypotheses()
    {
        var standard = V9CalibrationResourceGovernor.Decide(0, 8, 9 * G, 7 * G, 11 * G);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(standard.EffectiveParallelism, Is.EqualTo(2)); // (7 - 2) / 2
            Assert.That(standard.SchedulingMode, Is.EqualTo("AUTO_CPU_AND_LIVE_RAM_BOUNDED"));
            Assert.That(standard.EffectiveAvailableMemoryBytes, Is.EqualTo(7 * G));
            Assert.That(standard.ResourceAdapted, Is.True);
            Assert.That(standard.TestUsed, Is.False);
            Assert.That(standard.GoldUsed, Is.False);
            Assert.That(standard.Activated, Is.False);
        }));
        var spacious = V9CalibrationResourceGovernor.Decide(0, 8, 32 * G, 30 * G, 10 * G);
        Assert.That(spacious.EffectiveParallelism, Is.EqualTo(8)); // CPU ceiling
        var one = V9CalibrationResourceGovernor.Decide(0, 16, 4 * G, null, 10 * G);
        Assert.That(one.EffectiveParallelism, Is.EqualTo(1));
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
        Assert.That((Action)(() => V9CalibrationResourceGovernor.Decide(0, 8, 3 * G, null, 12 * G)),
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
        Assert.That(constrained.EffectiveParallelism, Is.EqualTo(2));
        Assert.That(constrained.HostAvailableMemoryBytes, Is.EqualTo(14 * G));
        Assert.That(constrained.CgroupRemainingMemoryBytes, Is.EqualTo(6 * G));
        var explicitOne = V9CalibrationResourceGovernor.Decide(1, 16, 14 * G, 6 * G, 8 * G);
        Assert.That(explicitOne.EffectiveParallelism, Is.EqualTo(1));
    }
}
