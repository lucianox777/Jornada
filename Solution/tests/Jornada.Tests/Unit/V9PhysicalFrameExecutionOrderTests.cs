using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9PhysicalFrameExecutionOrderTests
{
    private sealed record Frame(string Key, IReadOnlyList<LinkageBlockingPass> Passes);
    private static LinkageBlockingPass Pass(string id, params string[] fields) =>
        LinkageBlockingPass.Create(id, fields);

    [Test]
    public void Broader_candidate_pair_frames_are_piloted_first_without_dropping_work()
    {
        Frame[] original =
        [
            new("z-narrow", [Pass("A", "NOME", "DATA_NASCIMENTO", "NOME_MAE")]),
            new("a-wide", [Pass("B", "NOME")]),
            new("x-two-pass", [Pass("C", "NOME"), Pass("D", "NOME_MAE")]),
            new("m-medium", [Pass("E", "NOME", "DATA_NASCIMENTO")])
        ];
        var ordered = V9PhysicalFrameExecutionOrder.BroadestLikelyFirst(original,
            x => x.Passes, x => x.Key).Select(x => x.Key).ToArray();
        Assert.That(ordered, Is.EqualTo(new[]
        {
            "x-two-pass", "a-wide", "m-medium", "z-narrow"
        }));
        Assert.That(ordered.OrderBy(x => x, StringComparer.Ordinal),
            Is.EqualTo(original.Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal)));
    }

    [Test]
    public void Stable_key_breaks_ties_independently_of_input_order_or_cpu()
    {
        Frame[] original =
        [
            new("Z", [Pass("A", "NOME")]),
            new("A", [Pass("B", "NOME_MAE")]),
            new("M", [Pass("C", "DATA_NASCIMENTO")])
        ];
        var first = V9PhysicalFrameExecutionOrder.BroadestLikelyFirst(original,
            x => x.Passes, x => x.Key).Select(x => x.Key).ToArray();
        var reversed = V9PhysicalFrameExecutionOrder.BroadestLikelyFirst(
            original.Reverse().ToArray(), x => x.Passes, x => x.Key)
            .Select(x => x.Key).ToArray();
        Assert.That(first, Is.EqualTo(new[] { "A", "M", "Z" }));
        Assert.That(reversed, Is.EqualTo(first));
        Assert.That(V9PhysicalFrameExecutionOrder.Strategy,
            Does.Contain("NOT_SQL_COST"));
    }

    [Test]
    public void Empty_invalid_and_duplicate_operational_frames_fail_closed()
    {
        Assert.Throws<InvalidDataException>(() =>
            V9PhysicalFrameExecutionOrder.StructuralBroadnessScore([]));
        Assert.Throws<InvalidDataException>(() =>
            V9PhysicalFrameExecutionOrder.StructuralBroadnessScore(
                [new LinkageBlockingPass("X", Array.Empty<string>())]));
        Frame[] duplicates =
        [
            new("same", [Pass("X", "NOME")]),
            new("same", [Pass("Y", "NOME", "NOME_MAE")])
        ];
        Assert.Throws<InvalidDataException>(() =>
            V9PhysicalFrameExecutionOrder.BroadestLikelyFirst(duplicates,
                x => x.Passes, x => x.Key));
    }
}
