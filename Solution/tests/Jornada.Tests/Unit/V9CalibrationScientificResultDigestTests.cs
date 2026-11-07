using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9CalibrationScientificResultDigestTests
{
    private static string Sha(char c) => new(c, 64);

    // Unit-level deterministic orchestration canary: same small declared
    // scientific options, 1/0/8 local workers, intentionally out-of-order
    // completion. End-to-end SQL calibration still requires physical evidence.
    [Test]
    public async Task Parallelism_1_0_8_preserves_metrics_pending_reasons_fit_and_winner_digest()
    {
        async Task<string> Execute(int workers)
        {
            var inputs = Enumerable.Range(0, 12).Reverse().ToArray();
            var rows = await V9ParallelWork.MapAsync(inputs, workers, async (index, ct) =>
            {
                await Task.Delay((index % 5) * 3, ct);
                var reasons = new Dictionary<string, long>();
                if (index % 2 == 0)
                {
                    reasons.Add("SEM_CANDIDATO", index);
                    reasons.Add("CONFLITO", index % 3);
                }
                else
                {
                    reasons.Add("CONFLITO", index % 3);
                    reasons.Add("SEM_CANDIDATO", index);
                }
                return new V9CalibrationScientificResultRow(
                    "RS-" + (index / 3) + ":OPT-" + (index % 3), .5m,
                    10 + index, index % 3, 2 + index, index % 2,
                    reasons, Sha((char)('a' + index)));
            }, CancellationToken.None);
            return V9CalibrationScientificResultDigest.Compute(rows, "RS-0:OPT-0");
        }
        var serial = await Execute(1);
        var automatic = await Execute(0);
        var eight = await Execute(8);
        Assert.Multiple(() =>
        {
            Assert.That(automatic, Is.EqualTo(serial), "0 must be auto without changing scientific results");
            Assert.That(eight, Is.EqualTo(serial), "8 must not change scientific results");
        });
    }

    [Test]
    public void A_different_false_link_pending_reason_fit_or_winner_changes_the_digest()
    {
        var row = new V9CalibrationScientificResultRow("rs:opt", .5m,
            8, 1, 2, 1, new Dictionary<string, long> { ["SEM_CANDIDATO"] = 2 }, Sha('a'));
        var baseline = V9CalibrationScientificResultDigest.Compute([row], "rs:opt");
        Assert.Multiple(() =>
        {
            Assert.That(V9CalibrationScientificResultDigest.Compute([row with { FalsePositive = 2 }], "rs:opt"),
                Is.Not.EqualTo(baseline));
            Assert.That(V9CalibrationScientificResultDigest.Compute([row with
                { ReasonsByResolveStatus = new Dictionary<string, long> { ["SEM_CANDIDATO"] = 3 } }], "rs:opt"),
                Is.Not.EqualTo(baseline));
            Assert.That(V9CalibrationScientificResultDigest.Compute([row with { FittedSnapshotSha256 = Sha('b') }], "rs:opt"),
                Is.Not.EqualTo(baseline));
            Assert.That(V9CalibrationScientificResultDigest.Compute([row], "other-option"), Is.Not.EqualTo(baseline));
        });
    }
}
