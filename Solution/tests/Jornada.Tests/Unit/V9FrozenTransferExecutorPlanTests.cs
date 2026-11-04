using System.Text.Json;
using System.Text.Json.Serialization;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9FrozenTransferExecutorPlanTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static V9FrozenTransferExecutorPlan Committed()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Solution", "data", "reference",
                   "v9-tf-seed43-frozen-transfer", "executor.json")))
            current = current.Parent;
        Assert.That(current, Is.Not.Null, "executor.json pré-registrado não encontrado");
        var bytes = File.ReadAllBytes(Path.Combine(current!.FullName, "Solution", "data", "reference",
            "v9-tf-seed43-frozen-transfer", "executor.json"));
        return JsonSerializer.Deserialize<V9FrozenTransferExecutorPlan>(bytes, Json)!;
    }

    [Test]
    public void Committed_plan_freezes_six_seed42_arms_before_seed43()
    {
        var plan = Committed();
        Assert.DoesNotThrow((TestDelegate)(() => V9FrozenTransferCli.ValidatePlan(plan)));
        Assert.Multiple((TestDelegate)(() =>
        {
            Assert.That(plan.SourceRunId, Is.EqualTo(41900357031L));
            Assert.That(plan.SourceCommitSha, Is.EqualTo("935f704edd2bceb6a125ecfe532ad5b71f9c89b9"));
            Assert.That(plan.Arms.Count(a => a.Role.StartsWith("PRIMARY_", StringComparison.Ordinal)), Is.EqualTo(2));
            Assert.That(plan.Arms.Where(a => a.ConflictMargin == 0.5m).Select(a => a.OptionId),
                Is.EquivalentTo(new[] { V9FrozenTransferCli.ControlOption, V9FrozenTransferCli.TreatmentOption }));
            Assert.That(plan.EndToEndRepetitions, Is.GreaterThanOrEqualTo(20));
        }));
    }

    [Test]
    public void Rejects_refit_like_or_retargeted_mutations()
    {
        var plan = Committed();
        var mutations = new Func<V9FrozenTransferExecutorPlan, V9FrozenTransferExecutorPlan>[]
        {
            p => p with { TargetSeed = 42 },
            p => p with { SourceSeed = 43 },
            p => p with { EndToEndRepetitions = 0 },
            p => p with { PrimaryConflictMargin = 2m },
            p => p with { SourceArtifactName = "other" },
            p => p with { Version = "V2" },
            p => p with { Arms = p.Arms.Take(5).ToArray() },
            p => p with { Arms = p.Arms.Select((a, i) => i == 0 ? a with { OptionId = "TF_TWO_MF_ML_W1_M0_A0.5_T0.95" } : a).ToArray() },
            p => p with { Arms = p.Arms.Select((a, i) => i == 1 ? a with { SnapshotSha256 = p.Arms[0].SnapshotSha256 } : a).ToArray() },
            p => p with { Arms = p.Arms.Select(a => a with { Role = "PRIMARY_CONTROL" }).ToArray() },
            p => p with { Arms = p.Arms.Select((a, i) => i == 2 ? a with { ConflictMargin = 1m } : a).ToArray() },
            p => p with { SourcePlanTfSha256 = p.SourcePlanTfSha256.ToUpperInvariant() },
        };
        foreach (var mutate in mutations)
            Assert.Throws<InvalidDataException>((TestDelegate)(() => V9FrozenTransferCli.ValidatePlan(mutate(plan))));
    }

    [Test]
    public void Cli_refuses_wrong_arity_before_any_file_or_sql_access()
    {
        Assert.ThrowsAsync<ArgumentException>((AsyncTestDelegate)(() => V9FrozenTransferCli.RunAsync(["--v9-frozen-transfer", "a"])));
    }
}
